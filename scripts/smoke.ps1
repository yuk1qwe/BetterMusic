$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
$env:SEVER_PROFILE = Join-Path $env:RUNNER_TEMP 'sever-smoke-profile'
$env:SEVER_START_URL = 'http://127.0.0.1:18769/'
$env:SEVER_LAYOUT_DEBUG = '1'
$server = Start-Process python -ArgumentList '-m','http.server','18769','--bind','127.0.0.1','--directory',"$PWD/tests/fixture" -PassThru -WindowStyle Hidden
$app = $null
$completed = $false
try {
  $app = Start-Process "$PWD/dist/Feather/feather.exe" -PassThru
  $root = [System.Windows.Automation.AutomationElement]::RootElement
  $title = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Feather — браузер')
  $window = $null
  for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 1
    $app.Refresh()
    if ($app.HasExited) {
      foreach ($file in @((Join-Path $env:SEVER_PROFILE 'panic.txt'),(Join-Path $env:SEVER_PROFILE 'lifecycle.txt'),"$PWD/dist/Feather/debug.log")) {
        if (Test-Path $file) { Write-Host "Диагностика: $file"; Get-Content $file -Tail 100 | Write-Host }
      }
      throw "Процесс браузера завершился: $($app.ExitCode)"
    }
    $window = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $title)
    if ($window) { break }
  }
  if (!$window) { throw 'Окно браузера не появилось' }
  $scope = [System.Windows.Automation.TreeScope]::Descendants
  $tabTitle = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Проверка Feather')
  $tab = $null
  for ($i=0; $i -lt 30; $i++) { $tab=$window.FindFirst($scope,$tabTitle); if ($tab) { break }; Start-Sleep 1 }
  if (!$tab) { throw 'Локальная страница не загрузилась или заголовок вкладки не обновился' }
  $newCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '+')
  $newButton = $window.FindFirst($scope, $newCondition)
  if (!$newButton) { throw 'Кнопка новой вкладки отсутствует' }
  $newButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep 3
  # Отдельный снимок новой локальной страницы для визуальной проверки дизайна.
  try {
    $bounds=[System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap=New-Object System.Drawing.Bitmap($bounds.Width,$bounds.Height)
    $graphics=[System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($bounds.Location,[System.Drawing.Point]::Empty,$bounds.Size)
    $bitmap.Save("$PWD/dist/new-tab.png",[System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
  } catch { Write-Warning "Снимок стартовой страницы недоступен: $_" }
  $edits = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
  $address=$window.FindFirst($scope,$edits)
  if (!$address) { throw 'Адресная строка отсутствует' }
  $addressValue=$address.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
  if ($addressValue -ne 'Новая вкладка · страница Feather') { throw "Неверный адрес стартовой страницы: $addressValue" }
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
  # Проверяем адрес в отдельном окне и обновление при переходе на about:blank.
  $popupLinkCondition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Проверить всплывающее окно')
  $popupLink=$null
  for ($i=0; $i -lt 15; $i++) { $popupLink=$window.FindFirst($scope,$popupLinkCondition); if ($popupLink) {break}; Start-Sleep 1 }
  if (!$popupLink) {throw 'Ссылка для проверки всплывающего окна недоступна'}
  $popupLink.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  $popupCondition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Feather — всплывающее окно')
  $popup=$null
  for ($i=0; $i -lt 20; $i++) { $popup=$root.FindFirst([System.Windows.Automation.TreeScope]::Children,$popupCondition); if ($popup) {break}; Start-Sleep 1 }
  if (!$popup) {throw 'Всплывающее окно не открылось'}
  $popupAddressCondition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Адрес всплывающего окна')
  $popupAddress=$popup.FindFirst($scope,$popupAddressCondition)
  if (!$popupAddress) {throw 'В окне нет адресной строки'}
  $popupValue=$popupAddress.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
  if (!$popupValue.Current.IsReadOnly) {throw 'Адрес всплывающего окна должен быть доступен только для чтения'}
  for ($i=0; $i -lt 20; $i++) {if ($popupValue.Current.Value -like 'http://127.0.0.1:18769/popup.html*') {break}; Start-Sleep 1}
  if ($popupValue.Current.Value -notlike 'http://127.0.0.1:18769/popup.html*') {throw "Неверный адрес всплывающего окна: $($popupValue.Current.Value)"}
  $blankCondition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Открыть пустую страницу')
  $blank=$null
  for ($i=0; $i -lt 15; $i++) {$blank=$popup.FindFirst($scope,$blankCondition);if ($blank) {break};Start-Sleep 1}
  if (!$blank) {throw 'Ссылка внутри всплывающего окна недоступна'}
  $blank.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  for ($i=0; $i -lt 15; $i++) {if ($popupValue.Current.Value -eq 'about:blank') {break};Start-Sleep 1}
  if ($popupValue.Current.Value -ne 'about:blank') {throw "Адрес не обновился после перехода: $($popupValue.Current.Value)"}
  $popup.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
  $mainBlankCondition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Проверить адрес страницы')
  $mainBlank=$window.FindFirst($scope,$mainBlankCondition)
  if (!$mainBlank) {throw 'Ссылка проверки адреса недоступна'}
  $mainBlank.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  $mainValue=$address.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
  for ($i=0; $i -lt 15; $i++) {if ($mainValue.Current.Value -eq 'about:blank') {break};Start-Sleep 1}
  if ($mainValue.Current.Value -ne 'about:blank') {throw "Основной адрес не обновился: $($mainValue.Current.Value)"}
  $backCondition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '←')
  $window.FindFirst($scope,$backCondition).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  for ($i=0; $i -lt 15; $i++) {if ($mainValue.Current.Value -like 'http://127.0.0.1:18769/second.html*') {break};Start-Sleep 1}
  if ($mainValue.Current.Value -notlike 'http://127.0.0.1:18769/second.html*') {throw 'Возврат на тестовую страницу не выполнен'}
  $renderers = Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*--type=renderer*' -and $_.CommandLine -like '*feather*' }
  if (!$renderers) { throw 'Не обнаружен процесс renderer CEF' }
  if ($renderers | Where-Object { $_.CommandLine -match '(?:^|\s)--no-sandbox(?:\s|$)' }) { throw 'Процесс renderer запущен с --no-sandbox' }
  # Измерение рабочего набора — только срез в условиях CI, не сравнительный бенчмарк.
  $hardCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Режим: Hard')
  $liteCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Режим: Lite')
  # Режим Hard включён по умолчанию: проверяем выключение и повторное включение.
  $window.FindFirst($scope,$hardCondition).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  $liteButton = $null
  for ($i=0; $i -lt 20; $i++) { $liteButton=$window.FindFirst($scope,$liteCondition); if ($liteButton) { break }; Start-Sleep -Milliseconds 500 }
  if (!$liteButton) { throw 'Режим Lite не включился' }
  Start-Sleep 3
  $memoryLite = (Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq "$PWD\dist\Feather\feather.exe" } | Measure-Object -Property WorkingSetSize -Sum).Sum
  $liteButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  for ($i=0; $i -lt 20; $i++) { if ($window.FindFirst($scope,$hardCondition)) { break }; Start-Sleep -Milliseconds 500 }
  if (!$window.FindFirst($scope,$hardCondition)) { throw 'Режим Hard не включился' }
  Start-Sleep 3
  $memoryHard = (Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq "$PWD\dist\Feather\feather.exe" } | Measure-Object -Property WorkingSetSize -Sum).Sum
  "Рабочий набор процессов, байт: Lite=$memoryLite; Hard=$memoryHard. Один замер в CI, не бенчмарк." | Tee-Object -FilePath "$PWD/dist/measurement.txt"
  $statePath = Join-Path $env:SEVER_PROFILE 'state.json'
  if (!(Test-Path $statePath)) { throw 'Состояние не сохранено' }
  $bookmarksCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Закладки')
  $window.FindFirst($scope,$bookmarksCondition).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  $bookmarkCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '★ Сохранить / убрать')
  $bookmark = $null
  for ($i=0; $i -lt 20; $i++) { $bookmark=$window.FindFirst($scope,$bookmarkCondition); if ($bookmark) { break }; Start-Sleep -Milliseconds 500 }
  if (!$bookmark) { throw 'Панель Hard не открылась' }
  $bookmark.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  $state = $null
  for ($i=0; $i -lt 20; $i++) {
    $state = Get-Content $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($state.bookmarks.url -contains 'http://127.0.0.1:18769/second.html') { break }
    Start-Sleep -Milliseconds 500
  }
  if ($state.tabs.Count -lt 2 -or $state.tabs -notcontains 'http://127.0.0.1:18769/second.html') { throw 'Адреса вкладок не сохранены' }
  if ($state.bookmarks.url -notcontains 'http://127.0.0.1:18769/second.html') { throw 'Закладка не сохранена' }
  # Завершение и повторный запуск проверяют не только запись, но и восстановление сессии.
  $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
  for ($i=0; $i -lt 20; $i++) { $app.Refresh(); if ($app.HasExited) { break }; Start-Sleep 1 }
  if (!$app.HasExited) { Get-Content (Join-Path $env:SEVER_PROFILE 'lifecycle.txt') -ErrorAction SilentlyContinue | Write-Host; throw 'Окно с двумя вкладками не закрылось штатно' }
  $env:SEVER_START_URL = 'http://127.0.0.1:18769/no-session.html'
  $app = Start-Process "$PWD/dist/Feather/feather.exe" -PassThru
  $window=$null
  for ($i=0; $i -lt 60; $i++) {
    $app.Refresh(); if ($app.HasExited) { throw 'Браузер завершился при восстановлении сессии' }
    $window=$root.FindFirst([System.Windows.Automation.TreeScope]::Children,$title)
    $deferred=if ($window) { $window.FindFirst($scope,(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '127.0.0.1'))) } else { $null }
    if ($window -and $deferred -and $window.FindFirst($scope,$secondCondition)) { break }
    Start-Sleep 1
  }
  if (!$window -or !$deferred -or !$window.FindFirst($scope,$secondCondition)) { throw 'Вкладки не восстановились после перезапуска' }
  if ($window.FindFirst($scope,$tabTitle)) { throw 'Фоновая вкладка загрузилась до выбора' }
  $deferred.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  $first=$null
  for ($i=0; $i -lt 30; $i++) { $first=$window.FindFirst($scope,$tabTitle); if ($first) { break }; Start-Sleep 1 }
  if (!$first) { throw 'Отложенная вкладка не загрузилась при выборе' }
  # Настоящие сочетания клавиш переключают обе вкладки в восстановленной сессии.
  $restoredEdit=$window.FindFirst($scope,$edits)
  $restoredAddress=$restoredEdit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
  $restoredEdit.SetFocus()
  [System.Windows.Forms.SendKeys]::SendWait('^{TAB}')
  for ($i=0; $i -lt 20; $i++) { if ($restoredAddress.Current.Value -like 'http://127.0.0.1:18769/second.html*') { break }; Start-Sleep -Milliseconds 500 }
  if ($restoredAddress.Current.Value -notlike 'http://127.0.0.1:18769/second.html*') { throw 'Ctrl+Tab не переключил вкладку' }
  [System.Windows.Forms.SendKeys]::SendWait('^+{TAB}')
  for ($i=0; $i -lt 20; $i++) { if ($restoredAddress.Current.Value -eq 'http://127.0.0.1:18769/') { break }; Start-Sleep -Milliseconds 500 }
  if ($restoredAddress.Current.Value -ne 'http://127.0.0.1:18769/') { throw 'Ctrl+Shift+Tab не переключил вкладку' }
  $window.FindFirst($scope,$secondCondition).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  $closeCondition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '×')
  $window.FindFirst($scope,$closeCondition).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  for ($i=0; $i -lt 20; $i++) { if (!$window.FindFirst($scope,$secondCondition)) { break }; Start-Sleep 1 }
  $app.Refresh()
  if ($app.HasExited -or $window.FindFirst($scope,$secondCondition) -or !$window.FindFirst($scope,$tabTitle)) {
    Get-Content (Join-Path $env:SEVER_PROFILE 'lifecycle.txt') -ErrorAction SilentlyContinue | Write-Host
    throw 'Закрытие отдельной вкладки не сохранило окно и соседнюю вкладку'
  }
  Write-Host 'Проверены отрисовка, две вкладки, навигация, переключение клавишами, закладка, штатное закрытие, восстановление сессии и отсутствие флага --no-sandbox.'
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
  $completed = $true
} finally {
  if (!$completed) {
    foreach ($file in @((Join-Path $env:SEVER_PROFILE 'panic.txt'),(Join-Path $env:SEVER_PROFILE 'lifecycle.txt'),"$PWD/dist/Feather/debug.log")) {
      if (Test-Path $file) { Write-Host "Диагностика: $file"; Get-Content $file -Tail 100 | Write-Host }
    }
  }
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
