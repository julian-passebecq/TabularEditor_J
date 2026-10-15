# Raw ADOMD reference leg (not the product): runs each query alone through fabric-bi's xmla.ps1 as sp-writer and
# writes results-remote-adomd.json. The secret goes on STDIN only; output is redacted for ids.
param([string]$Workspace = 'dpf-test-pbibench-20261015', [string]$Database = 'Gate')
$ErrorActionPreference = 'Stop'
$S = Split-Path $PSScriptRoot
$fx = '<datapass-fabric-bi>\tests\native\fabric'
$cred = powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$fx\cred-read.ps1" -Target 'dpf-test-sp-writer' | ConvertFrom-Json
$tid = & 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd' account show --query tenantId -o tsv
$qs = (Get-Content "$S\ev\remote\results-remote.json" -Raw | ConvertFrom-Json) | % { $_.query }
$qs += 'EVALUATE ROW("ok", 1)'
$out = @(); $i = 0
foreach ($q in $qs) {
  $i++
  $req = @{ workspace = $Workspace; database = $Database; app = @{ clientId = $cred.user; secret = $cred.secret; tenantId = $tid }; commands = @(); queries = @($q) } | ConvertTo-Json -Depth 5 -Compress
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $r = $req | powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$fx\xmla.ps1" | Out-String | ConvertFrom-Json
  $err = if ($r.error) { $r.error.Replace($cred.user, '<redacted>').Replace($tid, '<redacted>') } else { $null }
  $res = if ($r.ok) { $r.results[0] } else { $null }
  $out += [ordered]@{ id = "Q$i"; query = $q; columns = $res.columns; rows = $res.rows; error = $err; ms = $sw.ElapsedMilliseconds }
  "Q$i ok=$($r.ok) rows=$(@($res.rows).Count) $($sw.ElapsedMilliseconds) ms $err"
}
$cred = $null
$out | ConvertTo-Json -Depth 8 | Set-Content "$S\ev\remote\results-remote-adomd.json"
