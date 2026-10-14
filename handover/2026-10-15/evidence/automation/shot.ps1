param([int]$ProcessId, [string]$ProcessName = 'PBIDesktop', [string]$Out)
# Captures the main window of a process (own screen capture; no input sent). Also dumps top-level UIA window names.
Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class W { [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; } }
'@
$procs = Get-Process -Id $ProcessId
$root = [Windows.Automation.AutomationElement]::RootElement
foreach ($p in $procs) {
  $cond = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
  $wins = $root.FindAll([Windows.Automation.TreeScope]::Children, $cond)
  foreach ($w in $wins) { "window: [" + $w.Current.Name + "] class=" + $w.Current.ClassName + " hwnd=" + $w.Current.NativeWindowHandle }
}
$h = ($procs | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1).MainWindowHandle
if ($Out -and $h) {
  $r = New-Object W+RECT; [void][W]::GetWindowRect($h, [ref]$r)
  $bmp = New-Object Drawing.Bitmap ([Math]::Max(1, $r.R - $r.L)), ([Math]::Max(1, $r.B - $r.T))
  $g = [Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc(); [void][W]::PrintWindow($h, $hdc, 2); $g.ReleaseHdc($hdc); $g.Dispose()
  $bmp.Save($Out, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose(); "saved $Out"
}

