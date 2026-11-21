<#
.SYNOPSIS
    Безопасная очистка данных лаунчера для тестирования бутстраппера.

.DESCRIPTION
    Перемещает папку %APPDATA%\.escapebook в резервную копию с меткой времени
    C:\EscB_Launcher_Backup_yyyyMMdd_HHmmss, имитируя чистую установку.
    Данные не удаляются безвозвратно: их можно вернуть, переместив папку
    обратно в %APPDATA%. Старые резервные копии не удаляются и сохраняются.

    Скрипт НЕ трогает исходный код и рабочую папку проекта.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File cleanup-for-test.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File cleanup-for-test.ps1 -WhatIf
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param()

$ErrorActionPreference = "Stop"

# Корректный вывод кириллицы в консоли Windows PowerShell.
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$sourcePath = "$env:APPDATA\.escapebook"
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$backupPath = "C:\EscB_Launcher_Backup_$timestamp"

# ---------------------------------------------------------------------------
# Защитные проверки: работаем строго с %APPDATA%\.escapebook.
# ---------------------------------------------------------------------------
try {
    $resolvedSource = [System.IO.Path]::GetFullPath($sourcePath).TrimEnd('\')
    $expectedSource = [System.IO.Path]::GetFullPath((Join-Path $env:APPDATA ".escapebook")).TrimEnd('\')
    $resolvedBackup = [System.IO.Path]::GetFullPath($backupPath).TrimEnd('\')

    if ($resolvedSource -ne $expectedSource) {
        throw "Небезопасный путь источника: '$resolvedSource'."
    }

    if ($resolvedBackup -eq $resolvedSource) {
        throw "Путь резервной копии совпадает с источником. Очистка отменена."
    }

    # Метка времени уникальна, но не перезаписываем существующую копию.
    if (Test-Path -LiteralPath $resolvedBackup) {
        throw "Путь '$resolvedBackup' уже существует. Повторите запуск через секунду."
    }
}
catch {
    Write-Host "❌ Проверка путей не пройдена: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host "=== Очистка данных лаунчера ===" -ForegroundColor Cyan

# 1. Источник существует?
if (-not (Test-Path -LiteralPath $resolvedSource)) {
    Write-Host "Launcher data not found. Nothing to clean." -ForegroundColor Yellow
    exit 0
}

try {
    # 2. Перемещаем данные лаунчера в новую резервную копию (не удаляем!).
    #    Старые бекапы C:\EscB_Launcher_Backup_* остаются нетронутыми.
    if ($PSCmdlet.ShouldProcess($resolvedSource, "Переместить в $resolvedBackup")) {
        Move-Item -LiteralPath $resolvedSource -Destination $resolvedBackup -Force
    }
}
catch [System.UnauthorizedAccessException] {
    Write-Host "❌ Нет прав на доступ к '$resolvedSource' или '$resolvedBackup'." -ForegroundColor Red
    Write-Host "   Закройте лаунчер и запустите скрипт от имени администратора." -ForegroundColor Red
    Write-Host "   Детали: $($_.Exception.Message)" -ForegroundColor DarkGray
    exit 1
}
catch {
    Write-Host "❌ Не удалось выполнить очистку: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "   Проверьте, что лаунчер закрыт и папка '$backupPath' доступна." -ForegroundColor Red
    exit 1
}

if (Test-Path -LiteralPath $resolvedBackup) {
    Write-Host ""
    Write-Host "✅ Данные лаунчера перемещены в $backupPath. Старые бекапы сохранены." -ForegroundColor Green
    Write-Host "   Для восстановления переместите папку обратно в %APPDATA%." -ForegroundColor DarkGray
}
else {
    Write-Host "⚠️ Операция не выполнена (режим -WhatIf). Изменений нет." -ForegroundColor Yellow
}
