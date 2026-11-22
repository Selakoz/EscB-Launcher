// EscapeBook auth-worker: OTP authentication, device (HWID) trust and admin
// endpoints backed by Cloudflare D1 (binding name: DB).
// All queries are parameterized. Tokens, OTP codes and session tokens are
// never logged. Secrets (ADMIN_KEY) come from wrangler/Dashboard only.

interface Env {
  DB: D1Database;
  ADMIN_KEY: string;
  TG_BOT_TOKEN: string;
  // Non-secret bot username (without @) used for the t.me deep link.
  TG_BOT_USERNAME?: string;
  // Discord bot token: OTP delivery via DMs (second auth channel).
  DS_BOT_TOKEN?: string;
}

interface TelegramSendResult {
  ok: boolean;
  // True when Telegram refused because the user never pressed /start.
  notStarted: boolean;
}

interface DiscordSendResult {
  ok: boolean;
  // True when the DM could not be delivered because the user blocks DMs.
  blocked: boolean;
}

interface PlayerRow {
  mc_nickname: string;
  // Snowflake IDs (Telegram/Discord) are stored as TEXT: they exceed
  // Number.MAX_SAFE_INTEGER and would lose precision as JS numbers.
  tg_id: string | null;
  ds_id: string | null;
  is_banned: number;
  created_at: number;
}

interface DeviceRow {
  mc_nickname: string;
  hwid: string;
  trusted: number;
  last_seen: number;
}

interface OtpRow {
  code: string;
  expires_at: number;
  confirmed: number;
  attempts: number;
}

interface RateLimitRow {
  count: number;
  window_end: number;
}

// Session lifetime: 30 days. Session tokens are stored in the `sessions`
// table so they can be validated later (GET /auth/validate).
const SESSION_TTL_SECONDS = 30 * 24 * 60 * 60;

const NICKNAME_RE = /^[A-Za-z0-9_]{3,16}$/;
// Numeric strings for Snowflake IDs (5-25 digits): validated as strings,
// never converted to Number (precision loss above 2^53).
const NUMERIC_ID_RE = /^\d{5,25}$/;

// Route table: pathname -> allowed HTTP method.
const ROUTES: Record<string, string> = {
  "/auth/request-otp": "POST",
  "/auth/confirm-otp": "POST",
  "/auth/register-device": "POST",
  "/auth/links": "POST",
  "/auth/validate": "GET",
  "/auth/request-otp-discord": "POST",
  "/admin/ban": "POST",
  "/admin/unban": "POST",
  "/admin/link": "POST",
  "/admin/info": "GET",
};

const CORS_HEADERS: Record<string, string> = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type, Authorization",
};

function nowSeconds(): number {
  return Math.floor(Date.now() / 1000);
}

function json(data: unknown, status = 200): Response {
  return new Response(JSON.stringify(data), {
    status,
    headers: { "Content-Type": "application/json", ...CORS_HEADERS },
  });
}

function ok(extra: Record<string, unknown> = {}): Response {
  return json({ ok: true, ...extra });
}

function fail(reason: string, status = 200): Response {
  return json({ ok: false, reason }, status);
}

// Generate a 32-char hex session token using the Workers crypto API.
function generateSessionToken(): string {
  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  return Array.from(bytes)
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");
}

// Parse and validate a JSON request body. Rejects non-JSON content types.
async function readJsonBody(request: Request): Promise<Record<string, any> | null> {
  const contentType = request.headers.get("Content-Type") || "";
  if (!contentType.includes("application/json")) return null;
  try {
    const body = await request.json();
    if (typeof body !== "object" || body === null) return null;
    return body as Record<string, any>;
  } catch {
    return null;
  }
}

function adminAuthorized(key: unknown, env: Env): boolean {
  return (
    typeof env.ADMIN_KEY === "string" &&
    env.ADMIN_KEY.length > 0 &&
    typeof key === "string" &&
    key === env.ADMIN_KEY
  );
}

// ------------------------------------------------------------ rate limits

// Returns true when the request is allowed, false when the window is full.
async function consumeRateLimit(
  env: Env,
  key: string,
  max: number,
  windowSeconds: number
): Promise<boolean> {
  const now = nowSeconds();
  const row = await env.DB.prepare(
    "SELECT count, window_end FROM rate_limits WHERE key = ?"
  )
    .bind(key)
    .first<RateLimitRow>();

  if (row && Number(row.window_end) > now) {
    if (Number(row.count) >= max) return false;
    await env.DB.prepare(
      "UPDATE rate_limits SET count = count + 1 WHERE key = ?"
    )
      .bind(key)
      .run();
    return true;
  }

  await env.DB.prepare(
    "INSERT OR REPLACE INTO rate_limits (key, count, window_end) VALUES (?, 1, ?)"
  )
    .bind(key, now + windowSeconds)
    .run();
  return true;
}

// Gives back a consumed slot (used when Telegram delivery never happened).
async function refundRateLimit(env: Env, key: string): Promise<void> {
  try {
    await env.DB.prepare(
      "UPDATE rate_limits SET count = MAX(0, count - 1) WHERE key = ?"
    )
      .bind(key)
      .run();
  } catch (err) {
    console.error("auth-worker: rate-limit refund failed:", String(err));
  }
}

function isLinkedId(value: string | null | undefined): boolean {
  return typeof value === "string" && value.trim().length > 0;
}

// ------------------------------------------------------- discord DM delivery

async function readDiscordError(response: Response): Promise<string> {
  try {
    const parsed = (await response.json()) as { message?: string };
    return typeof parsed?.message === "string" ? parsed.message : "";
  } catch {
    return "";
  }
}

function isDmBlocked(status: number, description: string): boolean {
  const lower = description.toLowerCase();
  return status === 403 || lower.includes("cannot send messages to this user");
}

// Creates a DM channel with the recipient and sends the OTP message.
// Logs status/description only — never the token, the code or the raw ds_id.
async function sendOtpToDiscord(
  dsId: string,
  code: string,
  env: Env
): Promise<DiscordSendResult> {
  const token = env.DS_BOT_TOKEN;
  if (typeof token !== "string" || token.length === 0) {
    console.error("auth-worker: DS_BOT_TOKEN is not configured");
    return { ok: false, blocked: false };
  }

  let channelId = "";

  try {
    const channelResponse = await fetch("https://discord.com/api/v10/users/@me/channels", {
      method: "POST",
      headers: {
        Authorization: `Bot ${token}`,
        "Content-Type": "application/json",
      },
      body: JSON.stringify({ recipient_id: dsId }),
    });

    if (!channelResponse.ok) {
      const description = await readDiscordError(channelResponse);
      console.error(
        "auth-worker: discord DM channel failed:",
        channelResponse.status,
        description.slice(0, 160)
      );
      return { ok: false, blocked: isDmBlocked(channelResponse.status, description) };
    }

    const channel = (await channelResponse.json()) as { id?: string };
    channelId = typeof channel?.id === "string" ? channel.id : "";
    if (channelId.length === 0) {
      console.error("auth-worker: discord DM channel response has no id");
      return { ok: false, blocked: false };
    }
  } catch (err) {
    console.error("auth-worker: discord DM channel failed:", String(err));
    return { ok: false, blocked: false };
  }

  try {
    const messageResponse = await fetch(
      `https://discord.com/api/v10/channels/${channelId}/messages`,
      {
        method: "POST",
        headers: {
          Authorization: `Bot ${token}`,
          "Content-Type": "application/json",
        },
        body: JSON.stringify({
          // Код скрыт спойлером Discord (||...||): не виден при шаринге экрана.
          content: `Ваш код для входа в EscB Client: ||${code}||\nНикому не сообщайте этот код.`,
        }),
      }
    );

    if (messageResponse.ok) return { ok: true, blocked: false };

    const description = await readDiscordError(messageResponse);
    console.error(
      "auth-worker: discord DM message failed:",
      messageResponse.status,
      description.slice(0, 160)
    );
    return { ok: false, blocked: isDmBlocked(messageResponse.status, description) };
  } catch (err) {
    console.error("auth-worker: discord DM message failed:", String(err));
    return { ok: false, blocked: false };
  }
}

// Deliver the OTP directly to the player's Telegram DM.
// Logs status/description only — never the token or the code.
async function sendOtpToTelegram(
  tgId: string,
  code: string,
  env: Env
): Promise<TelegramSendResult> {
  try {
    if (typeof env.TG_BOT_TOKEN !== "string" || env.TG_BOT_TOKEN.length === 0) {
      console.error("auth-worker: TG_BOT_TOKEN is not configured");
      return { ok: false, notStarted: false };
    }
    const response = await fetch(
      `https://api.telegram.org/bot${env.TG_BOT_TOKEN}/sendMessage`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          chat_id: tgId,
          // Код скрыт спойлером: не читается при шаринге экрана/скриншотах.
          text: `Ваш код для входа в EscB Client: <tg-spoiler>${code}</tg-spoiler>\nНикому не сообщайте этот код.`,
          parse_mode: "HTML",
        }),
      }
    );

    if (response.ok) return { ok: true, notStarted: false };

    let description = "";
    try {
      const parsed = (await response.json()) as { description?: string };
      description = typeof parsed?.description === "string" ? parsed.description : "";
    } catch {
      // Non-JSON Telegram response — status is enough.
    }

    const lower = description.toLowerCase();
    const notStarted =
      response.status === 403 ||
      lower.includes("bot can't initiate conversation") ||
      lower.includes("user is deactivated");

    console.error(
      "auth-worker: telegram sendMessage failed:",
      response.status,
      description.slice(0, 160)
    );
    return { ok: false, notStarted };
  } catch (err) {
    console.error("auth-worker: telegram sendMessage failed:", String(err));
    return { ok: false, notStarted: false };
  }
}

// POST /auth/request-otp — body: {mc_nickname, hwid}
async function handleRequestOTP(request: Request, env: Env): Promise<Response> {
  const body = await readJsonBody(request);
  if (!body) return fail("invalid_json", 400);

  const { mc_nickname, hwid } = body;
  if (typeof mc_nickname !== "string" || !NICKNAME_RE.test(mc_nickname)) {
    return fail("invalid_nickname");
  }
  if (typeof hwid !== "string" || hwid.length === 0) {
    return fail("invalid_request");
  }

  const now = nowSeconds();

  // Rate limit: max 3 OTP requests per nickname per 10-minute window.
  const rateKey = "otp:" + mc_nickname;
  const allowed = await consumeRateLimit(env, rateKey, 3, 600);
  if (!allowed) return fail("rate_limited");

  const player = await env.DB.prepare(
    "SELECT tg_id, is_banned FROM players WHERE mc_nickname = ?"
  )
    .bind(mc_nickname)
    .first<{ tg_id: string | null; is_banned: number }>();
  if (!player) return fail("player_not_found");
  if (Number(player.is_banned) === 1) return fail("banned");
  if (
    player.tg_id === null ||
    player.tg_id === undefined ||
    String(player.tg_id).length === 0
  ) {
    return fail("no_tg_linked");
  }

  // 6-digit OTP, valid for 5 minutes, resets attempts counter.
  const code = Math.floor(100000 + Math.random() * 900000).toString();
  await env.DB.prepare(
    "INSERT OR REPLACE INTO otp_codes (mc_nickname, code, expires_at, confirmed, attempts) VALUES (?, ?, ?, 0, 0)"
  )
    .bind(mc_nickname, code, now + 300)
    .run();

  // Send the OTP to the player's Telegram DM (chat_id accepts a numeric
  // string). Delivery problems are reported explicitly so the client can
  // guide the user instead of silently waiting for a code.
  const delivery = await sendOtpToTelegram(String(player.tg_id), code, env);

  if (!delivery.ok) {
    // The user could not receive anything: refund the rate-limit slot and
    // drop the unusable OTP row.
    await refundRateLimit(env, rateKey);
    await env.DB.prepare("DELETE FROM otp_codes WHERE mc_nickname = ?")
      .bind(mc_nickname)
      .run();

    if (delivery.notStarted) {
      return json({
        ok: false,
        reason: "tg_not_started",
        bot_username:
          typeof env.TG_BOT_USERNAME === "string" ? env.TG_BOT_USERNAME : "",
      });
    }
    return fail("tg_delivery_failed");
  }

  // NOTE: the OTP code itself is never returned to the client and never logged.
  return ok();
}

// POST /auth/request-otp-discord — body: {mc_nickname, hwid}
// Second OTP channel: the code is delivered via a Discord DM from the bot.
// Confirmation reuses /auth/confirm-otp (same auto-trust of the HWID).
async function handleRequestOtpDiscord(request: Request, env: Env): Promise<Response> {
  const body = await readJsonBody(request);
  if (!body) return fail("invalid_json", 400);

  const { mc_nickname, hwid } = body;
  if (typeof mc_nickname !== "string" || !NICKNAME_RE.test(mc_nickname)) {
    return fail("invalid_nickname");
  }
  if (typeof hwid !== "string" || hwid.length === 0) {
    return fail("invalid_request");
  }

  const now = nowSeconds();

  // Separate bucket: max 3 Discord OTP requests per nickname / 10 minutes.
  const rateKey = "otp_ds:" + mc_nickname;
  const allowed = await consumeRateLimit(env, rateKey, 3, 600);
  if (!allowed) return fail("rate_limited");

  const player = await env.DB.prepare(
    "SELECT ds_id, is_banned FROM players WHERE mc_nickname = ?"
  )
    .bind(mc_nickname)
    .first<{ ds_id: string | null; is_banned: number }>();
  if (!player) return fail("player_not_found");
  if (Number(player.is_banned) === 1) return fail("banned");
  if (!isLinkedId(player.ds_id)) return fail("no_ds_linked");

  const code = Math.floor(100000 + Math.random() * 900000).toString();
  const delivery = await sendOtpToDiscord(String(player.ds_id), code, env);

  if (!delivery.ok) {
    // Nothing was delivered: refund the slot; no OTP row was created yet.
    await refundRateLimit(env, rateKey);
    return fail(delivery.blocked ? "ds_dm_blocked" : "ds_delivery_failed");
  }

  await env.DB.prepare(
    "INSERT OR REPLACE INTO otp_codes (mc_nickname, code, expires_at, confirmed, attempts) VALUES (?, ?, ?, 0, 0)"
  )
    .bind(mc_nickname, code, now + 300)
    .run();

  // NOTE: the OTP code itself is never returned to the client and never logged.
  return ok();
}

// POST /auth/confirm-otp — body: {mc_nickname, code, hwid}
async function handleConfirmOTP(request: Request, env: Env): Promise<Response> {
  const body = await readJsonBody(request);
  if (!body) return fail("invalid_json", 400);

  const { mc_nickname, code, hwid } = body;
  if (typeof mc_nickname !== "string" || !NICKNAME_RE.test(mc_nickname)) {
    return fail("invalid_nickname");
  }
  if (typeof code !== "string" || code.length === 0) {
    return fail("invalid_request");
  }

  const now = nowSeconds();
  const row = await env.DB.prepare(
    "SELECT code, expires_at, confirmed, attempts FROM otp_codes WHERE mc_nickname = ?"
  )
    .bind(mc_nickname)
    .first<OtpRow>();
  if (!row) return fail("no_otp_requested");

  if (Number(row.expires_at) < now) {
    await env.DB.prepare("DELETE FROM otp_codes WHERE mc_nickname = ?")
      .bind(mc_nickname)
      .run();
    return fail("otp_expired");
  }

  if (Number(row.attempts) >= 5) {
    await env.DB.prepare("DELETE FROM otp_codes WHERE mc_nickname = ?")
      .bind(mc_nickname)
      .run();
    return fail("too_many_attempts");
  }

  if (row.code !== code) {
    await env.DB.prepare(
      "UPDATE otp_codes SET attempts = attempts + 1 WHERE mc_nickname = ?"
    )
      .bind(mc_nickname)
      .run();
    return fail("invalid_code");
  }

  // Auto-trust: the device that confirmed the OTP is trusted immediately, so
  // the client needs no separate /auth/register-device call.
  if (typeof hwid === "string" && hwid.length > 0) {
    await env.DB.prepare(
      "INSERT INTO devices (mc_nickname, hwid, trusted, last_seen) VALUES (?, ?, 1, strftime('%s','now')) " +
        "ON CONFLICT(mc_nickname, hwid) DO UPDATE SET trusted = 1, last_seen = strftime('%s','now')"
    )
      .bind(mc_nickname, hwid)
      .run();
  }

  const sessionToken = generateSessionToken();

  // Persist the session so it can be validated by GET /auth/validate.
  await env.DB.prepare(
    "INSERT INTO sessions (token, mc_nickname, created_at, expires_at) VALUES (?, ?, ?, ?)"
  )
    .bind(sessionToken, mc_nickname, now, now + SESSION_TTL_SECONDS)
    .run();

  // The OTP is single-use: delete it right after a successful confirmation.
  await env.DB.prepare("DELETE FROM otp_codes WHERE mc_nickname = ?")
    .bind(mc_nickname)
    .run();

  return ok({ session_token: sessionToken });
}

// POST /auth/register-device — body: {mc_nickname, hwid, session_token}
async function handleRegisterDevice(request: Request, env: Env): Promise<Response> {
  const body = await readJsonBody(request);
  if (!body) return fail("invalid_json", 400);

  const { mc_nickname, hwid, session_token } = body;
  if (typeof mc_nickname !== "string" || !NICKNAME_RE.test(mc_nickname)) {
    return fail("invalid_nickname");
  }
  if (typeof hwid !== "string" || hwid.length === 0) {
    return fail("invalid_request");
  }
  if (typeof session_token !== "string" || session_token.length === 0) {
    return fail("invalid_session");
  }

  const confirmed = await env.DB.prepare(
    "SELECT mc_nickname FROM otp_codes WHERE mc_nickname = ? AND confirmed = 1"
  )
    .bind(mc_nickname)
    .first<{ mc_nickname: string }>();
  if (!confirmed) return fail("invalid_session");

  await env.DB.prepare(
    "INSERT OR REPLACE INTO devices (mc_nickname, hwid, trusted, last_seen) VALUES (?, ?, 1, ?)"
  )
    .bind(mc_nickname, hwid, nowSeconds())
    .run();

  // One confirmed OTP grants exactly one device registration.
  await env.DB.prepare("DELETE FROM otp_codes WHERE mc_nickname = ?")
    .bind(mc_nickname)
    .run();

  return ok();
}

// POST /auth/links — body: {mc_nickname, hwid}
// Returns linked-account flags for a TRUSTED device. Raw IDs are never
// returned; unknown nickname and untrusted device share one generic answer.
async function handleLinks(request: Request, env: Env): Promise<Response> {
  const body = await readJsonBody(request);
  if (!body) return fail("invalid_json", 400);

  const { mc_nickname, hwid } = body;
  if (typeof mc_nickname !== "string" || !NICKNAME_RE.test(mc_nickname)) {
    return fail("invalid_nickname");
  }
  if (typeof hwid !== "string" || hwid.length === 0) {
    return fail("invalid_request");
  }

  // Reuse the rate-limit table: max 30 lookups / 10 min per nickname.
  const allowed = await consumeRateLimit(env, "links:" + mc_nickname, 30, 600);
  if (!allowed) return fail("rate_limited");

  const device = await env.DB.prepare(
    "SELECT hwid FROM devices WHERE mc_nickname = ? AND hwid = ? AND trusted = 1"
  )
    .bind(mc_nickname, hwid)
    .first<{ hwid: string }>();

  const player = await env.DB.prepare(
    "SELECT tg_id, ds_id FROM players WHERE mc_nickname = ?"
  )
    .bind(mc_nickname)
    .first<{ tg_id: string | null; ds_id: string | null }>();

  // Identical generic response for unknown nickname and untrusted device.
  if (!device || !player) return fail("unauthorized");

  return ok({
    tg_linked: isLinkedId(player.tg_id),
    ds_linked: isLinkedId(player.ds_id),
    bot_username:
      typeof env.TG_BOT_USERNAME === "string" ? env.TG_BOT_USERNAME : "",
  });
}

// GET /auth/validate — Authorization: Bearer <session_token>
// Used by other workers (e.g. skins uploads) to verify a session without
// touching the OTP flow. Returns {valid: true, nickname} or 401.
async function handleValidate(request: Request, env: Env): Promise<Response> {
  let token = "";
  const header = request.headers.get("Authorization") || "";
  if (header.toLowerCase().startsWith("bearer ")) {
    token = header.slice(7).trim();
  }
  if (token.length === 0) {
    return json({ valid: false, error: "invalid_or_missing_token" }, 401);
  }

  try {
    const session = await env.DB.prepare(
      "SELECT s.mc_nickname FROM sessions s " +
        "JOIN players p ON p.mc_nickname = s.mc_nickname " +
        "WHERE s.token = ? AND s.expires_at > ? AND p.is_banned = 0"
    )
      .bind(token, nowSeconds())
      .first<{ mc_nickname: string }>();

    if (!session) {
      return json({ valid: false, error: "invalid_or_missing_token" }, 401);
    }

    return json({ valid: true, nickname: session.mc_nickname });
  } catch (err) {
    // Never log the token itself; the error class is enough.
    console.error("auth-worker: session validation failed:", String(err));
    return json({ valid: false, error: "invalid_or_missing_token" }, 401);
  }
}

// POST /admin/ban — body: {mc_nickname, admin_key}
async function handleBan(request: Request, env: Env): Promise<Response> {
  const body = await readJsonBody(request);
  if (!body) return fail("invalid_json", 400);
  if (!adminAuthorized(body.admin_key, env)) return fail("unauthorized", 401);

  const { mc_nickname } = body;
  if (typeof mc_nickname !== "string" || !NICKNAME_RE.test(mc_nickname)) {
    return fail("invalid_nickname");
  }

  const result = await env.DB.prepare(
    "UPDATE players SET is_banned = 1 WHERE mc_nickname = ?"
  )
    .bind(mc_nickname)
    .run();
  if (!result.meta.changes) return fail("player_not_found", 404);

  return ok();
}

// POST /admin/unban — body: {mc_nickname, admin_key}
async function handleUnban(request: Request, env: Env): Promise<Response> {
  const body = await readJsonBody(request);
  if (!body) return fail("invalid_json", 400);
  if (!adminAuthorized(body.admin_key, env)) return fail("unauthorized", 401);

  const { mc_nickname } = body;
  if (typeof mc_nickname !== "string" || !NICKNAME_RE.test(mc_nickname)) {
    return fail("invalid_nickname");
  }

  const result = await env.DB.prepare(
    "UPDATE players SET is_banned = 0 WHERE mc_nickname = ?"
  )
    .bind(mc_nickname)
    .run();
  if (!result.meta.changes) return fail("player_not_found", 404);

  return ok();
}

// POST /admin/link — body: {mc_nickname, tg_id?, ds_id?, admin_key}
async function handleLink(request: Request, env: Env): Promise<Response> {
  const body = await readJsonBody(request);
  if (!body) return fail("invalid_json", 400);
  if (!adminAuthorized(body.admin_key, env)) return fail("unauthorized", 401);

  const { mc_nickname, tg_id, ds_id } = body;
  if (typeof mc_nickname !== "string" || !NICKNAME_RE.test(mc_nickname)) {
    return fail("invalid_nickname");
  }

  const hasTg = tg_id !== undefined && tg_id !== null && String(tg_id).length > 0;
  const hasDs = ds_id !== undefined && ds_id !== null && String(ds_id).length > 0;
  if (!hasTg && !hasDs) return fail("invalid_request");

  // Snowflake IDs are handled strictly as numeric strings (no Number()).
  const tgStr = hasTg ? String(tg_id) : null;
  const dsStr = hasDs ? String(ds_id) : null;
  if ((tgStr !== null && !NUMERIC_ID_RE.test(tgStr)) || (dsStr !== null && !NUMERIC_ID_RE.test(dsStr))) {
    return fail("invalid_request");
  }

  // Build a dynamic but fully parameterized UPDATE statement.
  const sets: string[] = [];
  const binds: unknown[] = [];
  if (hasTg) {
    sets.push("tg_id = ?");
    binds.push(tgStr);
  }
  if (hasDs) {
    sets.push("ds_id = ?");
    binds.push(dsStr);
  }
  binds.push(mc_nickname);

  const result = await env.DB.prepare(
    `UPDATE players SET ${sets.join(", ")} WHERE mc_nickname = ?`
  )
    .bind(...binds)
    .run();
  if (!result.meta.changes) return fail("player_not_found", 404);

  return ok();
}

// GET /admin/info?mc_nickname=X (admin_key via query param or x-admin-key header)
async function handleInfo(request: Request, env: Env): Promise<Response> {
  const url = new URL(request.url);
  const adminKey =
    url.searchParams.get("admin_key") || request.headers.get("x-admin-key");
  if (!adminAuthorized(adminKey, env)) return fail("unauthorized", 401);

  const mcNickname = url.searchParams.get("mc_nickname");
  if (typeof mcNickname !== "string" || !NICKNAME_RE.test(mcNickname)) {
    return fail("invalid_nickname");
  }

  const player = await env.DB.prepare(
    "SELECT mc_nickname, tg_id, ds_id, is_banned, created_at FROM players WHERE mc_nickname = ?"
  )
    .bind(mcNickname)
    .first<PlayerRow>();
  if (!player) return fail("player_not_found", 404);

  const devicesResult = await env.DB.prepare(
    "SELECT mc_nickname, hwid, trusted, last_seen FROM devices WHERE mc_nickname = ?"
  )
    .bind(mcNickname)
    .all<DeviceRow>();

  return ok({ player, devices: devicesResult.results });
}

export default {
  async fetch(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    // CORS preflight.
    if (request.method === "OPTIONS") {
      return new Response(null, { status: 204, headers: CORS_HEADERS });
    }

    const url = new URL(request.url);
    const path = url.pathname;

    const allowedMethod = ROUTES[path];
    if (!allowedMethod) {
      return json({ error: "not_found" }, 404);
    }
    if (request.method !== allowedMethod) {
      return json({ error: "method_not_allowed" }, 405);
    }

    try {
      switch (path) {
        case "/auth/request-otp":
          return await handleRequestOTP(request, env);
        case "/auth/confirm-otp":
          return await handleConfirmOTP(request, env);
        case "/auth/register-device":
          return await handleRegisterDevice(request, env);
        case "/auth/links":
          return await handleLinks(request, env);
        case "/auth/request-otp-discord":
          return await handleRequestOtpDiscord(request, env);
        case "/auth/validate":
          return await handleValidate(request, env);
        case "/admin/ban":
          return await handleBan(request, env);
        case "/admin/unban":
          return await handleUnban(request, env);
        case "/admin/link":
          return await handleLink(request, env);
        case "/admin/info":
          return await handleInfo(request, env);
        default:
          return json({ error: "not_found" }, 404);
      }
    } catch (err) {
      // Log the error class only — never SQL details, tokens or bodies.
      console.error("auth-worker error on", path, ":", String(err));
      return fail("internal_error", 500);
    }
  },
};
