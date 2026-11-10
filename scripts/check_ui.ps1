$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$exe = (Resolve-Path dist/portable/BetterMusic.exe).Path
$app = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
try {
  for ($i=0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 1
    $app.Refresh()
    if ($app.HasExited) { throw "Application exited with code $($app.ExitCode)" }
    if ($app.MainWindowHandle -ne 0) { break }
  }
  if ($app.MainWindowHandle -eq 0) { throw 'No application window appeared' }
  Start-Sleep -Seconds 5
  $root = [System.Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)
  if (!$root) { throw 'Windows UI Automation could not inspect the window' }
  function Capture([string]$name) {
    $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = [System.Drawing.Bitmap]::new($b.Width,$b.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($b.Location,[System.Drawing.Point]::Empty,$b.Size)
    $path = Join-Path $env:RUNNER_TEMP "$name.png"
    $bmp.Save($path,[System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "SCREENSHOT_${name}_BEGIN"
    Write-Host ([Convert]::ToBase64String([IO.File]::ReadAllBytes($path)))
    Write-Host "SCREENSHOT_${name}_END"
  }
  function EnumerateControls {
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
    Write-Host "ELEMENT_COUNT $($all.Count)"
    foreach ($x in $all) {
      $c = $x.Current
      if ($c.Name -and ($c.ControlType.ProgrammaticName -match 'Button|Hyperlink|Edit|Pane|TabItem')) {
        Write-Host "CONTROL type=$($c.ControlType.ProgrammaticName) enabled=$($c.IsEnabled) name=$($c.Name) id=$($c.AutomationId)"
      }
    }
  }
  Write-Host "WINDOW_TITLE $($app.MainWindowTitle)"
  EnumerateControls
  Capture 'initial'
  $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
  $target = $null
  foreach ($x in $all) { if ($x.Current.AutomationId -eq 'SignInWithBrowserButton') { $target = $x; break } }
  if (!$target) { throw 'Browser sign-in button not found in accessibility tree' }
  $pattern = $target.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
  $pattern.Invoke()
  Start-Sleep -Seconds 3
  Write-Host 'CLICKED Browser sign-in'
  EnumerateControls
  Capture 'browser_wait'
  $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
  $cancel = $null
  foreach ($x in $all) { if ($x.Current.AutomationId -eq 'CancelBrowserAuthButton') { $cancel = $x; break } }
  if (!$cancel) { throw 'Cancel button not found after starting browser sign-in' }
  $cancel.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Seconds 2
  Write-Host 'CLICKED Cancel browser sign-in'
  EnumerateControls
  Capture 'after_cancel'
} finally {
  if (!$app.HasExited) { Stop-Process -Id $app.Id -Force }
}
