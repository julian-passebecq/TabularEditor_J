param(
    [string]$Server = 'localhost\MSSQLSERVERTABU',
    [string]$Database = ('tmp_te2_' + (Get-Date -Format 'yyyyMMdd'))
)
# DAX-006 live gate. Local only, never in CI. Creates the throwaway database $Database (must not exist), runs the
# host smoke against it through the real TE2 connection and PbiBench AMO executor, and drops it. Build first with
# Scripts/Verify-PbiBench.ps1. Windows integrated auth; no credentials are read or stored.
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot '..\..\PbiBench.Host.Smoke\bin\Release\net48\PbiBench.Host.Smoke.exe'
if (-not (Test-Path $exe)) { throw 'Build the host smoke first (Scripts/Verify-PbiBench.ps1).' }
$env:PBIBENCH_LIVE_SERVER = $Server; $env:PBIBENCH_LIVE_DATABASE = $Database
try { & $exe; exit $LASTEXITCODE } finally { Remove-Item Env:PBIBENCH_LIVE_SERVER, Env:PBIBENCH_LIVE_DATABASE -ErrorAction SilentlyContinue }
