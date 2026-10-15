# Creates (or reuses) workspace $Name on the test trial capacity with Julian's CLI session (admin) and grants
# sp-writer Contributor. Prints only names, roles and booleans: no ids, no secrets.
param([Parameter(Mandatory)][string]$Name)
$ErrorActionPreference = 'Stop'
$az = 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd'
$tok = & $az account get-access-token --resource https://api.fabric.microsoft.com --query accessToken -o tsv
$h = @{ Authorization = "Bearer $tok" }
$F = 'https://api.fabric.microsoft.com/v1'
$capName = [Environment]::GetEnvironmentVariable('DPF_TEST_CAPACITY_NAME', 'User')
if ($Name -notmatch '^dpf-test-pbibench-\d{8}$') { throw "refusing workspace name $Name" }
$cap = (Invoke-RestMethod "$F/capacities" -Headers $h).value | ? { $_.displayName -eq $capName }
if (-not $cap) { throw "capacity not found" }
"capacity: $($cap.displayName) sku=$($cap.sku) state=$($cap.state)"
$ws = (Invoke-RestMethod "$F/workspaces" -Headers $h).value | ? { $_.displayName -eq $Name }
if ($ws) { "workspace exists: $Name (reused)" }
else {
  $ws = Invoke-RestMethod "$F/workspaces" -Method Post -Headers $h -ContentType 'application/json' -Body (@{ displayName = $Name; capacityId = $cap.id; description = 'PbiBench remote XMLA check (TabularEditor_J); test only' } | ConvertTo-Json)
  "workspace created: $Name"
}
$ws = Invoke-RestMethod "$F/workspaces/$($ws.id)" -Headers $h
"onCapacity: $($ws.capacityId -eq $cap.id)"
$cred = powershell.exe -NoProfile -ExecutionPolicy Bypass -File '<datapass-fabric-bi>\tests\native\fabric\cred-read.ps1' -Target 'dpf-test-sp-writer' | ConvertFrom-Json
$spObj = & $az ad sp show --id $cred.user --query id -o tsv
$cred = $null
$ra = (Invoke-RestMethod "$F/workspaces/$($ws.id)/roleAssignments" -Headers $h).value
if ($ra | ? { $_.principal.id -eq $spObj }) { "sp-writer already assigned: " + ($ra | ? { $_.principal.id -eq $spObj }).role }
else {
  [void](Invoke-RestMethod "$F/workspaces/$($ws.id)/roleAssignments" -Method Post -Headers $h -ContentType 'application/json' -Body (@{ principal = @{ id = $spObj; type = 'ServicePrincipal' }; role = 'Contributor' } | ConvertTo-Json))
  "sp-writer assigned: Contributor"
}
$ra = (Invoke-RestMethod "$F/workspaces/$($ws.id)/roleAssignments" -Headers $h).value
"role assignments: " + (($ra | % { "$($_.principal.type):$($_.role)" }) -join ', ')
