# OBS-001 probe: File.Replace on freshly written files vs Delete+Move. Writes only inside -Dir (default a new %TEMP% folder) and removes it.
param([string]$Dir = (Join-Path $env:TEMP ('obs-' + [guid]::NewGuid().ToString('N'))), [int]$N = 300)
New-Item -ItemType Directory -Force $Dir | Out-Null
$results = @{}
foreach ($mode in 'replace-immediate','replace-ext-json','move-delete') {
  $fail = 0; $hr = @{}
  for ($i = 0; $i -lt $N; $i++) {
    $ext = if ($mode -eq 'replace-ext-json') { '.json' } else { '.dat' }
    $dest = Join-Path $Dir ("d$i$ext"); $tmp = $dest + '.tmp'
    [IO.File]::WriteAllText($dest, "first $i")
    [IO.File]::WriteAllText($tmp, "second $i")
    try {
      if ($mode -eq 'move-delete') { [IO.File]::Delete($dest); [IO.File]::Move($tmp, $dest) }
      else { [IO.File]::Replace($tmp, $dest, [NullString]::Value) }
    } catch { $fail++; $k = '0x{0:X8}' -f $_.Exception.InnerException.HResult; if (!$k) { $k = $_.Exception.Message }; $hr[$_.Exception.InnerException.Message] = 1 + [int]$hr[$_.Exception.InnerException.Message] }
  }
  "$mode : $fail / $N failures  $(($hr.Keys | % { $_ + ' x' + $hr[$_] }) -join '; ')"
}
Remove-Item -Recurse -Force $Dir
