param([string]$Out)
# Hash + timestamp snapshot of global-config locations (check 4). Paths are written with placeholders.
$roots = @("$env:APPDATA\DaxStudio", "$env:LOCALAPPDATA\DaxStudio", "$env:LOCALAPPDATA\TabularEditor", "$env:APPDATA\TabularEditor",
  'C:\Program Files (x86)\Common Files\Microsoft Shared\Power BI Desktop\External Tools', "$env:LOCALAPPDATA\TE2Enhanced")
$rows = foreach ($r in $roots) {
  $label = $r.Replace($env:APPDATA, '%APPDATA%').Replace($env:LOCALAPPDATA, '%LOCALAPPDATA%')
  if (-not (Test-Path $r)) { [pscustomobject]@{root=$label; path='<absent>'; length=$null; lastWrite=$null; sha256=$null}; continue }
  foreach ($f in Get-ChildItem $r -Recurse -File -Force -ErrorAction SilentlyContinue) {
    $h = try { (Get-FileHash $f.FullName -Algorithm SHA256 -ErrorAction Stop).Hash } catch { 'LOCKED' }
    [pscustomobject]@{root=$label; path=$f.FullName.Substring($r.Length); length=$f.Length; lastWrite=$f.LastWriteTimeUtc.ToString('o'); sha256=$h}
  }
}
$rows | ConvertTo-Json -Depth 3 | Set-Content $Out -Encoding utf8NoBOM
"snapshot rows=$(@($rows).Count) -> $(Split-Path $Out -Leaf)"

