# Feeds the sp-writer credential (Credential Manager) and tenant id (CLI session) to the harness on STDIN only.
param([string]$Workspace = 'dpf-test-pbibench-20261015', [string]$Database = 'Gate')
$ErrorActionPreference = 'Stop'
$S = Split-Path $PSScriptRoot
$cred = powershell.exe -NoProfile -ExecutionPolicy Bypass -File '<datapass-fabric-bi>\tests\native\fabric\cred-read.ps1' -Target 'dpf-test-sp-writer' | ConvertFrom-Json
$tid = & 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd' account show --query tenantId -o tsv
$req = @{ clientId = $cred.user; secret = $cred.secret; tenantId = $tid; workspace = $Workspace; database = $Database
  fixture = '<repo>\handover\2026-10-15\fixture\Gate.SemanticModel\definition'
  pkg = "$S\te2"; ref = "$S\ref"; out = "$S\ev\remote" } | ConvertTo-Json -Compress
$cred = $null
$req | & "$S\harness\bin\PbiBench.Host.Smoke.exe"
"exit $LASTEXITCODE"
