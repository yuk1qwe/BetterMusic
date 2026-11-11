$ErrorActionPreference = 'Stop'
$release = Invoke-RestMethod -Uri 'https://api.github.com/repos/tauri-apps/cef-rs/releases/latest' -Headers @{ 'User-Agent' = 'Sever-Browser' }
$index = Invoke-RestMethod -Uri 'https://cef-builds.spotifycdn.com/index.json'
$stable = $index.windows64.versions | Where-Object { $_.channel -eq 'stable' } | Select-Object -First 1
Write-Host "Закреплённая версия Rust-привязок: 156.4.0+156.0.26"
Write-Host "Последний выпуск Rust-привязок: $($release.tag_name) $($release.html_url)"
Write-Host "Последний стабильный Windows x64 CEF: $($stable.cef_version)"
Write-Host 'Обновляйте привязки и бинарный CEF вместе; инструкция и проверки — в README.md.'
