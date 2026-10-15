param([string]$Exe, [string]$Out)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Out | Out-Null
$asm = [Reflection.Assembly]::ReflectionOnlyLoadFrom($Exe)
$names = $asm.GetManifestResourceNames() | ? { $_ -like 'costura.*.dll.compressed' }
"resources: " + ($names -join ', ')
foreach ($r in $names) {
  $n = $r -replace '^costura\.', '' -replace '\.compressed$', ''
  $s = $asm.GetManifestResourceStream($r)
  $d = New-Object IO.Compression.DeflateStream($s, [IO.Compression.CompressionMode]::Decompress)
  $o = [IO.File]::Create((Join-Path $Out $n)); $d.CopyTo($o); $o.Close(); $d.Close()
  "$n " + (Get-FileHash (Join-Path $Out $n)).Hash.ToLower()
}
