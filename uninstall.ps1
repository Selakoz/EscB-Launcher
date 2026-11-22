<#
.SYNOPSIS
    Полное удаление лаунчера EscB Client и всех его данных.

.DESCRIPTION
    Останавливает процессы лаунчера и игры, после чего удаляет:
      * %APPDATA%\.escapebook   — настройки, инстансы и установленный клиент;
      * %LOCALAPPDATA%\EscB     — install_path.txt с выбранной папкой установки;
      * ярлык «EscB Client» с рабочего стола.

    После этого загрузчик при следующем запуске снова покажет окно выбора
    папки установки. Скрипт работает только с путями EscB и не трогает
    системные файлы или данные других программ.

    ВАЖНО: данные удаляются безвозвратно.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File uninstall.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "SilentlyContinue"

# Корректный вывод кириллицы в консоли Windows PowerShell.
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

# ---------------------------------------------------------------------------
# Защитные проверки: работаем строго с папками EscB внутри APPDATA/LOCALAPPDATA.
# ---------------------------------------------------------------------------
function Get-NormalizedPath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }

    try { return [System.IO.Path]::GetFullPath($Path).TrimEnd('\') }
    catch { return $null }
}

$appDataRoot = Get-NormalizedPath $env:APPDATA
$localAppDataRoot = Get-NormalizedPath $env:LOCALAPPDATA

if (-not $appDataRoot -or -not $localAppDataRoot) {
    Write-Host "❌ Не удалось определить системные папки APPDATA/LOCALAPPDATA." -ForegroundColor Red
    exit 1
}

$dataPath = Get-NormalizedPath (Join-Path $appDataRoot ".escapebook")
$configPath = Get-NormalizedPath (Join-Path $localAppDataRoot "EscB")

$expectedDataPath = Get-NormalizedPath (Join-Path $appDataRoot ".escapebook")
$expectedConfigPath = Get-NormalizedPath (Join-Path $localAppDataRoot "EscB")

foreach ($pair in @(
        @{ Actual = $dataPath;   Expected = $expectedDataPath;   Label = "%APPDATA%\.escapebook" },
        @{ Actual = $configPath; Expected = $expectedConfigPath; Label = "%LOCALAPPDATA%\EscB" }
    )) {
    # Путь должен совпадать с ожидаемым и не быть корнем диска.
    if (-not $pair.Actual -or $pair.Actual -ne $pair.Expected -or $pair.Actual.Length -le 3) {
        Write-Host "❌ Небезопасный путь '$($pair.Actual)' для $($pair.Label). Удаление отменено." -ForegroundColor Red
        exit 1
    }
}

Write-Host "=== Удаление лаунчера EscB Client ===" -ForegroundColor Cyan
Write-Host ""

# ---------------------------------------------------------------------------
# 1. Останавливаем лаунчер, загрузчик, обновлятор и игру.
# ---------------------------------------------------------------------------
Stop-Process -Name "EscBClient", "EscapeBookBootstrapper", "EscBUpdater", "javaw" -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

# ---------------------------------------------------------------------------
# 2-4. Удаляем данные, конфигурацию пути установки и ярлык.
# ---------------------------------------------------------------------------
function Remove-EscBItem {
    param(
        [string]$Path,
        [string]$Label,
        [switch]$Recurse
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        Write-Host "• $Label — не найдено, пропуск." -ForegroundColor DarkGray
        return $true
    }

    # Несколько попыток на случай, если файл ещё держит закрывающийся процесс.
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        if ($Recurse) {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue
        }
        else {
            Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
        }

        if (-not (Test-Path -LiteralPath $Path)) {
            Write-Host "• $Label — удалено." -ForegroundColor Green
            return $true
        }

        Start-Sleep -Milliseconds 400
    }

    Write-Host "• $Label — не удалось удалить полностью." -ForegroundColor Yellow
    return $false
}

$dataRemoved = Remove-EscBItem -Path $dataPath -Label "%APPDATA%\.escapebook" -Recurse
# install_path.txt критичен: без него загрузчик снова спросит папку установки.
$configRemoved = Remove-EscBItem -Path $configPath -Label "%LOCALAPPDATA%\EscB (путь установки)" -Recurse

$shortcutPath = Join-Path $env:USERPROFILE "Desktop\EscB Client.lnk"
$shortcutRemoved = Remove-EscBItem -Path $shortcutPath -Label "ярлык «EscB Client» на рабочем столе"

Write-Host ""

if ($dataRemoved -and $configRemoved) {
    Write-Host "✅ Лаунчер EscB Client полностью удалён. При следующем запуске установщика вы сможете выбрать новую папку." -ForegroundColor Green
    exit 0
}

Write-Host "❌ Некоторые данные удалить не удалось. Закройте лаунчер и игру, затем запустите скрипт ещё раз (можно от имени администратора)." -ForegroundColor Red
exit 1
