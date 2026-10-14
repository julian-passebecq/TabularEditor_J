param([int]$ProcessId, [string]$CaptureDir)
# Lists visible top-level windows of a process (hwnd, title, rect) and optionally captures each one.
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices; using System.Collections.Generic;
public static class EW { public delegate bool P(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(P p, IntPtr l);
 [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
 public static List<IntPtr> For(int target) { var r = new List<IntPtr>(); EnumWindows((h, l) => { int pid; GetWindowThreadProcessId(h, out pid); if (pid == target && IsWindowVisible(h)) r.Add(h); return true; }, IntPtr.Zero); return r; }
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 public static string Title(IntPtr h) { var s = new StringBuilder(256); GetWindowText(h, s, 256); return s.ToString(); } }
'@
[void][EW]::SetProcessDPIAware()
foreach ($h in [EW]::For($ProcessId)) {
  $r = New-Object EW+RECT; [void][EW]::GetWindowRect($h, [ref]$r)
  "$h`t[$([EW]::Title($h))]`t$($r.R - $r.L)x$($r.B - $r.T)"
  if ($CaptureDir -and ($r.R - $r.L) -gt 50) {
    $bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T); $g = [Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
    [void][EW]::PrintWindow($h, $hdc, 2); $g.ReleaseHdc($hdc); $g.Dispose(); $bmp.Save((Join-Path $CaptureDir "win-$h.png")); $bmp.Dispose()
  }
}

