param(
    [Parameter(Mandatory)][string]$OutDirectory,
    [string]$DotNetPath = 'dotnet',
    [switch]$SkipMcp
)
# Portable distribution of TE2 Enhanced from an already built Release tree (run Verify-PbiBench.ps1
# first). Produces a zip, SHA256SUMS.txt and source-identity.json. This is a build artifact for
# qualification, not a signed release.
$ErrorActionPreference = 'Stop'
# .NET SHA256 instead of Get-FileHash: a powershell.exe started from PowerShell 7 can inherit a PSModulePath
# where Microsoft.PowerShell.Utility (Get-FileHash) does not load (Light QA D12).
function Get-Sha256([string]$Path) {
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try { return ([System.BitConverter]::ToString($sha.ComputeHash($stream)) -replace '-', '').ToLowerInvariant() }
        finally { $sha.Dispose() }
    } finally { $stream.Dispose() }
}
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$release = Join-Path $root 'TabularEditor/bin/Release'
$exe = Join-Path $release 'TabularEditor.exe'
if (!(Test-Path $exe)) { throw "Build the Release host first: $exe is missing." }

$sha = (git -C $root rev-parse HEAD).Trim()
$short = $sha.Substring(0, 8)
$dirty = [bool](git -C $root status --porcelain --untracked-files=no)
New-Item -ItemType Directory -Force $OutDirectory | Out-Null
$stage = Join-Path $OutDirectory "stage-$short"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $stage | Out-Null

# Host: everything the Release build produced except XML docs and debug symbols.
Get-ChildItem $release -Recurse -File |
    Where-Object { $_.Extension -notin '.xml', '.pdb' -and $_.Name -notlike '*.vshost.*' } |
    ForEach-Object {
        $relative = $_.FullName.Substring($release.Length).TrimStart('\', '/')
        $target = Join-Path $stage $relative
        New-Item -ItemType Directory -Force (Split-Path $target -Parent) | Out-Null
        Copy-Item $_.FullName $target
    }

if (!$SkipMcp) {
    # Separate read-only MCP companion (framework-dependent, needs any .NET 8+ runtime).
    & $DotNetPath publish (Join-Path $root 'TE2Enhanced.Mcp/TE2Enhanced.Mcp.csproj') -c Release -o (Join-Path $stage 'mcp') --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "MCP companion publish failed ($LASTEXITCODE)." }
    Get-ChildItem (Join-Path $stage 'mcp') -Filter *.pdb | Remove-Item
}

Copy-Item (Join-Path $root 'LICENSE') (Join-Path $stage 'LICENSE.txt')
Copy-Item (Join-Path $root 'docs/pbibench/GETTING_STARTED.md') (Join-Path $stage 'GETTING-STARTED.md')
New-Item -ItemType Directory -Force (Join-Path $stage 'docs') | Out-Null
Copy-Item (Join-Path $root 'docs/pbibench/MCP_COMPANION_V1.md') (Join-Path $stage 'docs/MCP_COMPANION_V1.md')

# Third-party notices generated from the shipped binaries themselves (product, company, copyright, version),
# so the list cannot drift from the zip content. TE2 itself is MIT (LICENSE.txt).
$notices = @(
    'TE2 Enhanced - third-party components shipped in this portable build.'
    'Generated from file version resources at package time. TE2 Enhanced and Tabular Editor 2 are MIT licensed (LICENSE.txt).'
    'Microsoft.AnalysisServices* client libraries are redistributed as in upstream Tabular Editor 2 under their Microsoft license terms.'
    'Localized <culture>\*.resources.dll satellites belong to the components listed below; runtimes\*\msalruntime*.dll belong to the Microsoft Authentication Library broker.'
    ''
)
Get-ChildItem $stage -Recurse -File -Include *.dll, *.exe |
    Where-Object { $_.Name -notmatch '^(TabularEditor|TOMWrapper|PbiBench\.|TE2Enhanced\.)' -and $_.Name -notlike '*.resources.dll' } |
    ForEach-Object {
        $v = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($_.FullName)
        $relative = $_.FullName.Substring($stage.Length).TrimStart('\', '/')
        $notices += '{0} | {1} | {2} | {3} | {4}' -f $relative, $v.ProductName, $v.CompanyName, $v.LegalCopyright, $v.FileVersion
    }
$notices | Set-Content (Join-Path $stage 'THIRD-PARTY-NOTICES.txt') -Encoding UTF8
@"
TE2 Enhanced (Tabular Editor 2 fork) - portable build $short
Source: https://github.com/julian-passebecq/TabularEditor_J commit $sha
Run TabularEditor.exe (requires .NET Framework 4.8). PbiBench features are under Tools > PbiBench.
mcp\TE2Enhanced.Mcp.exe is an optional read-only MCP companion (requires any .NET 8+ runtime);
see docs/MCP_COMPANION_V1.md. Start with GETTING-STARTED.md. Not signed; qualification build, not a release.
"@ | Set-Content (Join-Path $stage 'README-TE2Enhanced.txt') -Encoding UTF8

# Per-file manifest (inside the zip and next to it) so an extracted folder can be checked file by file.
$manifest = Get-ChildItem $stage -Recurse -File | Sort-Object FullName | ForEach-Object {
    '{0}  {1}' -f (Get-Sha256 $_.FullName), $_.FullName.Substring($stage.Length).TrimStart('\', '/').Replace('\', '/')
}
$manifest | Set-Content (Join-Path $OutDirectory 'MANIFEST.txt') -Encoding ASCII
$manifest | Set-Content (Join-Path $stage 'MANIFEST.txt') -Encoding ASCII

$zip = Join-Path $OutDirectory "TE2Enhanced-$short-portable.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$hashes = @($zip, $exe, (Join-Path $stage 'mcp/TE2Enhanced.Mcp.dll'), (Join-Path $stage 'PbiBench.Core.dll')) |
    Where-Object { Test-Path $_ } |
    ForEach-Object { '{0}  {1}' -f (Get-Sha256 $_), (Split-Path $_ -Leaf) }
$hashes | Set-Content (Join-Path $OutDirectory 'SHA256SUMS.txt') -Encoding ASCII

[pscustomobject]@{
    hostFileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion
    sourceCommit = $sha; dirtyTrackedFiles = $dirty; zip = (Split-Path $zip -Leaf)
    zipBytes = (Get-Item $zip).Length; mcpIncluded = -not $SkipMcp
    note = 'Build timestamps make binaries non-reproducible; hashes identify this build only.'
} | ConvertTo-Json | Set-Content (Join-Path $OutDirectory 'source-identity.json') -Encoding UTF8
Remove-Item -Recurse -Force $stage
$hashes | ForEach-Object { Write-Host $_ }
