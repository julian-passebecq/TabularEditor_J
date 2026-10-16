param([int]$Spid)
Add-Type -Path '<repo>/TabularEditor/bin/Release/Microsoft.AnalysisServices.Tabular.dll'
$s = New-Object Microsoft.AnalysisServices.Tabular.Server; $s.Connect("Data Source=localhost")
$sw = [Diagnostics.Stopwatch]::StartNew()
$null = $s.Execute("<Cancel xmlns=`"http://schemas.microsoft.com/analysisservices/2003/engine`"><SPID>$Spid</SPID><CancelAssociated>true</CancelAssociated></Cancel>")
"cancel sent {0} ms" -f $sw.ElapsedMilliseconds
while ($sw.ElapsedMilliseconds -lt 120000) {
  $res = $null; $found = $false; $line = ''
  $r = $s.ExecuteReader('<Statement>SELECT SESSION_SPID, COMMAND_ELAPSED_TIME_MS, COMMAND_END_TIME FROM $SYSTEM.DISCOVER_COMMANDS</Statement>', [ref]$res, $null)
  while ($r.Read()) { if ([int]$r[0] -eq $Spid) { $found = $true; $line = "el=$($r[1]) end=$($r[2])" } }
  $r.Close()
  if (-not $found -or $line -notmatch 'end=$') { "server stop seen at {0} ms: {1}" -f $sw.ElapsedMilliseconds, $(if ($found) { $line } else { 'row gone' }); break }
  Start-Sleep -Milliseconds 250
}
$s.Disconnect()
