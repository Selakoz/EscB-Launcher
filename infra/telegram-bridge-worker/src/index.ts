// EscapeBook telegram-bridge-worker: двусторонний мост Discord <-> Telegram.
//  • Все сообщения из Discord уходят в ОДИН Telegram-чат (TG_CHAT_ID).
//  • Медиа пересылаются как photo/document (TG) и files[0] (Discord).
//  • Reply-треды сохраняются через таблицы message_map_*.
// Токены и содержимое файлов никогда не логируются.

interface Env {
  DB: D1Database;
  TG_WEBHOOK_SECRET: string;
  TG_BOT_TOKEN: string;
  DISCORD_WEBHOOK_URL: string;
  // Optional shared secret for the /bridge/ds-to-tg route.
  BRIDGE_SECRET?: string;
  // Single Telegram group for ALL Discord messages.
  TG_CHAT_ID?: string;
  // Discord REST API (TG -> DS): bot token + target channel.
  DS_BOT_TOKEN?: string;
  DISCORD_CHANNEL_ID?: string;
}

interface TelegramUser {
  id?: number;
  username?: string;
  first_name?: string;
  is_bot?: boolean;
}

interface TelegramFileMeta {
  file_id?: string;
  file_size?: number;
}

interface TelegramMessage {
  message_id?: number;
  from?: TelegramUser;
  text?: string;
  caption?: string;
  chat?: { id?: number; type?: string };
  reply_to_message?: { message_id?: number };
  photo?: TelegramFileMeta[];
  document?: TelegramFileMeta;
  video?: TelegramFileMeta;
  audio?: TelegramFileMeta;
  voice?: TelegramFileMeta;
  animation?: TelegramFileMeta;
}

interface TelegramUpdate {
  message?: TelegramMessage;
  edited_message?: TelegramMessage;
}

interface DiscordAttachmentPayload {
  url?: string;
  filename?: string;
  content_type?: string;
}

const WEBHOOK_PATH = "/telegram/webhook";
const BRIDGE_PATH = "/bridge/ds-to-tg";

const TG_API_BASE = "https://api.telegram.org";
const TG_FILE_BASE = "https://api.telegram.org/file";
const DISCORD_API_BASE = "https://discord.com/api/v10";

const MAX_TG_FILE_BYTES = 50 * 1024 * 1024; // лимит Telegram Bot API
const MAX_DISCORD_FILE_BYTES = 25 * 1024 * 1024; // лимит загрузки Discord
const MAX_TG_TEXT = 4096;
const MAX_DISCORD_TEXT = 2000;
const MAX_CAPTION = 1024;

// Значение по умолчанию: считается «не настроено», пока не заменено.
const TG_CHAT_ID_PLACEHOLDER = "YOUR_FALLBACK_CHAT_ID";

function getChatId(value: string | undefined): string {
  const chatId = typeof value === "string" ? value.trim() : "";
  return chatId.length === 0 || chatId === TG_CHAT_ID_PLACEHOLDER ? "" : chatId;
}

const CORS_HEADERS: Record<string, string> = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type",
};

function json(data: unknown, status = 200): Response {
  return new Response(JSON.stringify(data), {
    status,
    headers: { "Content-Type": "application/json", ...CORS_HEADERS },
  });
}

// Telegram MarkdownV2: экранируем полный набор спецсимволов.
// Legacy parse_mode "Markdown" не поддерживает escape для "." "!" "(" ")" и др.,
// поэтому используется MarkdownV2 — синтаксис **bold** в нём работает так же.
function escapeTelegramMarkdown(value: string): string {
  return value.replace(/([_*\[\]()~`>#+\-=|{}.!\\])/g, "\\$1");
}

// Discord Markdown: экранируем спецсимволы, чтобы имена не ломали разметку.
function escapeMarkdown(value: string): string {
  return value.replace(/([*_~`\[\]()>#+\-=|{}.!])/g, "\\$1");
}

// ------------------------------------------------------------ Telegram API

// Разбор ошибок API без утечки секретов (только description/message).
async function readTelegramDescription(response: Response): Promise<string> {
  try {
    const data = (await response.json()) as { description?: string };
    return typeof data.description === "string" ? data.description : "";
  } catch {
    return "";
  }
}

async function readDiscordDescription(response: Response): Promise<string> {
  try {
    const data = (await response.json()) as { message?: string };
    return typeof data.message === "string" ? data.message : "";
  } catch {
    return "";
  }
}

async function sendTelegramText(
  env: Env,
  chatId: string,
  text: string,
  replyToMessageId?: string
): Promise<string | null> {
  const token = env.TG_BOT_TOKEN;
  if (typeof token !== "string" || token.length === 0) {
    console.error("tg-bridge: TG_BOT_TOKEN is not configured");
    return null;
  }

  try {
    const body: Record<string, unknown> = {
      chat_id: chatId,
      text: text.substring(0, MAX_TG_TEXT),
      parse_mode: "MarkdownV2",
    };
    if (replyToMessageId) {
      const replyId = Number(replyToMessageId);
      if (Number.isSafeInteger(replyId)) body.reply_to_message_id = replyId;
    }

    const response = await fetch(`${TG_API_BASE}/bot${token}/sendMessage`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });

    if (!response.ok) {
      const description = await readTelegramDescription(response);
      console.error(
        "tg-bridge: telegram sendMessage failed:",
        response.status,
        description
      );
      return null;
    }

    const data = (await response.json()) as { result?: { message_id?: number } };
    return data.result?.message_id ? String(data.result.message_id) : null;
  } catch (err) {
    console.error("tg-bridge: telegram sendMessage failed:", String(err));
    return null;
  }
}

function isImageAttachment(attachment: DiscordAttachmentPayload): boolean {
  const contentType = (attachment.content_type || "").toLowerCase();
  if (contentType.startsWith("image/")) return true;

  const name = (attachment.filename || "").toLowerCase();
  return /\.(png|jpe?g|gif|webp)$/.test(name);
}

async function sendTelegramMedia(
  env: Env,
  chatId: string,
  attachment: DiscordAttachmentPayload,
  caption: string | undefined,
  replyToMessageId?: string
): Promise<string | null> {
  const token = env.TG_BOT_TOKEN;
  if (typeof token !== "string" || token.length === 0) return null;

  const url = attachment.url;
  if (typeof url !== "string" || url.length === 0) return null;

  try {
    const fileResponse = await fetch(url);
    if (!fileResponse.ok) {
      console.error("tg-bridge: attachment download failed", fileResponse.status);
      return null;
    }

    const buffer = await fileResponse.arrayBuffer();
    if (buffer.byteLength > MAX_TG_FILE_BYTES) {
      console.warn("tg-bridge: attachment skipped (> 50 MB)");
      return null;
    }

    const isPhoto = isImageAttachment(attachment);
    const method = isPhoto ? "sendPhoto" : "sendDocument";
    const field = isPhoto ? "photo" : "document";
    const filename =
      attachment.filename || (isPhoto ? "photo.jpg" : "file.bin");

    const form = new FormData();
    form.append("chat_id", chatId);
    if (caption && caption.trim().length > 0) {
      form.append("caption", caption.substring(0, MAX_CAPTION));
      form.append("parse_mode", "MarkdownV2");
    }
    if (replyToMessageId) {
      const replyId = Number(replyToMessageId);
      if (Number.isSafeInteger(replyId)) {
        form.append("reply_to_message_id", String(replyId));
      }
    }
    form.append(
      field,
      new File([buffer], filename, {
        type: attachment.content_type || "application/octet-stream",
      }),
      filename
    );

    const response = await fetch(`${TG_API_BASE}/bot${token}/${method}`, {
      method: "POST",
      body: form,
    });

    if (!response.ok) {
      const description = await readTelegramDescription(response);
      console.error(
        `tg-bridge: telegram ${method} failed:`,
        response.status,
        description
      );
      return null;
    }

    const data = (await response.json()) as { result?: { message_id?: number } };
    return data.result?.message_id ? String(data.result.message_id) : null;
  } catch (err) {
    console.error("tg-bridge: telegram media send failed:", String(err));
    return null;
  }
}

// ------------------------------------------------------------- Discord API

async function sendDiscordText(
  env: Env,
  content: string,
  referenceMessageId?: string
): Promise<string | null> {
  const token = env.DS_BOT_TOKEN;
  const channelId = env.DISCORD_CHANNEL_ID;

  if (typeof token !== "string" || token.length === 0 ||
      typeof channelId !== "string" || channelId.length === 0) {
    console.error("tg-bridge: DS_BOT_TOKEN / DISCORD_CHANNEL_ID is not configured");
    return null;
  }

  try {
    const payload: Record<string, unknown> = {
      content: content.substring(0, MAX_DISCORD_TEXT),
    };
    if (referenceMessageId) {
      payload.message_reference = { message_id: referenceMessageId };
    }

    const response = await fetch(
      `${DISCORD_API_BASE}/channels/${channelId}/messages`,
      {
        method: "POST",
        headers: {
          Authorization: `Bot ${token}`,
          "Content-Type": "application/json",
        },
        body: JSON.stringify(payload),
      }
    );

    if (!response.ok) {
      const description = await readDiscordDescription(response);
      console.error(
        "tg-bridge: discord message failed:",
        response.status,
        description
      );
      return null;
    }

    const data = (await response.json()) as { id?: string };
    return typeof data.id === "string" ? data.id : null;
  } catch (err) {
    console.error("tg-bridge: discord message failed:", String(err));
    return null;
  }
}

function extractTelegramFileId(message: TelegramMessage): string | null {
  if (Array.isArray(message.photo) && message.photo.length > 0) {
    const largest = message.photo[message.photo.length - 1];
    if (largest?.file_id) return largest.file_id;
  }

  const candidates: Array<TelegramFileMeta | undefined> = [
    message.document,
    message.video,
    message.audio,
    message.voice,
    message.animation,
  ];

  for (const candidate of candidates) {
    if (candidate?.file_id) return candidate.file_id;
  }

  return null;
}

async function sendDiscordMedia(
  env: Env,
  content: string,
  fileId: string,
  referenceMessageId?: string
): Promise<string | null> {
  const token = env.DS_BOT_TOKEN;
  const channelId = env.DISCORD_CHANNEL_ID;
  const tgToken = env.TG_BOT_TOKEN;

  if (typeof token !== "string" || token.length === 0 ||
      typeof channelId !== "string" || channelId.length === 0 ||
      typeof tgToken !== "string" || tgToken.length === 0) {
    console.error("tg-bridge: Discord/Telegram token or channel is not configured");
    return null;
  }

  try {
    // 1. getFile -> file_path + size
    const metaResponse = await fetch(
      `${TG_API_BASE}/bot${tgToken}/getFile?file_id=${encodeURIComponent(fileId)}`
    );
    if (!metaResponse.ok) {
      console.error("tg-bridge: telegram getFile returned", metaResponse.status);
      return null;
    }

    const meta = (await metaResponse.json()) as {
      result?: { file_path?: string; file_size?: number };
    };
    const filePath = meta.result?.file_path;
    const fileSize = meta.result?.file_size;

    if (typeof filePath !== "string" || filePath.length === 0) {
      console.error("tg-bridge: telegram getFile has no file_path");
      return null;
    }
    if (typeof fileSize === "number" && fileSize > MAX_DISCORD_FILE_BYTES) {
      console.warn("tg-bridge: telegram file skipped (> 25 MB)");
      return null;
    }

    // 2. download
    const fileResponse = await fetch(`${TG_FILE_BASE}/bot${tgToken}/${filePath}`);
    if (!fileResponse.ok) {
      console.error("tg-bridge: telegram file download returned", fileResponse.status);
      return null;
    }

    const bytes = await fileResponse.arrayBuffer();
    if (bytes.byteLength > MAX_DISCORD_FILE_BYTES) {
      console.warn("tg-bridge: telegram file skipped (> 25 MB)");
      return null;
    }

    // 3. multipart upload to Discord
    const filename = filePath.split("/").pop() || "file";

    const payload: Record<string, unknown> = {
      content: content.substring(0, MAX_DISCORD_TEXT),
    };
    if (referenceMessageId) {
      payload.message_reference = { message_id: referenceMessageId };
    }

    const form = new FormData();
    form.append("payload_json", JSON.stringify(payload));
    form.append("files[0]", new File([bytes], filename), filename);

    const response = await fetch(
      `${DISCORD_API_BASE}/channels/${channelId}/messages`,
      {
        method: "POST",
        headers: { Authorization: `Bot ${token}` },
        body: form,
      }
    );

    if (!response.ok) {
      const description = await readDiscordDescription(response);
      console.error(
        "tg-bridge: discord media message failed:",
        response.status,
        description
      );
      return null;
    }

    const data = (await response.json()) as { id?: string };
    return typeof data.id === "string" ? data.id : null;
  } catch (err) {
    console.error("tg-bridge: discord media send failed:", String(err));
    return null;
  }
}

// ---------------------------------------------------------------- TG -> DS

// POST /telegram/webhook — Telegram update -> Discord channel (REST API).
async function handleWebhook(request: Request, env: Env): Promise<Response> {
  const secretToken = request.headers.get("X-Telegram-Bot-Api-Secret-Token");
  if (
    typeof env.TG_WEBHOOK_SECRET !== "string" ||
    env.TG_WEBHOOK_SECRET.length === 0 ||
    secretToken !== env.TG_WEBHOOK_SECRET
  ) {
    return json({ ok: false, reason: "unauthorized" }, 401);
  }

  let update: TelegramUpdate;
  try {
    update = (await request.json()) as TelegramUpdate;
    if (typeof update !== "object" || update === null) {
      return json({ ok: false, reason: "invalid_json" }, 400);
    }
  } catch {
    return json({ ok: false, reason: "invalid_json" }, 400);
  }

  const message = update.message || update.edited_message;
  if (!message) {
    return json({ ok: true, skipped: true });
  }

  // Part A: никогда не реагируем на сообщения ботов (защита от цикла
  // Telegram -> Discord -> Telegram).
  if (message.from?.is_bot) {
    return json({ ok: true, ignored: "bot_message" });
  }

  const text = typeof message.text === "string"
    ? message.text
    : typeof message.caption === "string"
      ? message.caption
      : "";

  const tgUsername =
    message.from?.username || message.from?.first_name || "Игрок";

  // Minecraft-ник для симметрии с DS->TG: ищем привязанного игрока
  // по Telegram ID (хранится как TEXT). Не найден -> без скобок.
  let mcNickname: string | null = null;
  const tgUserId = message.from?.id?.toString();
  if (tgUserId) {
    try {
      const player = await env.DB.prepare(
        "SELECT mc_nickname FROM players WHERE tg_id = ?"
      )
        .bind(tgUserId)
        .first<{ mc_nickname: string }>();

      if (
        player &&
        typeof player.mc_nickname === "string" &&
        player.mc_nickname.trim() !== ""
      ) {
        mcNickname = player.mc_nickname.trim();
      }
    } catch (err) {
      console.error("tg-bridge: mc nickname lookup failed:", String(err));
    }
  }

  const displayName = mcNickname
    ? `${escapeMarkdown(tgUsername)} (${escapeMarkdown(mcNickname)})`
    : escapeMarkdown(tgUsername);
  const discordText = `**${displayName}**: ${text}`;

  // Reply-тред: ищем Discord-сообщение, на которое ответили в Telegram.
  let referenceMessageId: string | undefined;
  const replyToId = message.reply_to_message?.message_id;
  if (typeof replyToId === "number") {
    try {
      const map = await env.DB.prepare(
        "SELECT discord_message_id FROM message_map_tg_to_ds WHERE telegram_message_id = ?"
      )
        .bind(String(replyToId))
        .first<{ discord_message_id: string }>();
      if (map?.discord_message_id) {
        referenceMessageId = map.discord_message_id;
      }
    } catch (err) {
      console.error("tg-bridge: reply lookup failed:", String(err));
    }
  }

  const fileId = extractTelegramFileId(message);
  const sentDiscordId = fileId
    ? await sendDiscordMedia(env, discordText, fileId, referenceMessageId)
    : await sendDiscordText(env, discordText, referenceMessageId);

  // Сохраняем обратное соответствие для будущих reply из Discord.
  if (sentDiscordId && typeof message.message_id === "number") {
    try {
      await env.DB.prepare(
        "INSERT OR REPLACE INTO message_map_tg_to_ds (telegram_message_id, discord_message_id, created_at) " +
          "VALUES (?, ?, strftime('%s','now'))"
      )
        .bind(String(message.message_id), sentDiscordId)
        .run();
    } catch (err) {
      console.error("tg-bridge: mapping save failed:", String(err));
    }
  }

  return json({ ok: true });
}

// ---------------------------------------------------------------- DS -> TG

// POST /bridge/ds-to-tg — body: {ds_id, ds_username, ds_nick, text, message_id,
//                                attachments[], reference_message_id}
// ВСЕ сообщения уходят в единый Telegram-чат TG_CHAT_ID.
async function handleDsToTg(request: Request, env: Env): Promise<Response> {
  if (
    typeof env.BRIDGE_SECRET === "string" &&
    env.BRIDGE_SECRET.length > 0 &&
    request.headers.get("x-bridge-secret") !== env.BRIDGE_SECRET
  ) {
    return json({ ok: false, reason: "unauthorized" }, 401);
  }

  let payload: {
    ds_id?: unknown;
    ds_username?: unknown;
    ds_nick?: unknown;
    text?: unknown;
    message_id?: unknown;
    attachments?: unknown;
    reference_message_id?: unknown;
  };

  try {
    payload = (await request.json()) as typeof payload;
    if (typeof payload !== "object" || payload === null) {
      return json({ ok: false, reason: "invalid_json" }, 400);
    }
  } catch {
    return json({ ok: false, reason: "invalid_json" }, 400);
  }

  const { ds_id, ds_username, ds_nick, text, message_id } = payload;

  if (typeof ds_id !== "string" || ds_id.length === 0) {
    return json({ ok: false, reason: "invalid_request" }, 400);
  }

  const messageId = typeof message_id === "string" ? message_id.trim() : "";
  const textValue = typeof text === "string" ? text : "";

  const attachments: DiscordAttachmentPayload[] = Array.isArray(payload.attachments)
    ? (payload.attachments as unknown[])
        .filter((item): item is DiscordAttachmentPayload =>
          typeof item === "object" && item !== null)
        .filter((item) => typeof item.url === "string" && item.url!.length > 0)
    : [];

  if (textValue.length === 0 && attachments.length === 0) {
    return json({ ok: true, skipped: true });
  }

  // English diagnostic (no secrets): what the listener actually sent.
  console.log(
    "DS->TG payload:",
    JSON.stringify({
      ds_id,
      ds_nick,
      message_id: messageId,
      attachments: attachments.length,
      reference_message_id:
        typeof payload.reference_message_id === "string"
          ? payload.reference_message_id
          : null,
    })
  );

  // Идемпотентность по message_id (защита от повторных POST-ов listener-а).
  if (messageId.length > 0) {
    const now = Math.floor(Date.now() / 1000);
    try {
      await env.DB.prepare(
        "DELETE FROM rate_limits WHERE key LIKE 'dsmsg:%' AND window_end < ?"
      )
        .bind(now)
        .run();

      const inserted = await env.DB.prepare(
        "INSERT OR IGNORE INTO rate_limits (key, count, window_end) VALUES (?, 1, ?)"
      )
        .bind(`dsmsg:${ds_id}:${messageId}`, now + 300)
        .run();

      if (!inserted.meta.changes) {
        return json({ ok: true, skipped: true, duplicate: true });
      }
    } catch (err) {
      console.error("tg-bridge: dedupe failed:", String(err));
    }
  }

  // Очистка старых соответствий (30 дней). Каждая операция изолирована:
  // отсутствие таблиц не должно блокировать доставку.
  try {
    await env.DB.prepare(
      "DELETE FROM message_map_ds_to_tg WHERE created_at < strftime('%s','now','-30 days')"
    ).run();
  } catch (err) {
    console.log("tg-bridge: mapping cleanup (ds->tg) skipped:", String(err));
  }

  try {
    await env.DB.prepare(
      "DELETE FROM message_map_tg_to_ds WHERE created_at < strftime('%s','now','-30 days')"
    ).run();
  } catch (err) {
    console.log("tg-bridge: mapping cleanup (tg->ds) skipped:", String(err));
  }

  // Единый Telegram-чат (обязателен для доставки).
  const groupChatId = getChatId(env.TG_CHAT_ID);
  if (groupChatId.length === 0) {
    console.log("TG_CHAT_ID is not configured; message skipped");
    return json({ ok: false, reason: "tg_chat_id_not_configured" });
  }

  console.log("Target Telegram chat:", groupChatId, "source: env.TG_CHAT_ID");

  // Display name: ds_nick -> ds_username -> "Игрок".
  let displayName: string;
  if (typeof ds_nick === "string" && ds_nick.trim() !== "") {
    displayName = ds_nick.trim();
  } else if (typeof ds_username === "string" && ds_username.trim() !== "") {
    displayName = ds_username.trim();
  } else {
    displayName = "Игрок";
  }

  // Minecraft-ник из D1 (или «не привязан»).
  let player: { mc_nickname: string } | null = null;
  try {
    player = await env.DB.prepare(
      "SELECT mc_nickname FROM players WHERE ds_id = ?"
    )
      .bind(ds_id)
      .first<{ mc_nickname: string }>();
  } catch (err) {
    console.error("tg-bridge db error (ds-to-tg):", String(err));
    return json({ ok: false, reason: "internal_error" }, 500);
  }

  const mcNick =
    player && typeof player.mc_nickname === "string" && player.mc_nickname.trim() !== ""
      ? player.mc_nickname.trim()
      : "не привязан";

  // MarkdownV2: литеральные скобки вокруг ника тоже обязаны быть экранированы.
  const telegramText =
    `**${escapeTelegramMarkdown(displayName)}** \\(${escapeTelegramMarkdown(mcNick)}\\): ` +
    escapeTelegramMarkdown(textValue);

  // Reply-тред: Discord message_id -> telegram_message_id.
  let replyToTelegramId: string | undefined;
  const referenceMessageId =
    typeof payload.reference_message_id === "string"
      ? payload.reference_message_id.trim()
      : "";
  if (referenceMessageId.length > 0) {
    try {
      const map = await env.DB.prepare(
        "SELECT telegram_message_id FROM message_map_ds_to_tg WHERE discord_message_id = ?"
      )
        .bind(referenceMessageId)
        .first<{ telegram_message_id: string }>();
      if (map?.telegram_message_id) {
        replyToTelegramId = map.telegram_message_id;
      }
    } catch (err) {
      console.error("tg-bridge: reply lookup (ds->tg) failed:", String(err));
    }
  }

  // Доставка: медиа (первое с подписью) или текст.
  let sentTelegramId: string | null = null;

  if (attachments.length > 0) {
    for (let index = 0; index < attachments.length; index++) {
      const attachment = attachments[index];
      const caption = index === 0 && textValue.length > 0 ? telegramText : undefined;
      const replyId = index === 0 ? replyToTelegramId : undefined;

      const sentId = await sendTelegramMedia(env, groupChatId, attachment, caption, replyId);
      if (sentId) sentTelegramId = sentId;
    }

    // Если все вложения не удалось отправить, но есть текст — шлём текст.
    if (!sentTelegramId && textValue.length > 0) {
      sentTelegramId = await sendTelegramText(env, groupChatId, telegramText, replyToTelegramId);
    }
  } else {
    sentTelegramId = await sendTelegramText(env, groupChatId, telegramText, replyToTelegramId);
  }

  if (!sentTelegramId) {
    return json({ ok: false, reason: "telegram_send_failed" });
  }

  // Сохраняем соответствие для reply-тредов из Telegram.
  if (messageId.length > 0) {
    try {
      await env.DB.prepare(
        "INSERT OR REPLACE INTO message_map_ds_to_tg (discord_message_id, telegram_message_id, created_at) " +
          "VALUES (?, ?, strftime('%s','now'))"
      )
        .bind(messageId, sentTelegramId)
        .run();
    } catch (err) {
      console.error("tg-bridge: mapping save (ds->tg) failed:", String(err));
    }
  }

  return json({ ok: true });
}

export default {
  async fetch(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    // CORS preflight.
    if (request.method === "OPTIONS") {
      return new Response(null, { status: 204, headers: CORS_HEADERS });
    }

    const url = new URL(request.url);
    const path = url.pathname;

    if (path !== WEBHOOK_PATH && path !== BRIDGE_PATH) {
      return json({ error: "not_found" }, 404);
    }
    if (request.method !== "POST") {
      return json({ error: "method_not_allowed" }, 405);
    }

    if (path === BRIDGE_PATH) {
      return handleDsToTg(request, env);
    }
    return handleWebhook(request, env);
  },
};
