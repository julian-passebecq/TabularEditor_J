param([int]$ProcessId, [string]$ProcessName, [string]$Name)
# Expands the first element with an exact name (ExpandCollapse pattern) and lists its named descendants.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [Windows.Automation.AutomationElement]
$p = Get-Process -Id $ProcessId | Select-Object -First 1
$w = $A::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, (New-Object Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)))
$e = $w.FindFirst([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition($A::NameProperty, $Name)))
if (-not $e) { "NOT_FOUND"; return }
$pat = $null
if ($e.TryGetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$pat)) { try { $pat.Expand(); "EXPANDED" } catch { "EXPAND_FAILED $($_.Exception.Message)" } } else { "NO_EXPAND" }
Start-Sleep -Milliseconds 1500
$parent = [Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($e)
foreach ($d in $parent.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)) { if ($d.Current.Name) { $d.Current.ControlType.ProgrammaticName.Replace('ControlType.','') + "`t" + $d.Current.Name } }

