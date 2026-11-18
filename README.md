# EscB Client (EscapeBook Launcher)

Десктопный лаунчер Minecraft для сервера EscapeBook на C# / .NET 8 / Windows Forms:
тёмный интерфейс, оффлайн-запуск, автообновление сборок с GitHub, фикс дискретной
видеокарты через реестр и телеметрия железа в Discord.

Проект состоит из двух приложений:

| Проект | Назначение | Итоговый файл |
| --- | --- | --- |
| `EscapeBookBootstrapper` | лёгкий загрузчик и автообновление | `EscapeBookBootstrapper.exe` |
| `EscapeBook` | основной лаунчер (WinForms) | `EscBClient.exe` (в архиве `EscBClient.zip`) |

## Как это работает (2 шага)

1. Игрок запускает **`EscapeBookBootstrapper.exe`**.
2. Загрузчик запрашивает последний релиз на GitHub и сравнивает его `tag_name`
   с локальным `%APPDATA%\.escapebook\launcher\.bootstrapper_version`:
   - если версии совпадают — сразу запускает установленный `EscBClient.exe`;
   - если нужен апдейт — скачивает `EscBClient.zip`, распаковывает его в
     `%APPDATA%\.escapebook\launcher\` (с перезаписью и обходом блокировки
     запущенного `.exe`), сохраняет новый тег и запускает клиент.
3. Загрузчик завершает работу, дальше играет лаунчер.

Если GitHub недоступен, загрузчик тихо запускает уже установленный
`EscBClient.exe`. Все ошибки пишутся в
`%APPDATA%\.escapebook\launcher\bootstrapper.log`.

## Возможности

- Никнейм, выбранная сборка, ОЗУ, JVM-аргументы и путь к Java сохраняются
  автоматически в `%APPDATA%\.escapebook\user_settings.json`.
- Ползунок ОЗУ: максимум рассчитывается по объёму физической памяти
  (общий объём минус 2 ГБ резерва для системы), выделить больше нельзя.
- Цветные подсказки по памяти: < 6144 МБ — красный, 6144–12288 — зелёный,
  > 12288 — жёлтый.
- Выбор сборки с индикацией состояния: «Актуальная», «Требуется обновление»,
  «Не установлено»; главная кнопка меняется на **ИГРАТЬ / ОБНОВИТЬ / УСТАНОВИТЬ**.
- Отдельные инстансы: `%APPDATA%\.escapebook\instances\<id>\`.
- Обновление сборки ZIP-архивом с прогрессом; `options.txt` не перезаписывается,
  состояние `.disabled`-модов сохраняется.
- Менеджер оптимизации: карточки `optional_mods` с переключателями
  `.jar` ↔ `.jar.disabled`.
- JVM-пресеты: аргументы обновляются с сервера только при росте
  `jvm_preset_version`.
- `javaw.exe`, которым запускается игра, прописывается в
  `HKEY_CURRENT_USER\Software\Microsoft\DirectX\UserGpuPreferences`
  со значением `GpuPreference=2;`.
- Телеметрия: CPU, все видеокарты, общий/свободный объём ОЗУ, выделенная память
  и итоговые JVM-аргументы; отправка на Discord webhook, если он задан.
- Запуск через [CmlLib.Core](https://github.com/CmlLib/CmlLib.Core) 4.1.0 в
  оффлайн-режиме; при `ExitCode != 0` показывается окно с ошибкой и ссылкой на лог.
- Проверка свободной ОЗУ перед запуском игры (OK/Cancel, можно отменить запуск).

## Сборка

Нужен .NET 8 SDK (Windows). В корне репозитория лежит `EscapeBook.sln`.

```powershell
dotnet restore
dotnet build
```

`dotnet build` собирает оба проекта. Проверка релизной сборки:

```powershell
dotnet build EscapeBook.sln -c Release
```

Запуск из исходников:

```powershell
dotnet run --project EscapeBook.csproj
```

### Публикация

Архитектура поставки: **bootstrapper — крошечный standalone EXE** (.NET Framework 4.8,
есть на любой Windows 10/11, ~30 КБ), **EscBClient.zip — self-contained** (внутри
полный .NET 8 Desktop Runtime, поэтому игра запускается на чистой Windows).

```powershell
# Всё одной командой (сборка + publish + архив + размеры):
powershell -ExecutionPolicy Bypass -File publish.ps1

# Или вручную:
# Основной лаунчер (self-contained) -> publish/client, затем архив для GitHub Releases
dotnet publish EscapeBook.csproj -c Release -r win-x64 --self-contained true -o publish/client
Compress-Archive -Path publish/client/* -DestinationPath publish/EscBClient.zip -Force

# Загрузчик (net48, standalone) -> publish/bootstrapper
dotnet publish EscapeBookBootstrapper/EscapeBookBootstrapper.csproj -c Release -o publish/bootstrapper
```

Подробный порядок публикации и релиза — в [`GITHUB_SETUP.md`](GITHUB_SETUP.md).

## Настройка GitHub

1. Загрузите `config.json` в репозиторий и замените в
   `Services/ConfigService.cs` константу `CONFIG_URL` на Raw-ссылку.
2. В `EscapeBookBootstrapper/Bootstrapper.cs` укажите владельца и имя
   репозитория с релизами:

   ```csharp
   private const string GitHubOwner = "your-github-owner";
   private const string GitHubRepo  = "your-github-repo";
   ```

3. В релизе GitHub должны лежать `EscapeBookBootstrapper.exe` и
   `EscBClient.zip`.

Полная инструкция: [`GITHUB_SETUP.md`](GITHUB_SETUP.md).

### Поля config.json

| Поле | Назначение |
| --- | --- |
| `jvm_preset_version` | Версия пресета JVM-аргументов. Растёт — аргументы обновляются у игроков. |
| `default_jvm_args` | JVM-аргументы по умолчанию. |
| `discord_webhook_url` | Webhook для телеметрии. Пусто — телеметрия отключена. |
| `banner_url` | Прямая ссылка на удалённый баннер. |
| `banner_hash` | SHA-256 баннера (64 hex-символа). |
| `builds[]` | Список сборок: `id`, `name`, `mc_version`, `version`, `client_zip_url`. |
| `optional_mods[]` | Доп. моды: `name`, `description`, `file_prefixes`, `enabled_by_default`. |

### Поля user_settings.json

| Поле | Назначение |
| --- | --- |
| `nickname` | Игровой ник. |
| `ramMb` | Выделенная память, МБ. |
| `customJvmArgs` | JVM-аргументы игрока. |
| `javaPath` | Ручной путь к `javaw.exe` (пусто — автоопределение). |
| `selectedBuildId` | Последняя выбранная сборка. |
| `optionalModStates` | Состояние переключателей модов (`имя` → `bool`). |
| `applied_jvm_preset_version` | Применённая версия пресета JVM. |

## Где лежат данные

```
%APPDATA%\.escapebook\
├── user_settings.json          настройки игрока
├── launcher.log                журнал основного лаунчера
├── .launcher_config.json       кэш удалённого конфига
├── assets\banner.png           баннер (встроенный или скачанный)
├── launcher\                   установленный клиент (управляет bootstrapper)
│   ├── EscBClient.exe
│   ├── .bootstrapper_version   локальный тег последнего обновления
│   └── bootstrapper.log        журнал загрузчика
└── instances\
    └── <id сборки>\
        ├── .client_version     версия установленного патча
        ├── version.txt         маркер версии для статуса сборки
        ├── .client_zip_url     URL архива (для обнаружения изменений)
        ├── logs\latest.log     лог Minecraft
        ├── mods\               моды инстанса (.disabled учитывается)
        └── ...                 файлы Minecraft (CmlLib)
```

`options.txt` игрока никогда не перезаписывается при обновлении сборки.

## JVM-аргументы

Игрок редактирует весь список аргументов в настройках. `default_jvm_args` из
`config.json` применяются только при первом запуске или при росте
`jvm_preset_version`. `-Xmx` и `-Xms` из текста игнорируются — память всегда
задаётся ползунком. К аргументам также добавляются оптимизации CmlLib (G1GC).

## Примечания

- Путь к Java: ручной из настроек → рантайм, установленный CmlLib → системная
  Java (`JAVA_HOME`, `PATH`, популярные вендоры).
- Телеметрия отправляется до запуска игры и никогда не блокирует запуск при
  ошибке сети.
- Лаунчер не закрывает Minecraft при закрытии окна лаунчера.
- Для WMI-запросов нужен обычный Windows; пакет `System.Management` подключён.
- Загрузчик собирается под .NET Framework 4.8 и не требует установки .NET 8;
  основной лаунчер поставляется self-contained (runtime внутри `EscBClient.zip`).
