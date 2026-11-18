// EscapeBook discord-interactions-worker: Discord Interactions Endpoint.
// Verifies Ed25519 request signatures (tweetnacl) and routes slash commands
// to D1-backed handlers. All queries are parameterized; tokens and raw
// interaction payloads are never logged.

import nacl from "tweetnacl";

interface Env {
  DB: D1Database;
  DS_PUBLIC_KEY: string;
  DS_APP_ID: string;
  DS_BOT_TOKEN: string;
  ADMIN_ROLE_ID: string;
}

interface InteractionOption {
  name: string;
  type: number;
  value?: string | number | boolean;
}

interface InteractionMember {
  roles?: string[];
}

interface Interaction {
  type: number;
  data?: { name?: string; options?: InteractionOption[] };
  member?: InteractionMember;
}

interface PlayerRow {
  mc_nickname: string;
  // Snowflake IDs are stored as TEXT (precision-safe), never as JS numbers.
  tg_id: string | null;
  ds_id: string | null;
  is_banned: number;
  created_at: number;
}

interface DeviceRow {
  hwid: string;
  trusted: number;
  last_seen: number;
}

const INTERACTIONS_PATH = "/interactions";
const NICKNAME_RE = /^[A-Za-z0-9_]{3,16}$/;
// Numeric strings for Snowflake IDs (5-25 digits): validated as strings,
// never converted to Number (precision loss above 2^53).
const NUMERIC_ID_RE = /^\d{5,25}$/;

// Discord interaction response types.
const RESP_PONG = 1;
const RESP_CHANNEL_MESSAGE_WITH_SOURCE = 4;

const EMBED_COLOR_OK = 0x2e7d32; // green
const EMBED_COLOR_BAD = 0xc62828; // red

function jsonResponse(data: unknown, status = 200): Response {
  return new Response(JSON.stringify(data), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function message(content: string): Response {
  return jsonResponse({ type: RESP_CHANNEL_MESSAGE_WITH_SOURCE, data: { content } });
}

function hexToUint8Array(hex: string): Uint8Array {
  const bytes = new Uint8Array(hex.length / 2);
  for (let i = 0; i < bytes.length; i++) {
    bytes[i] = parseInt(hex.substring(i * 2, i * 2 + 2), 16);
  }
  return bytes;
}

// Verify the Ed25519 detached signature Discord sends with every interaction.
// Discord signs `X-Signature-Timestamp + rawBody` with the application key.
function verifySignature(request: Request, rawBody: string, env: Env): boolean {
  const signature = request.headers.get("X-Signature-Ed25519");
  const timestamp = request.headers.get("X-Signature-Timestamp");

  if (
    !signature ||
    !timestamp ||
    typeof env.DS_PUBLIC_KEY !== "string" ||
    env.DS_PUBLIC_KEY.length === 0
  ) {
    return false;
  }
  try {
    const messageBytes = new TextEncoder().encode(timestamp + rawBody);
    return nacl.sign.detached.verify(
      messageBytes,
      hexToUint8Array(signature),
      hexToUint8Array(env.DS_PUBLIC_KEY)
    );
  } catch {
    // Malformed hex in signature or public key.
    return false;
  }
}

// Admin check: env.ADMIN_ROLE_ID must be present in interaction.member.roles.
function isAdmin(interaction: Interaction, env: Env): boolean {
  const roles = interaction.member?.roles;
  return (
    Array.isArray(roles) &&
    typeof env.ADMIN_ROLE_ID === "string" &&
    env.ADMIN_ROLE_ID.length > 0 &&
    roles.includes(env.ADMIN_ROLE_ID)
  );
}

function getOption(options: InteractionOption[] | undefined, name: string): InteractionOption | undefined {
  return options?.find((o) => o.name === name);
}

function getNickname(interaction: Interaction): string | null {
  const value = getOption(interaction.data?.options, "mc_nickname")?.value;
  if (typeof value !== "string" || !NICKNAME_RE.test(value)) return null;
  return value;
}

// /add <mc_nickname> — admin only. Inserts a new player row.
async function handleAdd(interaction: Interaction, env: Env): Promise<Response> {
  if (!isAdmin(interaction, env)) return message("Permission denied");

  const nickname = getNickname(interaction);
  if (!nickname) return message("Invalid mc_nickname (3-16 chars: A-Z, a-z, 0-9, _).");

  const existing = await env.DB.prepare(
    "SELECT mc_nickname FROM players WHERE mc_nickname = ?"
  )
    .bind(nickname)
    .first();
  if (existing) return message("Player already exists.");

  await env.DB.prepare("INSERT INTO players (mc_nickname) VALUES (?)")
    .bind(nickname)
    .run();
  return message("Player added.");
}

// /link <mc_nickname> [tg_id] [ds_id] — admin only. Links external accounts.
async function handleLink(interaction: Interaction, env: Env): Promise<Response> {
  if (!isAdmin(interaction, env)) return message("Permission denied");

  const nickname = getNickname(interaction);
  if (!nickname) return message("Invalid mc_nickname (3-16 chars: A-Z, a-z, 0-9, _).");

  const tgOption = getOption(interaction.data?.options, "tg_id")?.value;
  const dsOption = getOption(interaction.data?.options, "ds_id")?.value;

  const hasTg = tgOption !== undefined && tgOption !== null && String(tgOption).length > 0;
  const hasDs = dsOption !== undefined && dsOption !== null && String(dsOption).length > 0;
  if (!hasTg && !hasDs) return message("Provide at least one of: tg_id, ds_id.");

  // Discord/Telegram IDs are Snowflakes (> 2^53): keep them as numeric
  // strings end-to-end, never convert to Number (precision loss).
  const tgId = hasTg ? String(tgOption) : null;
  const dsId = hasDs ? String(dsOption) : null;
  if (
    (tgId !== null && !NUMERIC_ID_RE.test(tgId)) ||
    (dsId !== null && !NUMERIC_ID_RE.test(dsId))
  ) {
    return message("ID должен содержать только цифры (5–25 символов).");
  }

  const player = await env.DB.prepare(
    "SELECT mc_nickname FROM players WHERE mc_nickname = ?"
  )
    .bind(nickname)
    .first();
  if (!player) return message("Player not found.");

  // Dynamic but fully parameterized UPDATE.
  const sets: string[] = [];
  const binds: unknown[] = [];
  if (hasTg) {
    sets.push("tg_id = ?");
    binds.push(tgId);
  }
  if (hasDs) {
    sets.push("ds_id = ?");
    binds.push(dsId);
  }
  binds.push(nickname);

  await env.DB.prepare(`UPDATE players SET ${sets.join(", ")} WHERE mc_nickname = ?`)
    .bind(...binds)
    .run();
  return message("Accounts linked.");
}

// /ban <mc_nickname> — admin only. Sets is_banned = 1.
async function handleBan(interaction: Interaction, env: Env): Promise<Response> {
  if (!isAdmin(interaction, env)) return message("Permission denied");

  const nickname = getNickname(interaction);
  if (!nickname) return message("Invalid mc_nickname (3-16 chars: A-Z, a-z, 0-9, _).");

  const result = await env.DB.prepare(
    "UPDATE players SET is_banned = 1 WHERE mc_nickname = ?"
  )
    .bind(nickname)
    .run();
  if (!result.meta.changes) return message("Player not found.");
  return message("Player banned.");
}

// /unban <mc_nickname> — admin only. Sets is_banned = 0.
async function handleUnban(interaction: Interaction, env: Env): Promise<Response> {
  if (!isAdmin(interaction, env)) return message("Permission denied");

  const nickname = getNickname(interaction);
  if (!nickname) return message("Invalid mc_nickname (3-16 chars: A-Z, a-z, 0-9, _).");

  const result = await env.DB.prepare(
    "UPDATE players SET is_banned = 0 WHERE mc_nickname = ?"
  )
    .bind(nickname)
    .run();
  if (!result.meta.changes) return message("Player not found.");
  return message("Player unbanned.");
}

// /info <mc_nickname> — anyone. Returns an embed with player + devices.
async function handleInfo(interaction: Interaction, env: Env): Promise<Response> {
  const nickname = getNickname(interaction);
  if (!nickname) return message("Invalid mc_nickname (3-16 chars: A-Z, a-z, 0-9, _).");

  const player = await env.DB.prepare(
    "SELECT mc_nickname, tg_id, ds_id, is_banned, created_at FROM players WHERE mc_nickname = ?"
  )
    .bind(nickname)
    .first<PlayerRow>();
  if (!player) return message("Player not found.");

  const devicesResult = await env.DB.prepare(
    "SELECT hwid, trusted, last_seen FROM devices WHERE mc_nickname = ?"
  )
    .bind(nickname)
    .all<DeviceRow>();
  const devices = devicesResult.results;

  // Trim HWIDs for display; embed field values are limited to 1024 chars.
  const deviceLines = devices
    .map((d) => `\`${d.hwid.substring(0, 16)}…\` ${d.trusted ? "trusted" : "untrusted"}`)
    .join("\n");
  const devicesValue =
    devices.length === 0
      ? "No devices registered"
      : deviceLines.substring(0, 1000) || "No devices registered";

  const banned = Number(player.is_banned) === 1;
  return jsonResponse({
    type: RESP_CHANNEL_MESSAGE_WITH_SOURCE,
    data: {
      embeds: [
        {
          title: `Player: ${player.mc_nickname}`,
          color: banned ? EMBED_COLOR_BAD : EMBED_COLOR_OK,
          fields: [
            { name: "Minecraft", value: player.mc_nickname, inline: true },
            { name: "Telegram ID", value: player.tg_id !== null ? String(player.tg_id) : "—", inline: true },
            { name: "Discord ID", value: player.ds_id !== null ? String(player.ds_id) : "—", inline: true },
            { name: "Banned", value: banned ? "Yes" : "No", inline: true },
            {
              name: "Registered",
              value: new Date(Number(player.created_at) * 1000).toISOString(),
              inline: true,
            },
            { name: `Devices (${devices.length})`, value: devicesValue, inline: false },
          ],
        },
      ],
    },
  });
}

// Route an APPLICATION_COMMAND interaction to its handler.
async function handleCommand(interaction: Interaction, env: Env): Promise<Response> {
  const name = interaction.data?.name;
  try {
    switch (name) {
      case "add":
        return await handleAdd(interaction, env);
      case "link":
        return await handleLink(interaction, env);
      case "ban":
        return await handleBan(interaction, env);
      case "unban":
        return await handleUnban(interaction, env);
      case "info":
        return await handleInfo(interaction, env);
      default:
        return message("Unknown command");
    }
  } catch (err) {
    // Never expose SQL details or the raw payload — log error class only.
    console.error("discord-worker db error on command", String(name), ":", String(err));
    return message("Internal error");
  }
}

export default {
  async fetch(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    const url = new URL(request.url);

    if (url.pathname !== INTERACTIONS_PATH) {
      return jsonResponse({ error: "not_found" }, 404);
    }
    if (request.method !== "POST") {
      return jsonResponse({ error: "method_not_allowed" }, 405);
    }

    // Read the raw body once: it is used for signature verification and JSON parsing.
    const rawBody = await request.text();
    if (!verifySignature(request, rawBody, env)) {
      return jsonResponse({ error: "invalid_signature" }, 401);
    }

    let interaction: Interaction;
    try {
      interaction = JSON.parse(rawBody) as Interaction;
    } catch {
      return jsonResponse({ error: "invalid_json" }, 400);
    }

    // PING handshake.
    if (interaction.type === 1) {
      return jsonResponse({ type: RESP_PONG });
    }

    // APPLICATION_COMMAND.
    if (interaction.type === 2) {
      return handleCommand(interaction, env);
    }

    return message("Unsupported interaction type");
  },
};
