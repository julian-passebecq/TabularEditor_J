param([string]$Pbip, [string]$Tag)
$ErrorActionPreference = 'Stop'
$w = Split-Path $PSCommandPath
$t = "$w\flaui\bin\PilotDesk.exe"
$before = @(Get-Process PBIDesktop -ErrorAction SilentlyContinue).Id
Start-Process "$env:LOCALAPPDATA\Microsoft\WindowsApps\PBIDesktopStore.exe" -ArgumentList "`"$Pbip`""
for ($i = 0; $i -lt 30; $i++) { Start-Sleep 2; $p = Get-CimInstance Win32_Process -Filter "Name='PBIDesktop.exe'" | ? { $before -notcontains $_.ProcessId -and $_.CommandLine -match [regex]::Escape($Pbip) }; if ($p) { break } }
if (-not $p) { throw 'no PBIDesktop' }
$procId = $p.ProcessId
for ($i = 0; $i -lt 60; $i++) { $ms = Get-CimInstance Win32_Process -Filter "Name='msmdsrv.exe' AND ParentProcessId=$procId"; if ($ms) { break }; Start-Sleep 2 }
$ws = [regex]::Match($ms.CommandLine, '-s "([^"]+)"').Groups[1].Value
for ($i = 0; $i -lt 30; $i++) { if (Test-Path "$ws\msmdsrv.port.txt") { break }; Start-Sleep 2 }
$port = (Get-Content "$ws\msmdsrv.port.txt" -Raw -Encoding Unicode).Trim()
& $t wait $procId 'Text name=\[35\]' 120 | Out-Host
$db = (& $t dbs "localhost:$port" | Select-Object -First 1).Split("`t")[0]
$info = [ordered]@{ tag = $Tag; pbiPid = $procId; msmdsrvPid = $ms.ProcessId; port = [int]$port; db = $db; workspace = Split-Path $ws -Parent }
$info | ConvertTo-Json | Set-Content "$w\inst-$Tag.json"
$info | ConvertTo-Json -Compress

