$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$exe = (Resolve-Path "$PWD/dist/Feather/feather.exe").Path
$env:SEVER_LAYOUT_DEBUG='1'
Remove-Item Env:SEVER_START_URL -ErrorAction SilentlyContinue
$server = Start-Process python -ArgumentList '-m','http.server','18770','--bind','127.0.0.1','--directory',"$PWD/tests/fixture" -PassThru -WindowStyle Hidden
$results=@()
try {
  foreach ($mode in @('lazy','eager')) {
    $profile = Join-Path $env:RUNNER_TEMP ("feather-bench-" + $mode + '-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $profile | Out-Null
    $state=@{ tabs=@(0..9 | ForEach-Object { "http://127.0.0.1:18770/second.html?tab=$_" }); active=0; hard=$false; bookmarks=@(); history=@() }
    $state | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $profile 'state.json') -Encoding utf8
    $env:SEVER_PROFILE=$profile
    if ($mode -eq 'eager') { $env:FEATHER_EAGER_RESTORE='1' } else { Remove-Item Env:FEATHER_EAGER_RESTORE -ErrorAction SilentlyContinue }
    $timer=[System.Diagnostics.Stopwatch]::StartNew()
    $app=Start-Process $exe -PassThru
    try {
      $expected=if ($mode -eq 'eager') {10} else {1}
      $life=Join-Path $profile 'lifecycle.txt'
      $count=0
      for ($i=0;$i -lt 60;$i++) {
        Start-Sleep 1; $app.Refresh(); if ($app.HasExited) { throw "Браузер завершился при замере $mode" }
        if (Test-Path $life) { $count=@(Get-Content $life | Where-Object { $_ -eq 'on_after_created' }).Count }
        if ($count -ge $expected) { break }
      }
      if ($count -ne $expected) { throw "Режим $mode: ожидалось $expected браузеров CEF, получено $count" }
      $timer.Stop(); $createdMs=$timer.ElapsedMilliseconds
      Start-Sleep 10
      $samples=@()
      for ($i=0;$i -lt 3;$i++) {
        $processes=@(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq $exe })
        if (!$processes.Count) { throw "Нет процессов Feather для замера $mode" }
        $total=($processes | Measure-Object -Property WorkingSetSize -Sum).Sum
        $samples+= [long]$total
        Start-Sleep 3
      }
      $results+= [pscustomobject]@{mode=$mode; restored_tabs=10; loaded_browsers=$count; time_to_created_ms=$createdMs; samples_bytes=$samples; average_bytes=[long](($samples|Measure-Object -Average).Average); process_count=$processes.Count}
      $root=[System.Windows.Automation.AutomationElement]::RootElement
      $condition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'Feather — браузер')
      $window=$root.FindFirst([System.Windows.Automation.TreeScope]::Children,$condition)
      if (!$window) { throw "Окно замера $mode не найдено" }
      $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
      for ($i=0;$i -lt 30;$i++) { $app.Refresh(); if ($app.HasExited) { break }; Start-Sleep 1 }
      if (!$app.HasExited) { throw "Окно замера $mode не закрылось" }
    } finally {
      $app.Refresh()
      if (!$app.HasExited) { Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue; Start-Sleep 2 }
    }
  }
  $report=[pscustomobject]@{description='Рабочий набор процессов; общие страницы могут учитываться несколько раз. Один прогон на Windows runner, не универсальный бенчмарк.'; scenarios=$results}
  $report | ConvertTo-Json -Depth 6 | Set-Content "$PWD/dist/benchmark.json" -Encoding utf8
  $report | ConvertTo-Json -Depth 6 | Tee-Object -FilePath "$PWD/dist/benchmark.txt"
} finally {
  Remove-Item Env:FEATHER_EAGER_RESTORE -ErrorAction SilentlyContinue
  Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
}
