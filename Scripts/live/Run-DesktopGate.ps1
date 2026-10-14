param(
    [Parameter(Mandatory = $true)][int]$Port,
    [ValidateSet('live', 'disk')][string]$Phase = 'live',
    [string]$TmdlFolder
)
# Power BI Desktop gate. Local only, never in CI. Open a disposable copy of handover/2026-10-15/fixture/Gate.pbip in
# Power BI Desktop first; $Port is the port in <workspace>\Data\msmdsrv.port.txt of that Desktop instance.
#   live: TE2 connects, writes the measure "TE2 Gate Double", queries it, cancels a slow query, prints the DAX Studio args.
#   disk: after saving the project in Desktop, TE2 reloads $TmdlFolder (Gate.SemanticModel\definition) and finds the measure.
# Build first with Scripts/Verify-PbiBench.ps1. Windows integrated auth; no credentials are read or stored.
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot '..\..\PbiBench.Host.Smoke\bin\Release\net48\PbiBench.Host.Smoke.exe'
if (-not (Test-Path $exe)) { throw 'Build the host smoke first (Scripts/Verify-PbiBench.ps1).' }
$env:PBIBENCH_DESKTOP_SERVER = "localhost:$Port"; $env:PBIBENCH_DESKTOP_PHASE = $Phase
if ($TmdlFolder) { $env:PBIBENCH_DESKTOP_TMDL = $TmdlFolder }
try { & $exe; exit $LASTEXITCODE }
finally { Remove-Item Env:PBIBENCH_DESKTOP_SERVER, Env:PBIBENCH_DESKTOP_PHASE, Env:PBIBENCH_DESKTOP_TMDL -ErrorAction SilentlyContinue }
