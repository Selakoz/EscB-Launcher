# Pack test build into ZIP
$buildFolder = "builds/season-test"
$outputZip = "builds/season-test.zip"

if (-not (Test-Path $buildFolder)) {
    Write-Host "❌ Папка $buildFolder не найдена!" -ForegroundColor Red
    exit 1
}

# Remove old ZIP if exists
if (Test-Path $outputZip) {
    Remove-Item $outputZip -Force
    Write-Host "🗑️ Удалён старый архив $outputZip"
}

# Create ZIP
Compress-Archive -Path "$buildFolder/*" -DestinationPath $outputZip -Force
Write-Host "✅ Архив создан: $outputZip" -ForegroundColor Green
Write-Host "📦 Размер: $([math]::Round((Get-Item $outputZip).Length / 1MB, 2)) МБ"
