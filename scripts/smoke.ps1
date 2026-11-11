$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
$env:SEVER_PROFILE = Join-Path $env:RUNNER_TEMP 'sever-smoke-profile'
$env:SEVER_START_URL = 'http://127.0.0.1:18769/'
$env:SEVER_LAYOUT_DEBUG = '1'
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
  # Проверяем не только заголовок, но и фактическую отрисовку второй вкладки.
  $rect=$window.Current.BoundingRectangle
  $x=[int]($rect.Left + $rect.Width / 2)
  $y=[int]($rect.Top + $rect.Height * 0.6)
  $drawn=$false
  for ($i=0; $i -lt 15; $i++) {
    $pixel=New-Object System.Drawing.Bitmap(1,1)
    $g=[System.Drawing.Graphics]::FromImage($pixel)
    $g.CopyFromScreen($x,$y,0,0,(New-Object System.Drawing.Size(1,1)))
    $color=$pixel.GetPixel(0,0)
    $g.Dispose(); $pixel.Dispose()
    if ([math]::Abs($color.R-177) -lt 5 -and [math]::Abs($color.G-220) -lt 5 -and [math]::Abs($color.B-186) -lt 5) { $drawn=$true; break }
    Start-Sleep 1
  }
  if (!$drawn) { Get-Content (Join-Path $env:SEVER_PROFILE 'layout.txt') -ErrorAction SilentlyContinue | Write-Host; throw 'Заголовок появился, но содержимое второй вкладки не отрисовано' }
  $renderers = Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*--type=renderer*' -and $_.CommandLine -like '*sever*' }
  if (!$renderers) { throw 'Не обнаружен процесс renderer CEF' }
  if ($renderers | Where-Object { $_.CommandLine -match '(?:^|\s)--no-sandbox(?:\s|$)' }) { throw 'Процесс renderer запущен с --no-sandbox' }
  # Измерение рабочего набора — только срез в условиях CI, не сравнительный бенчмарк.
  $memoryLite = (Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq "$PWD\dist\Север\sever.exe" } | Measure-Object -Property WorkingSetSize -Sum).Sum
  $modeCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Lite / Hard')
  $window.FindFirst($scope,$modeCondition).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep 3
  $memoryHard = (Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq "$PWD\dist\Север\sever.exe" } | Measure-Object -Property WorkingSetSize -Sum).Sum
  "Рабочий набор процессов, байт: Lite=$memoryLite; Hard=$memoryHard. Один замер в CI, не бенчмарк." | Tee-Object -FilePath "$PWD/dist/measurement.txt"
  $statePath = Join-Path $env:SEVER_PROFILE 'state.json'
  if (!(Test-Path $statePath)) { throw 'Состояние не сохранено' }
  $bookmarkCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '★ Сохранить / убрать')
  $bookmark = $window.FindFirst($scope,$bookmarkCondition)
  if (!$bookmark) { throw 'Панель Hard не открылась' }
  $bookmark.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  $state = Get-Content $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
  if ($state.tabs.Count -lt 2 -or $state.tabs -notcontains 'http://127.0.0.1:18769/second.html') { throw 'Адреса вкладок не сохранены' }
  if ($state.bookmarks.url -notcontains 'http://127.0.0.1:18769/second.html') { throw 'Закладка не сохранена' }
  # Завершение и повторный запуск проверяют не только запись, но и восстановление сессии.
  $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
  for ($i=0; $i -lt 20; $i++) { $app.Refresh(); if ($app.HasExited) { break }; Start-Sleep 1 }
  if (!$app.HasExited) { Get-Content (Join-Path $env:SEVER_PROFILE 'lifecycle.txt') -ErrorAction SilentlyContinue | Write-Host; throw 'Окно с двумя вкладками не закрылось штатно' }
  $env:SEVER_START_URL = 'http://127.0.0.1:18769/no-session.html'
  $app = Start-Process "$PWD/dist/Север/sever.exe" -PassThru
  $window=$null
  for ($i=0; $i -lt 60; $i++) {
    $app.Refresh(); if ($app.HasExited) { throw 'Браузер завершился при восстановлении сессии' }
    $window=$root.FindFirst([System.Windows.Automation.TreeScope]::Children,$title)
    if ($window -and $window.FindFirst($scope,$tabTitle) -and $window.FindFirst($scope,$secondCondition)) { break }
    Start-Sleep 1
  }
  if (!$window -or !$window.FindFirst($scope,$tabTitle) -or !$window.FindFirst($scope,$secondCondition)) { throw 'Две вкладки не восстановились после перезапуска' }
  $closeCondition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '×')
  $window.FindFirst($scope,$closeCondition).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  for ($i=0; $i -lt 20; $i++) { if (!$window.FindFirst($scope,$secondCondition)) { break }; Start-Sleep 1 }
  $app.Refresh()
  if ($app.HasExited -or $window.FindFirst($scope,$secondCondition) -or !$window.FindFirst($scope,$tabTitle)) {
    Get-Content (Join-Path $env:SEVER_PROFILE 'lifecycle.txt') -ErrorAction SilentlyContinue | Write-Host
    throw 'Закрытие отдельной вкладки не сохранило окно и соседнюю вкладку'
  }
  Write-Host 'Проверены отрисовка, две вкладки, навигация, закладка, штатное закрытие, восстановление сессии и отсутствие флага --no-sandbox.'
  try {
    $bounds=[System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap=New-Object System.Drawing.Bitmap($bounds.Width,$bounds.Height)
    $graphics=[System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($bounds.Location,[System.Drawing.Point]::Empty,$bounds.Size)
    $bitmap.Save("$PWD/dist/smoke.png",[System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose();$bitmap.Dispose()
  } catch { Write-Warning "Снимок недоступен: $_" }
  # Проверяем, что закрытая ранее вкладка не оставила процесс CEF незавершённым.
  $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
  for ($i=0; $i -lt 20; $i++) { $app.Refresh(); if ($app.HasExited) { break }; Start-Sleep 1 }
  if (!$app.HasExited) {
    Get-Content (Join-Path $env:SEVER_PROFILE 'lifecycle.txt') -ErrorAction SilentlyContinue | Write-Host
    throw 'Браузер не завершился после закрытия отдельной вкладки и окна'
  }
} finally {
  if (!(Test-Path "$PWD/dist/smoke.png")) {
    try {
      $bounds=[System.Windows.Forms.Screen]::PrimaryScreen.Bounds
      $bitmap=New-Object System.Drawing.Bitmap($bounds.Width,$bounds.Height)
      $graphics=[System.Drawing.Graphics]::FromImage($bitmap)
      $graphics.CopyFromScreen($bounds.Location,[System.Drawing.Point]::Empty,$bounds.Size)
      $bitmap.Save("$PWD/dist/smoke.png",[System.Drawing.Imaging.ImageFormat]::Png)
      $graphics.Dispose(); $bitmap.Dispose()
    } catch { Write-Warning "Снимок недоступен: $_" }
  }
  if ($app -and !$app.HasExited) { $app.CloseMainWindow() | Out-Null; Start-Sleep 3; if (!$app.HasExited) { Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue } }
  Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
}
