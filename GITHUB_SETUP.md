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
  "secret_builds": [
    {
      "code": "SECRET2024",
      "build": {
        "id": "secret_season",
        "name": "Секретный сезон",
        "mc_version": "1.21.1",
        "version": "1.0.0",
        "client_zip_url": "https://github.com/ВАШ_АККАУНТ/escapebook-launcher/releases/download/v1.0.0/secret.zip"
      }
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
    },
    {
      "name": "JourneyMap",
      "description": "Карта мира и миникарта.",
      "file_prefixes": ["journeymap-"],
      "enabled_by_default": true,
      "group": "map_mods"
    },
    {
      "name": "Xaero's Minimap",
      "description": "Альтернативная миникарта и карта мира.",
      "file_prefixes": ["XaerosMinimap_", "XaerosWorldMap_"],
      "enabled_by_default": false,
      "group": "map_mods"
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

Игрок всё ещё может вручную включить/выключить обычный мод переключателем; состояние
сохраняется между обновлениями сборки. Для модов с `group` действует выбор одного
варианта из группы (см. раздел 3.1).

## 3.1. Группы модов (`group`)

Необязательное поле `group` в `optional_mods` делает моды взаимоисключающими: игрок
может выбрать только один мод из группы. В интерфейсе такие моды показываются
радиокнопками вместо переключателей.

```json
{
  "name": "JourneyMap",
  "description": "Карта мира и миникарта.",
  "file_prefixes": ["journeymap-"],
  "enabled_by_default": true,
  "group": "map_mods"
},
{
  "name": "Xaero's Minimap",
  "description": "Альтернативная миникарта и карта мира.",
  "file_prefixes": ["XaerosMinimap_", "XaerosWorldMap_"],
  "enabled_by_default": false,
  "group": "map_mods"
}
```

- Моды с одинаковым `group` (без учёта регистра) образуют одну группу.
- При выборе одного мода остальные моды группы автоматически выключаются: их файлы
  получают суффикс `.disabled`.
- Моды без `group` остаются обычными независимыми переключателями.
- В каждой группе задавайте `enabled_by_default: true` только у одного мода.
- Текущий выбор сохраняется в `optionalModStates` в `user_settings.json` и
  учитывается при следующем обновлении сборки.
- Группа из одного мода отображается как обычный переключатель.

## 3.2. Секретные сборки (`secret_builds`)

Массив `secret_builds` добавляет сборки, которые **не видны** в выпадающем списке по
умолчанию. Игрок активирует их, набрав код на главном окне лаунчера — отдельного поля
ввода для этого нет.

```json
"secret_builds": [
  {
    "code": "SECRET2024",
    "build": {
      "id": "secret_season",
      "name": "Секретный сезон",
      "mc_version": "1.21.1",
      "version": "1.0.0",
      "client_zip_url": "https://github.com/ВАШ_АККАУНТ/escapebook-launcher/releases/download/v1.0.0/secret.zip"
    }
  }
]
```

- `code` должен состоять только из латинских букв и цифр; регистр не важен.
- `build` использует ту же структуру, что и элементы массива `builds`, включая
  обязательные `id` и `mc_version`.
- После ввода правильного кода сборка добавляется в список и сразу выбирается.
- Выбор сохраняется в `user_settings.json` (`selectedBuildId`), но сама сборка
  остаётся скрытой до повторного ввода кода в новой сессии лаунчера.
- Коды в массиве не должны повторяться; при повторе используется первая запись.
- Код распознаётся при вводе с клавиатуры в любом месте главного окна, включая
  поле никнейма. Символы кода при этом также попадут в никнейм — при необходимости
  очистите поле и введите ник заново.

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

1. Откройте `Services/ConfigService.cs` и убедитесь, что `CONFIG_URL` указывает
   на ваш реальный Raw-адрес `config.json`.
2. Откройте `EscapeBookBootstrapper/Bootstrapper.cs` и замените константы
   `GitHubOwner` / `GitHubRepo` на репозиторий, где будут лежать релизы.
3. Соберите решение:

```powershell
dotnet restore
dotnet build EscapeBook.sln -c Release
```

Полный процесс публикации обоих приложений и оформления релиза — в разделе 11.

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

## 11. Сборка и релиз двух проектов

Проект состоит из двух приложений:

| Проект | Что это | Файл для игроков |
| --- | --- | --- |
| `EscapeBookBootstrapper/EscapeBookBootstrapper.csproj` | лёгкий загрузчик и автообновление | `EscapeBookBootstrapper.exe` |
| `EscBUpdater/EscBUpdater.csproj` | проверка обновлений по ярлыку | вшит внутрь `EscapeBookBootstrapper.exe` |
| `EscapeBook.csproj` | основной лаунчер (WinForms) | `EscBClient.zip` |

**Архитектура поставки:** bootstrapper — самостоятельный standalone EXE под
.NET Framework 4.8 (~350 КБ вместе с вшитым обновлятором, работает на чистой
Windows 10/11 без установки .NET); скачиваемый `EscBClient.zip` содержит
**полный .NET 8 Desktop Runtime** (self-contained), поэтому игра запускается на
ПК без установленного .NET 8.

### 11.1. Подготовка

1. Укажите реальный `CONFIG_URL` в `Services/ConfigService.cs`.
2. Укажите репозиторий в `EscapeBookBootstrapper/Bootstrapper.cs`:

   ```csharp
   private const string GitHubOwner = "your-github-owner";
   private const string GitHubRepo  = "your-github-repo";
   ```

3. Соберите оба проекта:

   ```powershell
   dotnet build EscapeBook.sln -c Release
   ```

### 11.2. Публикация основного лаунчера (EscBClient.zip)

```powershell
dotnet publish EscapeBook.csproj -c Release -r win-x64 --self-contained true -o publish/client
Compress-Archive -Path publish/client/* -DestinationPath publish/EscBClient.zip -Force
```

`--self-contained true` кладёт внутрь папки **полный .NET 8 Desktop Runtime**,
поэтому архив весит ~60–80 МБ (это нормально) и работает на чистой Windows.

Требования к архиву:

- `EscBClient.exe` должен лежать **в корне** архива (загрузчик проверяет его
  наличие перед распаковкой);
- `config.json` копируется в `publish/client` автоматически при публикации;
- архив должен содержать все файлы из `publish/client` (dll, deps.json,
  runtimeconfig.json, config.json, файлы рантайма).

> Не добавляйте в `EscBClient.zip` папку `user_settings.json` или инстансы —
> они хранятся отдельно в `%APPDATA%\.escapebook`.

### 11.3. Публикация загрузчика (EscapeBookBootstrapper.exe)

```powershell
dotnet publish EscapeBookBootstrapper/EscapeBookBootstrapper.csproj -c Release -o publish/bootstrapper
```

Загрузчик собирается под **.NET Framework 4.8** (предустановлен на всех
Windows 10/11): готовый `publish/bootstrapper/EscapeBookBootstrapper.exe`
занимает ~350 КБ (вместе с вшитым `EscBUpdater.exe`), самодостаточен и
**не требует установки .NET 8** на ПК игрока.
RID (`-r`) не указывается: .NET Framework не использует RID-папки.

При сборке загрузчика автоматически собирается проект `EscBUpdater`, и его
`EscBUpdater.exe` вшивается в загрузчик как ресурс: отдельный файл в релиз
прикладывать не нужно.

### 11.4. Оформление GitHub Release

1. Откройте репозиторий → **Releases** → **Create a new release**.
2. Тег: например `v1.0.0` (загрузчик сравнивает именно `tag_name`).
3. Прикрепите **ровно два** ассета:
   - `EscapeBookBootstrapper.exe` — точка входа для игроков;
   - `EscBClient.zip` — архив основного лаунчера.
4. Опубликуйте релиз.

### 11.5. Как обновляются игроки

1. Игрок запускает `EscapeBookBootstrapper.exe`.
2. Загрузчик запрашивает
   `https://api.github.com/repos/<OWNER>/<REPO>/releases/latest`.
3. Находит ассет `EscBClient.zip` и сравнивает `tag_name` с
   `%APPDATA%\.escapebook\launcher\.bootstrapper_version`.
4. Если тег новее (или файла нет) — скачивает архив, очищает папку лаунчера
   от файлов прошлой версии (сохраняя `user_settings.json`,
   `.launcher_config.json` и `instances/`), распаковывает архив, сохраняет тег
   и запускает `EscBClient.exe`.
5. После первого успешного запуска создаётся ярлык «EscB Client». Ярлык
   запускает `EscBUpdater.exe` (вшит в загрузчик): он проверяет релиз, при
   необходимости скачивает и устанавливает обновление, затем запускает клиент.
6. Если GitHub недоступен — запускает уже установленный клиент; ошибки пишутся в
   `%APPDATA%\.escapebook\launcher\bootstrapper.log` (обновлятор — в `updater.log`).

Важно: чтобы выпустить обновление, достаточно создать новый релиз с новым тегом
(`v1.0.1`, `v1.0.2`, …) и приложить к нему новый `EscBClient.zip`.

## 12. Переменные auth-worker для OTP-доставки

Для авторизации через Telegram воркеру `escapebook-auth` нужны две переменные:

| Переменная | Тип | Назначение |
| --- | --- | --- |
| `TG_BOT_TOKEN` | Secret | Токен бота от @BotFather (отправка OTP в ЛС). |
| `TG_BOT_USERNAME` | Обычная переменная | Username бота **без `@`** — из него лаунчер строит ссылку `https://t.me/<username>?start=1`. |

Задаются в `infra/auth-worker/wrangler.toml` (секция `[vars]` для username) и через
Dashboard → Workers → `escapebook-auth` → Settings → Variables and Secrets (для токена).

> Внимание: `TG_BOT_USERNAME` объявлен в `[vars]`, поэтому каждый `wrangler deploy`
> перезаписывает значение из Dashboard значением из `wrangler.toml` — вписывайте
> реальный username прямо в `wrangler.toml` перед деплоем.

Игрок обязан **нажать «Запустить» (/start) в боте**: иначе Telegram отвечает 403
(«bot can't initiate conversation»), воркер возвращает `tg_not_started`, а лаунчер
показывает инструкцию и кнопку «Открыть бота в Telegram» вместо ожидания кода.

## 13. Discord-канал OTP (секрет `DS_BOT_TOKEN`)

Второй канал авторизации: код приходит в **личные сообщения Discord** от бота EscapeBook
(`POST /auth/request-otp-discord` в `escapebook-auth`).

1. Discord Developer Portal → ваше приложение → **Bot** → скопируйте токен.
2. Cloudflare Dashboard → Workers → `escapebook-auth` → Settings → Variables and Secrets
   → добавьте секрет **`DS_BOT_TOKEN`**.
3. Игрок должен заранее привязать Discord (`/link ds_id:...` в Discord) и разрешить
   личные сообщения от участников сервера (Настройки → Конфиденциальность сервера →
   «Личные сообщения от участников сервера»).

Если ЛС запрещены, Discord отвечает 403 («Cannot send messages to this user»), воркер
возвращает `ds_dm_blocked`, а лаунчер показывает инструкцию включить ЛС. Токен хранится
только в Cloudflare; в клиенте его нет.

## 14. Variables против Secrets при деплое из GitHub Actions

- `wrangler deploy` **полностью заменяет Variables воркера** значениями из блока
  `[vars]` в `infra/telegram-bridge-worker/wrangler.toml`. Если переменная задана
  в Dashboard как Variable, но в `[vars]` её нет (или она пустая) — после деплоя
  значение **затрётся**.
- **Secrets** (Dashboard → Settings → Variables and Secrets → тип Secret) деплой
  **не затирает**: `TG_BOT_TOKEN`, `DS_BOT_TOKEN`, `TG_WEBHOOK_SECRET`,
  `BRIDGE_SECRET`, `DISCORD_CHANNEL_ID` хранятся только как Secrets.
- Поэтому в репозитории `TG_CHAT_ID = ""`, а реальный ID группы подставляется
  workflow-ом «Deploy escapebook-tg-bridge» из **repository secret `TG_CHAT_ID`**
  (GitHub → Settings → Secrets and variables → Actions → New repository secret,
  значение вида `-1001234567890`). Если секрет не задан, workflow падает на шаге
  `Inject route config` и **не трогает** работающий воркер.
- После изменения `wrangler.toml`, секретов или кода запустите workflow заново:
  Actions → «Deploy escapebook-tg-bridge» → Run workflow.

Итог: чтобы DS→TG не сломался после деплоя, `TG_CHAT_ID` живёт в
GitHub-секрете, а не в Dashboard Variable.




