# Searches files (and the entries of zip files) for the real sp-writer client id, secret and tenant id,
# as UTF-8 and UTF-16LE. Prints file names and counts only, never the values.
param([string]$PathList)
$Paths = $PathList -split ';'
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$cred = powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'D:\PROJ\datapass-fabric-bi\.claude\worktrees\exciting-easley-f14c68\tests\native\fabric\cred-read.ps1' -Target 'dpf-test-sp-writer' | ConvertFrom-Json
$tid = & 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd' account show --query tenantId -o tsv
$needles = @(@{ n = 'clientId'; v = $cred.user }, @{ n = 'secret'; v = $cred.secret }, @{ n = 'tenantId'; v = $tid })
$cred = $null
$pats = foreach ($x in $needles) { foreach ($enc in [Text.Encoding]::UTF8, [Text.Encoding]::Unicode) { @{ n = $x.n; b = $enc.GetBytes($x.v) } } }
function Has([byte[]]$hay, [byte[]]$nd) {
  $first = $nd[0]; $max = $hay.Length - $nd.Length
  for ($i = [Array]::IndexOf($hay, $first); $i -ge 0 -and $i -le $max; $i = [Array]::IndexOf($hay, $first, $i + 1)) {
    $ok = $true; for ($j = 1; $j -lt $nd.Length; $j++) { if ($hay[$i + $j] -ne $nd[$j]) { $ok = $false; break } }
    if ($ok) { return $true }
  }
  return $false
}
$items = New-Object System.Collections.Generic.List[object]
foreach ($p in $Paths) {
  foreach ($f in Get-ChildItem $p -Recurse -File) {
    $items.Add(@{ name = $f.FullName; bytes = [IO.File]::ReadAllBytes($f.FullName) })
    if ($f.Extension -eq '.zip') {
      $z = [IO.Compression.ZipFile]::OpenRead($f.FullName)
      foreach ($e in $z.Entries) { $s = $e.Open(); $m = New-Object IO.MemoryStream; $s.CopyTo($m); $s.Close(); $items.Add(@{ name = "$($f.Name)!$($e.FullName)"; bytes = $m.ToArray() }) }
      $z.Dispose()
    }
  }
}
$hits = 0
foreach ($it in $items) { foreach ($pt in $pats) { if (Has $it.bytes $pt.b) { $hits++; "HIT $($pt.n) in $($it.name)" } } }
"real-value scan: $($items.Count) files/entries, $hits hits"

