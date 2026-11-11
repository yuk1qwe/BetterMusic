$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
$env:SEVER_PROFILE = Join-Path $env:RUNNER_TEMP 'sever-smoke-profile'
$env:SEVER_START_URL = 'http://127.0.0.1:18769/'
$server = Start-Process python -ArgumentList '-m','http.server','18769','--bind','127.0.0.1','--directory',"$PWD/tests/fixture" -PassThru -WindowStyle Hidden
$app = $null
try {
  $app = Start-Process "$PWD/dist/Север/sever.exe" -PassThru
  $root = [System.Windows.Automation.AutomationElement]::RootElement
  $title = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Север — браузер')
  $window = $null
  for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 1
    $app.Refresh()
    if ($app.HasExited) { throw "Процесс браузера завершился: $($app.ExitCode)" }
    $window = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $title)
    if ($window) { break }
  }
  if (!$window) { throw 'Окно браузера не появилось' }
  $scope = [System.Windows.Automation.TreeScope]::Descendants
  $tabTitle = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Проверка Севера')
  $tab = $null
  for ($i=0; $i -lt 30; $i++) { $tab=$window.FindFirst($scope,$tabTitle); if ($tab) { break }; Start-Sleep 1 }
  if (!$tab) { throw 'Локальная страница не загрузилась или заголовок вкладки не обновился' }
  $newCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '+')
  $newButton = $window.FindFirst($scope, $newCondition)
  if (!$newButton) { throw 'Кнопка новой вкладки отсутствует' }
  $newButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep 2
  $edits = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
  $address=$window.FindFirst($scope,$edits)
  if (!$address) { throw 'Адресная строка отсутствует' }
  $address.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('http://127.0.0.1:18769/second.html')
  $goCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Перейти')
  $window.FindFirst($scope,$goCondition).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  $secondCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Вторая страница')
  $second=$null
  for ($i=0; $i -lt 30; $i++) { $second=$window.FindFirst($scope,$secondCondition); if ($second) { break }; Start-Sleep 1 }
  if (!$second) { throw 'Переход во второй вкладке не выполнен' }
  $renderers = Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*--type=renderer*' -and $_.CommandLine -like '*sever*' }
  if (!$renderers) { throw 'Не обнаружен процесс renderer CEF' }
  if ($renderers | Where-Object { $_.CommandLine -match '(?:^|\s)--no-sandbox(?:\s|$)' }) { throw 'Процесс renderer запущен с --no-sandbox' }
  Write-Host 'Окно, две вкладки, навигация и отсутствие флага --no-sandbox проверены.'
  try {
    $bounds=[System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap=New-Object System.Drawing.Bitmap($bounds.Width,$bounds.Height)
    $graphics=[System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($bounds.Location,[System.Drawing.Point]::Empty,$bounds.Size)
    $bitmap.Save("$PWD/dist/smoke.png",[System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose();$bitmap.Dispose()
  } catch { Write-Warning "Снимок недоступен: $_" }
} finally {
  if ($app -and !$app.HasExited) { $app.CloseMainWindow() | Out-Null; Start-Sleep 3; if (!$app.HasExited) { Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue } }
  Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
}
