# Endpoint stability probe (raw ADOMD, DAX Studio's ADOMD 19.x): N fresh connections, each opens catalog Gate and runs
# Q1, plus N fresh catalog-less connections listing databases ($SYSTEM.DBSCHEMA_CATALOGS). Prints counts only.
param([int]$N = 10, [string]$Workspace = 'dpf-test-pbibench-20261015', [string]$Catalog = 'Gate', [string]$Query = 'EVALUATE ROW("Total", [Total Amount])', [string]$Expect = '35')
$ErrorActionPreference = 'Stop'
Add-Type -Path "$env:ProgramFiles\DAX Studio\bin\Microsoft.AnalysisServices.AdomdClient.dll"
$fx = '<datapass-fabric-bi>\tests\native\fabric'
$cred = powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$fx\cred-read.ps1" -Target 'dpf-test-sp-writer' | ConvertFrom-Json
$tid = & 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd' account show --query tenantId -o tsv
$base = "Data Source=powerbi://api.powerbi.com/v1.0/myorg/$Workspace;User ID=app:$($cred.user)@$tid;Password=$($cred.secret)"
$okQ = 0; $okL = 0; $errs = @{}
for ($i = 0; $i -lt $N; $i++) {
  foreach ($kind in 'query', 'list') {
    $c = New-Object Microsoft.AnalysisServices.AdomdClient.AdomdConnection
    try {
      if ($kind -eq 'query') {
        $c.ConnectionString = "$base;Initial Catalog=$Catalog"; $c.Open()
        $cmd = $c.CreateCommand(); $cmd.CommandText = $Query; $r = $cmd.ExecuteReader(); [void]$r.Read(); $v = $r.GetValue(0); $r.Close()
        if ("$v" -eq $Expect) { $okQ++ } else { $errs["value $v"]++ }
      } else {
        $c.ConnectionString = $base; $c.Open()
        $cmd = $c.CreateCommand(); $cmd.CommandText = 'SELECT [CATALOG_NAME] FROM $SYSTEM.DBSCHEMA_CATALOGS'; $r = $cmd.ExecuteReader(); $names = @(); while ($r.Read()) { $names += $r.GetValue(0) }; $r.Close()
        if ($names -contains $Catalog) { $okL++ } else { $errs["list [$($names -join ';')]"]++ }
      }
    } catch { $m = ($_.Exception.Message -split "`n")[0] -replace [regex]::Escape($cred.user), '<redacted>'; $errs["$kind $m"]++ }
    finally { $c.Close() }
  }
}
$cred = $null
"$(Get-Date -Format HH:mm) query ok $okQ/$N, list-contains-catalog ok $okL/$N"
$errs.GetEnumerator() | % { "  $($_.Value)x $($_.Key)" }
