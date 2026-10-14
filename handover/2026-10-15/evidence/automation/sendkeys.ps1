param([int]$ProcessId, [string]$ProcessName, [string]$Keys)
# Brings the process main window to the foreground and sends a key chord (pywinauto-level automation).
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class F { [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow(); }
'@
$p = Get-Process -Id $ProcessId | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
[void][F]::SetForegroundWindow($p.MainWindowHandle)
Start-Sleep -Milliseconds 700
if ([F]::GetForegroundWindow() -ne $p.MainWindowHandle) { "NOT_FOREGROUND"; return }
[System.Windows.Forms.SendKeys]::SendWait($Keys)
"SENT $Keys"

