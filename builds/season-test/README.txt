СБОРКА SEASON-TEST (NeoForge 1.21.1)

ЭТА СБОРКА ТРЕБУЕТ РУЧНОЙ ДОЗАГРУЗКИ ФАЙЛОВ:

1. NeoForge 1.21.1:
   - Скачайте installer: https://neoforged.net/
   - Запустите: java -jar NeoForge-1.21.1-installer.jar --installClient <путь к этой папке>
   - ИЛИ: скопируйте содержимое папки .minecraft после установки NeoForge через лаунчер

2. CustomSkinLoader (опционально, для системы скинов):
   - Скачайте с CurseForge: https://www.curseforge.com/minecraft/mc-mods/custom-skin-loader
   - Версия для NeoForge 1.21.1
   - Поместите JAR в папку mods/

3. Упаковка:
   - После добавления всех файлов запустите: powershell -ExecutionPolicy Bypass -File ../../pack-test-build.ps1
   - Архив season-test.zip появится в папке builds/

4. Загрузка на GitHub:
   - Загрузите season-test.zip в новый релиз на GitHub
   - Обновите config.json, добавив сборку с id "season-test"
