param([int]$ProcessId, [string]$ProcessName, [string]$Action = 'list', [string]$Name, [int]$Max = 400)
# UI Automation helper: list named elements of a process window, or Invoke the first element with an exact name.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [Windows.Automation.AutomationElement]
$root = $A::RootElement
$procs = Get-Process -Id $ProcessId
foreach ($p in $procs) {
  $cond = New-Object Windows.Automation.PropertyCondition($A::ProcessIdProperty, $p.Id)
  foreach ($w in $root.FindAll([Windows.Automation.TreeScope]::Children, $cond)) {
    if ($Action -eq 'list') {
      $all = $w.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)
      $n = 0
      foreach ($e in $all) { $nm = $e.Current.Name; if ($nm) { $e.Current.ControlType.ProgrammaticName.Replace('ControlType.','') + "`t" + $nm; if (++$n -ge $Max) { break } } }
    } elseif ($Action -eq 'invoke') {
      $nc = New-Object Windows.Automation.PropertyCondition($A::NameProperty, $Name)
      $e = $w.FindFirst([Windows.Automation.TreeScope]::Descendants, $nc)
      if (-not $e) { "NOT_FOUND $Name"; continue }
      $ip = $null
      if ($e.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern, [ref]$ip)) { $ip.Invoke(); "INVOKED $Name" } else { "NO_INVOKE $Name" }
      return
    }
  }
}

