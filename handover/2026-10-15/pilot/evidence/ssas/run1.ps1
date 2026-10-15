$ErrorActionPreference='Stop'
$pkg='<scratch>\te2'
$ev='<scratch>\evidence\ssas'
$fx='<repo>\handover\2026-10-15\fixture\Gate.SemanticModel\definition'
$DB='tmp_pbibench-pilot_20261015'; $HOSTN='localhost'
foreach($d in 'Microsoft.AnalysisServices.Core','Microsoft.AnalysisServices','Microsoft.AnalysisServices.Tabular.Json','Microsoft.AnalysisServices.Tabular'){[void][Reflection.Assembly]::LoadFrom("$pkg\$d.dll")}
Add-Type -AssemblyName 'Microsoft.AnalysisServices.AdomdClient, Version=15.0.0.0, Culture=neutral, PublicKeyToken=89845dcd8080cc91'
$T='Microsoft.AnalysisServices.Tabular'
$tsrv=New-Object "$T.Server"; $tsrv.Connect("Data Source=$HOSTN")
$before=@($tsrv.Databases|%{$_.Name}|sort)
"before: [$($before -join ';')]"
if(-not (Test-Path "$ev\dblist-before.json")){ ConvertTo-Json -InputObject @($before) -Compress | Set-Content "$ev\dblist-before.json" }
if($tsrv.Databases.FindByName($DB)){ throw 'tmp db already exists' }
$m=[Microsoft.AnalysisServices.Tabular.TmdlSerializer]::DeserializeDatabaseFromFolder($fx)
$m.Name=$DB; $m.ID=$DB; "fixture dsv=$($m.Model.DefaultPowerBIDataSourceVersion)"; $m.Model.DefaultPowerBIDataSourceVersion=[Microsoft.AnalysisServices.Tabular.PowerBIDataSourceVersion]::PowerBI_V1
"fixture compat=$($m.CompatibilityLevel)"
$tsrv.Databases.Add($m); $m.Update('ExpandFull')
$m.Model.RequestRefresh('Full'); $m.Model.SaveChanges() | out-null
"created, state=$($tsrv.Databases.GetByName($DB).State) compat=$($tsrv.Databases.GetByName($DB).CompatibilityLevel)"
$tsrv.Disconnect()
$qs=@(
'EVALUATE ROW("Total", [Total Amount])',
'EVALUATE SUMMARIZECOLUMNS(Sales[Region], "Total", [Total Amount]) ORDER BY Sales[Region]',
'EVALUATE ROW("Rows", COUNTROWS(Sales), "Avg", AVERAGE(Sales[Amount]), "Max", MAX(Sales[Amount]))',
'EVALUATE ROW("Share North", DIVIDE(CALCULATE([Total Amount], Sales[Region] = "North"), [Total Amount]))',
'EVALUATE ADDCOLUMNS(VALUES(Sales[Region]), "Rank", RANKX(ALL(Sales[Region]), [Total Amount])) ORDER BY Sales[Region]',
'EVALUATE ROW("Blank", CALCULATE([Total Amount], Sales[Region] = "West"), "Div0", DIVIDE(1, 0), "Txt", CONCATENATEX(Sales, Sales[Region], ",", Sales[Amount], ASC))')
$cn=New-Object Microsoft.AnalysisServices.AdomdClient.AdomdConnection("Data Source=$HOSTN;Catalog=$DB")
$cn.Open()
$res=@(); $i=0
foreach($q in $qs){ $i++
 $o=[ordered]@{id="Q$i";query=$q;columns=@();rows=@();error=$null}
 try{ $cmd=$cn.CreateCommand(); $cmd.CommandText=$q; $r=$cmd.ExecuteReader()
  $cols=@(0..($r.FieldCount-1)|%{$r.GetName($_)}); $o.columns=$cols
  $rows=@(); while($r.Read()){ $row=@(); for($c=0;$c -lt $r.FieldCount;$c++){ $v=$r.GetValue($c); if($v -is [DBNull]){$v=$null}; $row+=,$v }; $rows+=,$row }
  $o.rows=$rows; $r.Close() }catch{ $o.error=$_.Exception.Message }
 $res+=[pscustomobject]$o }
$res | ConvertTo-Json -Depth 6 | Set-Content "$ev\results-ssas.json"
# engine info
$eng=[ordered]@{instance='default (MSSQLServerOLAPService, localhost)'}
$tsrv=New-Object "$T.Server"; $tsrv.Connect("Data Source=$HOSTN")
$eng.serverVersion=$tsrv.Version; $eng.productName=$tsrv.ProductName; $eng.edition=$tsrv.Edition; $eng.serverMode=[string]$tsrv.ServerMode; $eng.productLevel=$tsrv.ProductLevel
$eng.databaseCompatibilityLevel=$tsrv.Databases.GetByName($DB).CompatibilityLevel
$eng.fixtureCompatibilityLevel=$m.CompatibilityLevel
try{ $cmd=$cn.CreateCommand(); $cmd.CommandText='SELECT * FROM $SYSTEM.DISCOVER_PROPERTIES WHERE [PropertyName]=''DBMSVersion'''; $r=$cmd.ExecuteReader(); $p=@(); while($r.Read()){ $p+=[ordered]@{name=$r['PropertyName'];value=[string]$r['PropertyValue']} }; $r.Close(); $eng.discoverProperties=$p }catch{ $eng.discoverPropertiesError=$_.Exception.Message }
$eng.adomdServerVersion=$cn.ServerVersion
$cn.Close()
$eng | ConvertTo-Json -Depth 5 | Set-Content "$ev\engine-ssas.json"
$tsrv.Disconnect()
'done'






