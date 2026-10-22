# Shared by verification and packaging. Only runtime files are eligible for redistribution.
function Get-PackageSha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $algorithm = [Security.Cryptography.SHA256]::Create()
        try { return ([BitConverter]::ToString($algorithm.ComputeHash($stream)) -replace '-', '').ToLowerInvariant() }
        finally { $algorithm.Dispose() }
    } finally { $stream.Dispose() }
}

function Get-HostRuntimeInventory([string]$Release) {
    $base = [IO.Path]::GetFullPath($Release).TrimEnd('\', '/')
    Get-ChildItem -LiteralPath $base -Recurse -File | Where-Object {
        $_.Extension -in '.dll', '.exe', '.config' -and $_.Name -notlike '*.vshost.*'
    } | Sort-Object FullName | ForEach-Object {
        [pscustomobject]@{
            path = $_.FullName.Substring($base.Length + 1).Replace('\', '/')
            sha256 = Get-PackageSha256 $_.FullName
        }
    }
}

function Assert-VerifiedPackageBuild([string]$Root, [string]$ReceiptPath) {
    if (!(Test-Path -LiteralPath $ReceiptPath -PathType Leaf)) { throw 'No successful build receipt. Run Verify-PbiBench.ps1 first.' }
    $commit = (git -C $Root rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify package source commit.' }
    $changes = @(git -C $Root status --porcelain --untracked-files=normal)
    if ($LASTEXITCODE -ne 0 -or $changes.Count -gt 0) { throw 'Package source must be clean, including untracked source files.' }
    $receipt = Get-Content -LiteralPath $ReceiptPath -Raw | ConvertFrom-Json
    if ($receipt.schema -ne 'pbibench.verified-build/1' -or $receipt.sourceCommit -ne $commit -or !$receipt.cleanSource) {
        throw 'Build receipt does not match this clean source commit. Verify again before packaging.'
    }
    $actual = @(Get-HostRuntimeInventory (Join-Path $Root 'TabularEditor/bin/Release'))
    if ($actual.Count -eq 0 -or $actual.Count -ne @($receipt.files).Count) { throw 'Runtime file inventory changed since verification.' }
    foreach ($file in $actual) {
        $expected = @($receipt.files | Where-Object { $_.path -ceq $file.path })
        if ($expected.Count -ne 1 -or $expected[0].sha256 -ne $file.sha256) { throw "Runtime changed since verification: $($file.path)" }
    }
    return $receipt
}
