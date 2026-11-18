<#
.SYNOPSIS
    Создает минимальную тестовую сборку для проверки bootstrapper-а.

.DESCRIPTION
    По умолчанию собирает минимальный ZIP с заглушкой EscBClient.exe (проверка
    скачивания, распаковки и маркера версии). С ключом -RealClient упаковывает
    настоящий опубликованный клиент из publish\client (полный тест запуска).

    Скрипт создает два файла с одинаковым содержимым:
      - test-build.zip  — имя, указанное в задаче;
      - EscBClient.zip  — имя ассета, которое ищет bootstrapper
                          (Bootstrapper.ReleaseAssetName = "EscBClient.zip").
    В релиз GitHub загружается EscBClient.zip.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File create-test-build.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File create-test-build.ps1 -RealClient
#>

[CmdletBinding()]
param(
    [switch]$RealClient,
    [string]$OutputZip = "test-build.zip"
)

$ErrorActionPreference = "Stop"

# Корректный вывод кириллицы в консоли Windows PowerShell.
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$root = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
$buildDir = Join-Path $root "test-build"
$zipPath = Join-Path $root $OutputZip
$assetZipPath = Join-Path $root "EscBClient.zip"

Write-Host "=== EscB Test Build ==="

# 1. Чистая временная папка
if (Test-Path $buildDir) {
    Remove-Item $buildDir -Recurse -Force
}
New-Item -ItemType Directory -Path $buildDir | Out-Null

if ($RealClient) {
    # ------------------------------------------------------------- реальный клиент
    $publishDir = Join-Path $root "publish\client"
    $realExe = Join-Path $publishDir "EscBClient.exe"

    if (-not (Test-Path $realExe)) {
        throw "Не найден $realExe. Сначала выполните:`n" +
              "  dotnet publish EscapeBook.csproj -c Release -r win-x64 --self-contained false -o publish/client"
    }

    Write-Host "Источник: опубликованный клиент publish\client (-RealClient)"
    Copy-Item -Path (Join-Path $publishDir "*") -Destination $buildDir -Recurse -Force
}
else {
    # ------------------------------------------------------------ заглушка (по умолчанию)
    Write-Host "Источник: заглушка EscBClient.exe (проверка скачивания/распаковки)"

    # Заглушка ~10 КБ: bootstrapper проверяет только наличие файла в архиве.
    $placeholder = Join-Path $buildDir "EscBClient.exe"
    $header = "MZ" # условная сигнатура, файл не является настоящим исполняемым
    $padding = ("0" * 10240)
    Set-Content -Path $placeholder -Value ($header + $padding) -Encoding ASCII -NoNewline
}

# config.json обязателен: лаунчер использует его как fallback
$configSource = Join-Path $root "config.json"
if (Test-Path $configSource) {
    if (-not (Test-Path (Join-Path $buildDir "config.json"))) {
        Copy-Item $configSource (Join-Path $buildDir "config.json") -Force
    }
}
else {
    Write-Warning "config.json не найден в корне репозитория — архив будет без него."
}

# 2. Упаковка
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}
Compress-Archive -Path (Join-Path $buildDir "*") -DestinationPath $zipPath -CompressionLevel Optimal -Force

if (-not (Test-Path $zipPath)) {
    throw "Не удалось создать $zipPath"
}

# 3. Копия под именем, которое ищет bootstrapper
Copy-Item $zipPath $assetZipPath -Force

# 4. Итоги
$zipInfo = Get-Item $zipPath
$hash = (Get-FileHash -Algorithm SHA256 -Path $zipPath).Hash

Write-Host ""
Write-Host "Архив:     $($zipInfo.FullName)"
Write-Host ("Размер:    {0:N1} КБ" -f ($zipInfo.Length / 1KB))
Write-Host "SHA-256:   $hash"
Write-Host "Копия для релиза (имя ассета!): $assetZipPath"
Write-Host ""
Write-Host "Дальше:"
Write-Host "  1) Загрузите EscBClient.zip в GitHub Release с тегом v0.0.1"
Write-Host "     (bootstrapper скачивает только ассет с именем EscBClient.zip)."
Write-Host "  2) Для полного теста запуска используйте: .\create-test-build.ps1 -RealClient"
Write-Host ""
Write-Host "=== Готово ==="
