# Настройка GitHub для лаунчера EscB Client

Пошаговая шпаргалка по публикации конфига, архива сборки и баннера, а также по вставке
ссылок в код перед финальной сборкой.

## 0. Переименование и путь к данным

- Заголовок окна и отображаемое имя приложения: **EscB Client**.
- Сборка производится как `EscBClient.exe` (`AssemblyName` в `EscapeBook.csproj`).
- Папка данных игрока осталась прежней: `%APPDATA%\.escapebook` (не изменилась, ничего
  дополнительно настраивать не нужно).

## 1. Создайте репозиторий

Создайте публичный репозиторий на GitHub, например:

```
https://github.com/ВАШ_АККАУНТ/escapebook-launcher
```

## 2. Загрузите config.json

Положите файл `config.json` в корне репозитория (ветка `main`).

Пример структуры:

```json
{
  "jvm_preset_version": 1,
  "default_jvm_args": "-XX:+UnlockExperimentalVMOptions ...",
  "discord_webhook_url": "",
  "banner_url": "https://raw.githubusercontent.com/ВАШ_АККАУНТ/escapebook-launcher/main/assets/banner.png",
  "banner_hash": "SHA-256 баннера",
  "builds": [
    {
      "id": "season2",
      "name": "Сезон 2 — Актуальный",
      "mc_version": "1.21.1",
      "version": "1.0.0",
      "client_zip_url": "https://github.com/ВАШ_АККАУНТ/escapebook-launcher/releases/download/v1.0.0/season2.zip"
    }
  ],
  "optional_mods": [
    {
      "name": "Шейдеры (Iris + Sodium)",
      "description": "Улучшают графику и повышают FPS.",
      "file_prefixes": ["iris-", "oculus-", "sodium-"],
      "enabled_by_default": true
    },
    {
      "name": "Voxy + аддоны",
      "description": "Пакет оптимизации и визуальных улучшений.",
      "file_prefixes": ["voxy-", "voxyaddons-"],
      "enabled_by_default": true
    },
    {
      "name": "Дополнительные визуальные аддоны",
      "description": "Дополнительная графика, могут снижать FPS.",
      "file_prefixes": ["extra-visual-", "fancy-"],
      "enabled_by_default": false
    }
  ]
}
```

## 3. Поле `enabled_by_default` (новое)

Каждый элемент `optional_mods` теперь содержит булево поле `enabled_by_default`:

- `true` — мод включается по умолчанию (файлы **без** `.disabled`).
- `false` — мод выключен по умолчанию (файлы получают суффикс `.disabled`).

Лаウンчер применяет эти значения:
- при первом создании инстанса сборки (автоматически, по скачивании архива);
- при нажатии кнопки **«Сбросить по умолчанию»** во вкладке «Оптимизация и визуал» настроек.

Игрок всё ещё может вручную включить/выключить любой мод чекбоксом; состояние сохраняется
между обновлениями сборки.

## 4. Получите прямую ссылку на config.json (Raw)

1. Откройте загруженный `config.json` на GitHub.
2. Нажмите кнопку **Raw**.
3. Скопируйте адрес из строки браузера. Он будет выглядеть так:

```
https://raw.githubusercontent.com/ВАШ_АККАУНТ/escapebook-launcher/main/config.json
```

4. В коде лаунчера (`Services/ConfigService.cs`) замените заглушку:

```csharp
public const string CONFIG_URL =
    "https://raw.githubusercontent.com/ВАШ_АККАУНТ/escapebook-launcher/main/config.json";
```

## 5. Подготовьте архив сборки (client.zip)

Архив должен содержать файлы клиента (mods, config, resourcepacks и т.д.). Можно Pack'овать
с одной общей папкой в корне архива — лаунчер автоматически срежет её при распаковке.

Загрузите архив в **GitHub Releases**:

1. Перейдите в репозиторий → **Releases** → **Create a new release**.
2. Тег: `v1.0.0`, название: `Сезон 2 v1.0.0`.
3. Прикрепите файл `season2.zip`.
4. Опубликуйте релиз.

## 6. Получите прямую ссылку на архив

После публикации релиза скопируйте ссылку на загруженный `.zip`. Она выглядит так:

```
https://github.com/ВАШ_АККАУНТ/escapebook-launcher/releases/download/v1.0.0/season2.zip
```

Вставьте её в поле `client_zip_url` нужной сборки в `config.json`.

## 7. Подготовьте и загрузите баннер

Баннер — карта `1920 × 480` (соотношение 4:1), формат PNG с прозрачностью.

1. Положите `banner.png` в папку `assets/` репозитория:

```
https://github.com/ВАШ_АККАУНТ/escapebook-launcher/main/assets/banner.png
```

2. Вычислите SHA-256 хэш файла. Пример (PowerShell):

```powershell
Get-FileHash -Algorithm SHA256 .\assets\banner.png | Format-List Hash
```

3. Вставьте полученный хэш (в нижнем регистре) в `banner_hash` в `config.json`.
4. Прямая ссылка на баннер вставьте в `banner_url`:

```
https://raw.githubusercontent.com/ВАШ_АККАУНТ/escapebook-launcher/main/assets/banner.png
```

Лаунчер при запуске сравнит хэш локального файла
`%APPDATA%\.escapebook\assets\banner.png` с `banner_hash` и при различии
тихо скачает новый баннер (без прав админа).

## 8. Версия пресета JVM-аргументов

Поле `jvm_preset_version` (целое число) управляет обновлением локальных
аргументов игрока:

- При первом запуске аргументы берутся из `default_jvm_args` конфига.
- Позже аргументы перезаписываются только если `jvm_preset_version` на
  сервере стал выше сохранённого у игрока (`applied_jvm_preset_version`).

Поднимайте версию, когда хотите обновить аргументы у всех игроков.

## 8.1. Окно настроек и автосохранение

Настройки теперь сохраняются **сразу при изменении** любого контрола —
кнопку «Сохранить» нажимать не обязательно (она тоже сохраняет и закрывает окно).

Окно «Настройки» состоит из двух вкладок:

- **«Запуск и память»** — ползунок ОЗУ с живым индикатором «МБ (ГБ)» и цветными
  подсказками, поле JVM-аргументов и кнопка сброса, путь к `javaw.exe`.
  RAM также мгновенно пишется в `user_settings.json`.
- **«Оптимизация и визуал»** — карточки модов с переключателями. Клик по
  переключателю сразу переименовывает `.jar` ↔ `.jar.disabled` в папке
  `mods` активного инстанса и сохраняет состояние в `user_settings.json`
  (`optionalModStates`). Если сборка ещё не скачана, выбор запомнится и будет
  применён после загрузки.

Если `config.json` недоступен, вкладка модов показывает встроенный набор
(«Iris Shaders», «Voxy (Дальняя прорисовка)», «Sodium»), чтобы интерфейс можно
было проверить без сервера.

## 9. Перед финальной сборкой

Откройте `Services/ConfigService.cs` и убедитесь, что `CONFIG_URL` указывает
на ваш реальный Raw-адрес `config.json`.

Соберите проект:

```powershell
dotnet restore
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false
```

Готовый `EscBClient.exe` будет в `bin\Release\net8.0-windows\` (или в
`publish\` после publish). Рядом с ним уже лежит пример `config.json`
(копируется при сборке).

## 10. Структура данных на ПК игрока

```
%APPDATA%\.escapebook\
├── user_settings.json        ник, ОЗУ, аргументы, путь к Java, выбранная сборка
├── launcher.log              журнал лаунчера
├── assets\
│   └── banner.png            баннер (встроенный или скачанный с GitHub)
└── instances\
    ├── <id сборки>\
    │   ├── .client_version   версия установленного патча
    │   ├── .client_zip_url   URL архива (для обнаружения изменений)
    │   ├── logs\latest.log   лог Minecraft
    │   ├── mods\             моды инстанса (.disabled учитывается)
    │   └── ...               файлы Minecraft (CmlLib)
```

`options.txt` игрока никогда не перезаписывается при обновлении сборки.
