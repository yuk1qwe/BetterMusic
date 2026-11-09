param(
    [string]$Version,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$rootDir = Resolve-Path "$PSScriptRoot\.."
$projectDir = Join-Path $rootDir "src\YMusic.WinUI"
$csprojPath = Join-Path $projectDir "YMusic.WinUI.csproj"
$distDir = Join-Path $rootDir "dist"
$publishDir = Join-Path $projectDir "bin\Release\net8.0-windows10.0.26100.0\win-x64\publish"

if (-not $Version) {
    [xml]$xml = Get-Content $csprojPath
    $Version = $xml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $Version) { $Version = "1.0.0" }
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  WinYandex Release Packager v$Version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

if (-not (Test-Path $distDir)) {
    New-Item -ItemType Directory -Path $distDir | Out-Null
}

if (-not $SkipBuild) {
    Write-Host "[1/3] Building and publishing Release (win-x64)..." -ForegroundColor Yellow
    dotnet publish (Join-Path $projectDir "YMusic.WinUI.csproj") -c Release /p:PublishProfile=win-x64
    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet publish failed!"
        exit 1
    }
} else {
    Write-Host "[1/3] Skipping build as requested..." -ForegroundColor DarkGray
}

Write-Host "[2/3] Creating Portable ZIP archive..." -ForegroundColor Yellow
$zipOutput = Join-Path $distDir "WinYandex-v$Version-win-x64-portable.zip"
if (Test-Path $zipOutput) {
    Remove-Item $zipOutput -Force
}
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipOutput -CompressionLevel Optimal
Write-Host "  -> Portable ZIP created: $zipOutput" -ForegroundColor Green

Write-Host "[3/3] Checking for Inno Setup compiler (ISCC.exe)..." -ForegroundColor Yellow
$isccPaths = @(
    "ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:LocalAppData}\Programs\Inno Setup 6\ISCC.exe"
)

$iscc = $null
foreach ($path in $isccPaths) {
    if (Get-Command $path -ErrorAction SilentlyContinue) {
        $iscc = (Get-Command $path).Source
        break
    }
    if (Test-Path $path) {
        $iscc = $path
        break
    }
}

if ($iscc) {
    Write-Host "  Found Inno Setup at: $iscc" -ForegroundColor Green
    $issFile = Join-Path $rootDir "installer\installer.iss"
    Write-Host "  Compiling installer..." -ForegroundColor Yellow
    & $iscc "/DMyAppVersion=$Version" $issFile
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  -> Setup EXE created in: $distDir" -ForegroundColor Green
    } else {
        Write-Warning "Inno Setup compilation returned exit code $LASTEXITCODE"
    }
} else {
    Write-Host "  Inno Setup 6 not found on system." -ForegroundColor DarkYellow
    Write-Host "  To build a single-file 'WinYandex-Setup.exe':" -ForegroundColor Cyan
    Write-Host "  1. Download Inno Setup (free, ~4 MB) from: https://jrsoftware.org/isdl.php" -ForegroundColor Cyan
    Write-Host "  2. Run: ISCC.exe installer\installer.iss" -ForegroundColor Cyan
}

$changelogFile = Join-Path $rootDir "CHANGELOG.md"
if (Test-Path $changelogFile) {
    $changelog = Get-Content -Raw -Encoding UTF8 $changelogFile
    $escaped = [regex]::Escape($Version)
    $pattern = "(?ms)^##\s*\[?" + $escaped + "\]?[^\r\n]*\r?\n(.*?)(?=\r?\n---\s*(\r?\n|\Z)|\r?\n##\s*\[|\Z)"
    if ($changelog -match $pattern) {
        $notes = $matches[1].Trim()
        $notesFile = Join-Path $distDir "release_notes.md"
        [System.IO.File]::WriteAllText($notesFile, $notes, [System.Text.Encoding]::UTF8)
        Write-Host "  -> Release notes extracted to: $notesFile" -ForegroundColor Green
    }
}

Write-Host "`nAll release artifacts available in: $distDir" -ForegroundColor Cyan
Get-ChildItem $distDir | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
