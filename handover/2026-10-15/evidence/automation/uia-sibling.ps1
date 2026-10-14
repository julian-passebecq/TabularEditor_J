param([int]$ProcessId, [string]$ProcessName, [string]$AnchorText, [string]$ButtonName, [int]$Up = 1)
# Invokes the button named $ButtonName inside the container that holds the text $AnchorText ($Up levels above it).
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [Windows.Automation.AutomationElement]
$p = Get-Process -Id $ProcessId | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
$w = $A::FromHandle($p.MainWindowHandle)
$anchor = $w.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition($A::NameProperty, $AnchorText)))
if (-not $anchor) { "ANCHOR_NOT_FOUND"; return }
$c = $anchor
for ($i = 0; $i -lt $Up; $i++) { $c = [Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($c) }
$b = $c.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition($A::NameProperty, $ButtonName)))
if (-not $b) { "BUTTON_NOT_FOUND in " + $c.Current.Name + " / " + $c.Current.ClassName; return }
$ip = $null
if ($b.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern, [ref]$ip)) { $ip.Invoke(); "INVOKED $ButtonName near $AnchorText" } else { "NO_INVOKE" }

