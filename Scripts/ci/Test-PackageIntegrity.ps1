param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../../.tools/package-integrity-tests'))
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Package-Integrity.ps1')
$testRoot = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ([Guid]::NewGuid().ToString('N'))
$fixture = Join-Path $testRoot 'repository'
$release = Join-Path $fixture 'TabularEditor/bin/Release'
$checks = [Collections.Generic.List[string]]::new()
function Check([bool]$Condition, [string]$Name) {
    if (!$Condition) { throw "FAIL: $Name" }
    $checks.Add($Name)
}
function Refused([scriptblock]$Action, [string]$Expected, [string]$Name) {
    $message = ''
    try { & $Action | Out-Null } catch { $message = $_.Exception.Message }
    Check ($message.Contains($Expected)) $Name
}
New-Item -ItemType Directory -Force $release, (Join-Path $fixture 'Scripts/ci'),
    (Join-Path $fixture 'docs/pbibench'), (Join-Path $fixture '.tools') | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Package-Integrity.ps1'), (Join-Path $PSScriptRoot 'Package-PbiBench.ps1') -Destination (Join-Path $fixture 'Scripts/ci')
foreach ($file in @('LICENSE', 'license-FastColoredTextbox.txt', 'license-FastWildcardMatching.txt',
    'license-TreeViewAdv.txt', 'TabularEditor-license.rtf', 'docs/pbibench/GETTING_STARTED.md', 'docs/pbibench/MCP_COMPANION_V1.md')) {
    Set-Content -LiteralPath (Join-Path $fixture $file) -Value "Synthetic notice: $file"
}
Set-Content -LiteralPath (Join-Path $fixture '.gitignore') -Value @('TabularEditor/bin/', '.tools/')
git -C $fixture init --quiet
git -C $fixture add .
git -C $fixture -c user.name=PackageTest -c user.email=package-test@example.invalid commit --quiet -m 'Synthetic package fixture'
if ($LASTEXITCODE -ne 0) { throw 'Fixture Git setup failed.' }
foreach ($file in @('TabularEditor.exe', 'TabularEditor.exe.config', 'PbiBench.Core.dll', 'private-query.dax', 'private-model.bim', 'settings.json', 'build.log', 'debug.pdb')) {
    Set-Content -LiteralPath (Join-Path $release $file) -Value "Synthetic runtime fixture: $file"
}
$receipt = Join-Path $fixture '.tools/verified-build.json'
Refused { Assert-VerifiedPackageBuild $fixture $receipt } 'No successful build receipt' 'Missing verification rejected'
$identity = [pscustomobject]@{
    schema = 'pbibench.verified-build/1'; sourceCommit = (git -C $fixture rev-parse HEAD).Trim(); cleanSource = $true
    files = @(Get-HostRuntimeInventory $release)
}
$identity | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $receipt
Check ($identity.files.Count -eq 3) 'Only runtime extensions selected'
$null = Assert-VerifiedPackageBuild $fixture $receipt
$checks.Add('Matching clean build accepted')
$hostPath = Join-Path $release 'TabularEditor.exe'
$original = [IO.File]::ReadAllBytes($hostPath)
Add-Content -LiteralPath $hostPath -Value 'changed'
Refused { Assert-VerifiedPackageBuild $fixture $receipt } 'Runtime changed' 'Changed binary rejected'
[IO.File]::WriteAllBytes($hostPath, $original)
Set-Content -LiteralPath (Join-Path $release 'extra.dll') -Value 'extra'
Refused { Assert-VerifiedPackageBuild $fixture $receipt } 'inventory changed' 'Added runtime rejected'
Remove-Item -LiteralPath (Join-Path $release 'extra.dll')
Set-Content -LiteralPath (Join-Path $fixture 'untracked.cs') -Value '// pending source'
Refused { Assert-VerifiedPackageBuild $fixture $receipt } 'source must be clean' 'Untracked source rejected'
Remove-Item -LiteralPath (Join-Path $fixture 'untracked.cs')
Add-Content -LiteralPath (Join-Path $fixture 'LICENSE') -Value 'changed'
Refused { Assert-VerifiedPackageBuild $fixture $receipt } 'source must be clean' 'Dirty tracked source rejected'
git -C $fixture add LICENSE
git -C $fixture -c user.name=PackageTest -c user.email=package-test@example.invalid commit --quiet -m 'Change source identity'
Refused { Assert-VerifiedPackageBuild $fixture $receipt } 'does not match' 'Different source commit rejected'
$identity.sourceCommit = (git -C $fixture rev-parse HEAD).Trim()
$identity | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $receipt
$out = Join-Path $testRoot 'portable'
& (Join-Path $fixture 'Scripts/ci/Package-PbiBench.ps1') -OutDirectory $out -SkipMcp
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Get-ChildItem -LiteralPath $out -Filter '*.zip' | Select-Object -First 1
$archive = [IO.Compression.ZipFile]::OpenRead($zip.FullName)
try {
    $names = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    foreach ($notice in @('license-FastColoredTextbox.txt', 'license-FastWildcardMatching.txt', 'license-TreeViewAdv.txt', 'TabularEditor-license.rtf')) {
        Check ($names -contains "licenses/$notice") "Preserved $notice"
    }
    Check (@($names | Where-Object { $_ -match 'private-|settings.json|build.log|debug.pdb' }).Count -eq 0) 'Scratch and private files excluded from zip'
    Check ($names -contains 'verified-build.json') 'Build identity travels with zip'
    $manifest = @(Get-Content -LiteralPath (Join-Path $out 'MANIFEST.txt'))
    Check ($manifest.Count -eq $archive.Entries.Count - 1) 'Manifest covers every entry except itself'
    foreach ($line in $manifest) {
        $hash, $path = $line -split '  ', 2
        $entry = $archive.Entries | Where-Object { $_.FullName.Replace('\', '/') -ceq $path }
        $stream = $entry.Open()
        $algorithm = [Security.Cryptography.SHA256]::Create()
        try { $actual = ([BitConverter]::ToString($algorithm.ComputeHash($stream)) -replace '-', '').ToLowerInvariant() }
        finally { $algorithm.Dispose(); $stream.Dispose() }
        if ($hash -ne $actual) { throw "Manifest mismatch: $path" }
    }
    $checks.Add('Every archived byte matches manifest')
} finally { $archive.Dispose() }
Refused { & (Join-Path $fixture 'Scripts/ci/Package-PbiBench.ps1') -OutDirectory $out -SkipMcp } 'never overwritten' 'Existing distribution preserved'
[pscustomobject]@{ result = 'PASS'; count = $checks.Count; checks = @($checks); evidenceDirectory = $testRoot } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $testRoot 'result.json')
Write-Host "Package integrity: $($checks.Count) checks PASS ($testRoot)"
