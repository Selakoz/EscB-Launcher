<#
.SYNOPSIS
    Полная публикация EscB Client: self-contained лаунчер + крошечный bootstrapper.

.DESCRIPTION
    1) dotnet build EscapeBook.sln -c Release
    2) dotnet publish EscapeBook.csproj -c Release -r win-x64 --self-contained true
       (в архив EscBClient.zip попадает ПОЛНЫЙ .NET 8 Desktop Runtime)
    3) Compress-Archive publish/client -> publish/EscBClient.zip
    4) dotnet publish EscapeBookBootstrapper (net48, standalone, ~30 КБ)

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File publish.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$root = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
Set-Location $root

Write-Host "=== 1/4 Сборка решения ==="
dotnet build EscapeBook.sln -c Release
if ($LASTEXITCODE -ne 0) { throw "dotnet build завершился с ошибкой" }

Write-Host ""
Write-Host "=== 2/4 Публикация лаунчера (self-contained, win-x64) ==="
if (Test-Path publish\client) { Remove-Item publish\client -Recurse -Force }
dotnet publish EscapeBook.csproj -c Release -r win-x64 --self-contained true -o publish/client
if ($LASTEXITCODE -ne 0) { throw "publish лаунчера завершился с ошибкой" }

Write-Host ""
Write-Host "=== 3/4 Архив EscBClient.zip ==="
if (Test-Path publish\EscBClient.zip) { Remove-Item publish\EscBClient.zip -Force }
Compress-Archive -Path publish\client\* -DestinationPath publish\EscBClient.zip -CompressionLevel Optimal -Force

Write-Host ""
Write-Host "=== 4/4 Публикация bootstrapper (.NET Framework 4.8, standalone) ==="
if (Test-Path publish\bootstrapper) { Remove-Item publish\bootstrapper -Recurse -Force }
dotnet publish EscapeBookBootstrapper\EscapeBookBootstrapper.csproj -c Release -o publish\bootstrapper
if ($LASTEXITCODE -ne 0) { throw "publish bootstrapper завершился с ошибкой" }

$clientDirMb = (Get-ChildItem publish\client -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB
$zipMb = (Get-Item publish\EscBClient.zip).Length / 1MB
$bootKb = (Get-Item publish\bootstrapper\EscapeBookBootstrapper.exe).Length / 1KB

Write-Host ""
Write-Host ("publish/client (с runtime): {0:N1} MB" -f $clientDirMb)
Write-Host ("publish/EscBClient.zip:     {0:N1} MB" -f $zipMb)
Write-Host ("EscapeBookBootstrapper.exe: {0:N1} KB (standalone, без .NET 8)" -f $bootKb)
Write-Host ""
Write-Host "В GitHub Release загрузите: publish\EscBClient.zip и publish\bootstrapper\EscapeBookBootstrapper.exe"
Write-Host "=== Готово ==="
