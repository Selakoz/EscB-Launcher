# EscB Auth Infrastructure — руководство по развертыванию

Инфраструктура авторизации EscapeBook построена на бесплатных сервисах:

- **Cloudflare Workers + D1 (SQLite)** — API авторизации, мост Telegram → Discord,
  обработка Discord-interactions. База данных хранит `mc_nickname`, `tg_id`, `ds_id`,
  HWID-устройства, баны и OTP-коды.
- **Telegram Bot** — OTP-авторизация игроков; вебхук пересылает сообщения в Discord.
- **Hugging Face Space (Docker)** — `discord-listener` слушает Discord WebSocket и
  обращается к CF Worker, который пересылает события в Telegram.

## Структура проекта

```
infra/
├── auth-worker/                # Worker: OTP-авторизация, HWID, баны (escapebook-auth)
├── telegram-bridge-worker/     # Worker: вебхук Telegram -> Discord (escapebook-tg-bridge)
├── discord-interactions-worker/# Worker: Discord slash-команды (escapebook-discord)
├── discord-listener/           # Python (Docker): Discord WS -> CF Worker -> Telegram
├── shared/
│   └── schema.sql              # Схема базы данных D1
└── SETUP.md                    # Этот файл
```

## Шаг 1. Создайте базу данных D1

Выполните один раз (нужен установленный и авторизованный `wrangler`):

```powershell
npx wrangler d1 create escapebook-db
```

Команда выведет `database_id` — GUID вида `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`.

## Шаг 2. Впишите database_id в wrangler.toml

Откройте каждый из трех файлов и замените плейсхолдер
`<PASTE_YOUR_D1_DATABASE_ID_HERE>` на полученный `database_id`:

- `infra/auth-worker/wrangler.toml`
- `infra/telegram-bridge-worker/wrangler.toml`
- `infra/discord-interactions-worker/wrangler.toml`

```toml
[[d1_databases]]
binding = "DB"
database_name = "escapebook-db"
database_id = "ВАШ_DATABASE_ID"
```

## Шаг 3. Инициализируйте схему базы данных

Из корня репозитория:

```powershell
npx wrangler d1 execute escapebook-db --file=infra/shared/schema.sql
```

Для применения схемы к локальной (dev) базе добавьте флаг `--local`:

```powershell
npx wrangler d1 execute escapebook-db --file=infra/shared/schema.sql --local
```

Схема создает таблицы: `players`, `devices`, `otp_codes`, `rate_limits`.

### 3.1. Миграция: tg_id/ds_id → TEXT (Snowflake fix)

Telegram/Discord ID — это Snowflake-числа больше `Number.MAX_SAFE_INTEGER` (2^53).
Раньше `players.tg_id` и `players.ds_id` были `INTEGER`, из-за чего большие ID
искажались, а `/link ds_id:715134156798951454` отклонялся валидацией. Теперь оба
поля — `TEXT` (строки из цифр), на клиенте и воркерах они никогда не приводятся к
`Number`.

Для существующих баз выполните миграцию (данные сохраняются через `CAST`, таблица
пересоздается по стандартному SQLite-паттерну):

```powershell
npx wrangler d1 execute escapebook-db --remote --file=infra/shared/migrations/001_ids_to_text.sql
```

**Перепривязка аккаунтов после миграции НЕ требуется**: существующие значения
`tg_id`/`ds_id` переносятся как есть (`CAST(... AS TEXT)`). Исключение — строки,
которые до миграции уже были записаны с потерей точности (ID > 2^53, сохранённые
как INTEGER): их значения округлены, и для таких игроков `/link` нужно выполнить
заново с корректным ID. Новые схемы (`schema.sql`) уже создают поля как `TEXT`.

### 3.2. Перерегистрация slash-команд (очистка глобальных + guild)

После изменения опций `tg_id`/`ds_id` с INTEGER (type 4) на STRING (type 3)
команды нужно перерегистрировать. Discord валидирует INTEGER-опции на стороне
клиента по диапазону safe-integer, поэтому старые глобальные команды продолжают
отклонять Snowflake ID даже после обновления воркера — их необходимо удалить.

Скрипт `register-guild.mjs` выполняет два шага строго по порядку и печатает
HTTP-статус каждого:

1. `PUT []` → `/applications/<DS_APP_ID>/commands` — **очищает устаревшие
   глобальные команды** (с INTEGER-опциями).
2. `PUT <5 определений>` → `/applications/<DS_APP_ID>/guilds/<DS_GUILD_ID>/commands` —
   регистрирует обновлённые команды на сервер (**мгновенный эффект**).

Запуск (PowerShell, из `infra/discord-interactions-worker`):

```powershell
$env:DS_APP_ID="<application id>"
$env:DS_BOT_TOKEN="<bot token>"
$env:DS_GUILD_ID="<server id>"
node register-guild.mjs
```

Переменные окружения: `DS_APP_ID`, `DS_BOT_TOKEN`, `DS_GUILD_ID` — токен
передаётся только через окружение и никогда не печатается; при ошибке скрипт
выводит полный JSON-ответ Discord. Определения команд держите в синхроне с
`src/commands.ts`.

> После очистки глобальных команд **guild-команды являются единственным
> авторитетным источником**: на сервере действуют они (мгновенно), а глобальный
> набор пуст и больше не перехватывает ввод INTEGER-валидацией.

## Шаг 4. Задайте секреты через Cloudflare Dashboard

Секреты **никогда** не хранятся в репозитории. Задайте их в панели Cloudflare:

**Dashboard → Workers & Pages → <имя воркера> → Settings → Variables and Secrets → Add**

| Воркер | Секрет | Назначение |
| --- | --- | --- |
| `escapebook-auth` | `ADMIN_KEY` | Общий секрет для `/admin/*` endpoints |
| `escapebook-auth` | `TG_BOT_TOKEN` | Токен бота Telegram — OTP отправляется напрямую в ЛС игрока |
| `escapebook-auth` | `TG_BOT_USERNAME` | (переменная, не секрет) username бота без `@` для deep-link `t.me/...?start=1` |
| `escapebook-auth` | `DS_BOT_TOKEN` | Токен Discord-бота — OTP отправляется в ЛС (`/auth/request-otp-discord`) |
| `escapebook-tg-bridge` | `TG_BOT_TOKEN` | Токен бота Telegram (отправка в `/bridge/ds-to-tg`) |
| `escapebook-tg-bridge` | `TG_WEBHOOK_SECRET` | Секрет вебхука (`X-Telegram-Bot-Api-Secret-Token`) |
| `escapebook-tg-bridge` | `DISCORD_WEBHOOK_URL` | Discord webhook для пересылки |
| `escapebook-tg-bridge` | `BRIDGE_SECRET` | (опционально) общий секрет для `/bridge/ds-to-tg` (заголовок `x-bridge-secret`) |
| `escapebook-tg-bridge` | `TG_CHAT_ID` | (переменная, не секрет) единый чат Telegram для ВСЕХ сообщений из Discord (`[vars]`) |
| `escapebook-tg-bridge` | `DS_BOT_TOKEN` | Discord bot token (REST API: отправка сообщений/файлов в канал) |
| `escapebook-tg-bridge` | `DISCORD_CHANNEL_ID` | (переменная) ID канала Discord для Telegram→Discord |

> Мост Discord → Telegram: listener отправляет `{ds_id, ds_username, text}`.
> Для привязанного игрока сообщение уходит в его ЛС и подписывается ником
> Minecraft; для **непривязанного** — в общий чат `TG_CHAT_ID` с Discord-username
> в качестве имени. Если `TG_CHAT_ID` пуст, такие сообщения пропускаются
> (в логе воркера: `telegram target chat is not configured`).
| `escapebook-discord` | `DS_PUBLIC_KEY` | Публичный ключ приложения Discord (проверка подписей) |
| `escapebook-discord` | `DS_APP_ID` | ID приложения Discord (регистрация slash-команд) |
| `escapebook-discord` | `DS_BOT_TOKEN` | Токен бота Discord (регистрация slash-команд) |
| `escapebook-discord` | `ADMIN_ROLE_ID` | ID роли Discord для админ-команд (/add, /link, /ban, /unban) |

Тип секрета выбирайте **Secret** (зашифрованный).

## Шаг 5. Задеплойте воркеры

Для каждого воркера:

```powershell
cd infra/auth-worker
npm install
npx wrangler deploy
```

```powershell
cd infra/telegram-bridge-worker
npm install
npx wrangler deploy
```

```powershell
cd infra/discord-interactions-worker
npm install
npx wrangler deploy
```

После деплоя каждый воркер получает публичный URL вида:

```
https://escapebook-auth.<ваш-аккаунт>.workers.dev
https://escapebook-tg-bridge.<ваш-аккаунт>.workers.dev
https://escapebook-discord.<ваш-аккаунт>.workers.dev
```

## Шаг 6. Настройте внешние вебхуки

### Telegram (BotFather)

1. Создайте бота: **@BotFather → /newbot**.
2. Сохраните токен в секреты воркеров (Шаг 4).
3. Установите вебхук на telegram-bridge-worker:

   ```
   @BotFather → /setwebhook → https://escapebook-tg-bridge.<ваш-аккаунт>.workers.dev/telegram
   ```

   (точный путь вебхука уточняется при реализации worker'а).

### Discord (Developer Portal)

1. **discord.com/developers/applications → New Application**.
2. Скопируйте **Public Key** в секрет `DISCORD_PUBLIC_KEY`.
3. В разделе **Bot** скопируйте токен в секрет `DISCORD_BOT_TOKEN`.
4. В разделе **Interactions Endpoint URL** укажите:

   ```
   https://escapebook-discord.<ваш-аккаунт>.workers.dev/interactions
   ```

5. Добавьте бота на сервер с правами отправки сообщений и использования slash-команд.

## Шаг 7. discord-listener (Hugging Face Space)

1. Создайте Space: **huggingface.co/new-space → Docker → blank**.
2. Скопируйте файлы из `infra/discord-listener/` в корень Space
   (`Dockerfile`, `main.py`, `requirements.txt`, `.dockerignore`).
3. В настройках Space (Settings → Variables and secrets) задайте переменные
   окружения:

   | Переменная | Назначение |
   | --- | --- |
   | `DS_BOT_TOKEN` | Токен Discord-бота (официальный discord.py, не self-bot) |
   | `DISCORD_CHANNEL_ID` | ID канала, сообщения из которого пересылаются в Telegram |
   | `AUTH_WORKER_URL` | URL telegram-bridge-worker (`https://escapebook-tg-bridge...`) |
   | `BRIDGE_SECRET` | (опционально) общий секрет `/bridge/ds-to-tg`, если задан `BRIDGE_SECRET` у воркера |

   > Токен Telegram-бота в Space **не задается** — он хранится только в
   > секретах Cloudflare (`TG_BOT_TOKEN`); listener отправляет воркеру лишь
   > `ds_id` и `text`.

4. В Discord Developer Portal → ваше приложение → **Bot** включите
   **Message Content Intent** (без него бот не увидит текст сообщений).

5. Space автоматически соберет Docker-образ и запустит `app.py`, который
   слушает Discord WebSocket и пересылает события через
   `POST <AUTH_WORKER_URL>/bridge/ds-to-tg` в CF Worker → Telegram.

## 8. Привязки и ошибки доставки OTP

- `POST /auth/links` (body `{mc_nickname, hwid}`) — только для доверенного устройства
  (`devices.trusted = 1`). Ответ: `{ok:true, tg_linked, ds_linked, bot_username}`;
  сырые `tg_id`/`ds_id` не возвращаются. Неизвестный ник и недоверенное устройство
  получают одинаковый `{ok:false, reason:"unauthorized"}` (без oracle).
  Лимит: 30 запросов / 10 минут на ник.
- Новые причины отказа `/auth/request-otp`:
  - `tg_not_started` — бот не может написать первым (403,
    «bot can't initiate conversation», «user is deactivated»). Слот rate-limit
    возвращается, клиент показывает инструкцию и кнопку «Открыть бота в Telegram»
    (повторная попытка через 10 с).
  - `tg_delivery_failed` — прочие сбои доставки; клиент предлагает повторить
    позже или написать @Selakoz.

## Проверка
- `GET https://escapebook-auth.<ваш-аккаунт>.workers.dev/health` — должен вернуть 200
  (после реализации endpoint'а; сейчас placeholder возвращает `placeholder`).
- Схема БД: `npx wrangler d1 execute escapebook-db --command="SELECT name FROM sqlite_master WHERE type='table'"`.

## Примечания

- KV-неймспейсы **не используются** — только D1.
- Все URL, токены и ID задаются через переменные окружения/секреты;
  в коде и репозитории нет ни одного захардкоженного значения.
