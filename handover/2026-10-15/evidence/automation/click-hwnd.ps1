param([long]$Hwnd, [int]$X, [int]$Y)
# Clicks at window-relative physical pixel (X, Y) of one specific window, only if it is the foreground window.
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class C { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; } }
'@
[void][C]::SetProcessDPIAware()
$h = [IntPtr]$Hwnd
[void][C]::SetForegroundWindow($h); Start-Sleep -Milliseconds 600
if ([C]::GetForegroundWindow() -ne $h) { "NOT_FOREGROUND"; return }
$r = New-Object C+RECT; [void][C]::GetWindowRect($h, [ref]$r)
[void][C]::SetCursorPos($r.L + $X, $r.T + $Y); Start-Sleep -Milliseconds 150
[C]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero); [C]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
"CLICKED $($r.L + $X),$($r.T + $Y) in $Hwnd"
