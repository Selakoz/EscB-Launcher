// EscapeBook skins worker.
//
// Serves Minecraft skins stored directly in Cloudflare D1 as BLOBs (no R2,
// no external object storage). Public reads are cacheable; uploads/deletes
// require a bearer token issued by the EscapeBook auth worker.
//
// Routes:
//   GET    /api/skins/:nickname.png    -> PNG bytes (default Steve if absent)
//   GET    /api/skins/:nickname/info   -> JSON metadata
//   POST   /api/skins/upload           -> multipart/form-data, field "file"
//   DELETE /api/skins                  -> removes the caller's skin
//
// Auth contract (implemented by the auth worker):
//   GET  ${AUTH_WORKER_URL}/auth/validate
//        Authorization: Bearer <session_token>
//        X-HWID: <device id>   (optional; forwarded when the caller sends it)
//   200  { "valid": true, "nickname": "<MC nickname>" }
//        ({ "ok": true, "nickname": "..." } is also accepted)
//   401  anything else
// While AUTH_WORKER_URL is unset, uploads and deletes fail closed with 503.

interface Env {
  DB: D1Database;
  // Base URL of the auth worker, without a trailing slash.
  AUTH_WORKER_URL?: string;
}

interface SkinRow {
  skin_data: ArrayBuffer | number[];
  content_type: string;
  size_bytes: number;
  uploaded_at: number;
  updated_at: number;
}

interface SkinInfoRow {
  size_bytes: number;
  uploaded_at: number;
  updated_at: number;
}

const ALLOWED_ORIGIN = "https://escapebook.site";
const MAX_SKIN_BYTES = 1_048_576; // 1 MiB
// Multipart framing adds a small amount on top of the file itself.
const MAX_REQUEST_BYTES = MAX_SKIN_BYTES + 65_536;
const NICKNAME_RE = /^[A-Za-z0-9_]{3,16}$/;
const PNG_SIGNATURE = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

// Minimal 64x64 Steve skin (classic palette with an EscapeBook gold stripe).
// Generated as a real PNG, so it can be served byte-for-byte.
const DEFAULT_SKIN_BASE64 =
  "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAA+klEQVR42u3a0QnCMBSF4WzhAr64gt0gI1hcwyF8sFMIDuCjI3QYN4jEB6GKScNNbK75DxyooKX5iKVtakwk3XrlQr2d+mCN9gAAAAAAAADAHwPEBli6AGgA+Da9mwCI/cfVA8QG6Ouz3V9en/22z5zfVn+STDnY88E+m2Pg6gCOu27S5gBKtb7rAju4SRNPWnYYnaSp+88OCgAAjQF8DDixsQN6rxQoN2h2gNoKAAA/BrhfN8FKv88MAACAtgCK32xJn+gAAIByAEIIIYQQQgghhBBpci+u6nvxEgAA2gJYel0BAAAAAAAAAADQC6B+8VO6ugwAAMsCPACblbixQVbFYAAAAABJRU5ErkJggg==";

let defaultSkinBytes: Uint8Array | null = null;

function getDefaultSkin(): Uint8Array {
  if (defaultSkinBytes) return defaultSkinBytes;
  const binary = atob(DEFAULT_SKIN_BASE64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  defaultSkinBytes = bytes;
  return bytes;
}

function corsHeaders(origin: string | null): Headers {
  const headers = new Headers();
  headers.set("Vary", "Origin");
  // Workers/launchers usually send no Origin header; still advertise the
  // allowed origin so the response is cacheable and consistent.
  if (!origin || origin === ALLOWED_ORIGIN) {
    headers.set("Access-Control-Allow-Origin", ALLOWED_ORIGIN);
  }
  return headers;
}

function jsonResponse(
  data: unknown,
  status: number,
  origin: string | null,
  extraHeaders?: Record<string, string>,
): Response {
  const headers = corsHeaders(origin);
  headers.set("Content-Type", "application/json; charset=utf-8");
  headers.set("X-Content-Type-Options", "nosniff");
  if (extraHeaders) {
    for (const [key, value] of Object.entries(extraHeaders)) headers.set(key, value);
  }
  return new Response(JSON.stringify(data), { status, headers });
}

function errorResponse(
  status: number,
  code: string,
  message: string,
  origin: string | null,
): Response {
  return jsonResponse({ ok: false, error: code, message }, status, origin);
}

function toArrayBuffer(bytes: Uint8Array): ArrayBuffer {
  return new Uint8Array(bytes).buffer;
}

function pngResponse(
  bytes: Uint8Array,
  origin: string | null,
  cacheControl: string,
  contentType = "image/png",
): Response {
  const headers = corsHeaders(origin);
  headers.set("Content-Type", contentType);
  headers.set("Cache-Control", cacheControl);
  headers.set("X-Content-Type-Options", "nosniff");
  return new Response(toArrayBuffer(bytes), { status: 200, headers });
}

// D1 returns BLOBs as ArrayBuffer; older runtimes may hand back a byte array.
function toBytes(value: unknown): Uint8Array {
  if (value instanceof ArrayBuffer) return new Uint8Array(value);
  if (ArrayBuffer.isView(value)) {
    return new Uint8Array(value.buffer, value.byteOffset, value.byteLength);
  }
  if (Array.isArray(value)) return new Uint8Array(value);
  return new Uint8Array(0);
}

function readUint32BE(bytes: Uint8Array, offset: number): number {
  return (
    ((bytes[offset] << 24) |
      (bytes[offset + 1] << 16) |
      (bytes[offset + 2] << 8) |
      bytes[offset + 3]) >>>
    0
  );
}

type PngCheck =
  | { ok: true; width: number; height: number }
  | { ok: false; reason: string };

// Validates the 8-byte PNG signature, the IHDR chunk type and the dimensions
// stored at bytes 16-23 (width then height, both big-endian uint32).
function inspectPng(bytes: Uint8Array): PngCheck {
  if (bytes.byteLength < 24) {
    return { ok: false, reason: "File is too small to be a valid PNG." };
  }
  for (let i = 0; i < PNG_SIGNATURE.length; i++) {
    if (bytes[i] !== PNG_SIGNATURE[i]) {
      return { ok: false, reason: "File is not a valid PNG image." };
    }
  }
  // Bytes 12-15 hold the first chunk type, which must be "IHDR".
  const isIhdr =
    bytes[12] === 0x49 &&
    bytes[13] === 0x48 &&
    bytes[14] === 0x44 &&
    bytes[15] === 0x52;
  if (!isIhdr) {
    return { ok: false, reason: "PNG is missing the IHDR chunk." };
  }
  const width = readUint32BE(bytes, 16);
  const height = readUint32BE(bytes, 20);
  const validSize = width === 64 && (height === 64 || height === 32);
  if (!validSize) {
    return {
      ok: false,
      reason: `Unsupported skin size ${width}x${height}. Expected 64x64 or 64x32.`,
    };
  }
  return { ok: true, width, height };
}

type AuthResult =
  | { ok: true; nickname: string }
  | { ok: false; response: Response };

function parseBearerToken(request: Request): string | null {
  const header = request.headers.get("Authorization");
  if (!header) return null;
  const match = /^Bearer\s+(\S+)$/i.exec(header.trim());
  return match ? match[1] : null;
}

// Device-bound uploads (launcher) send X-HWID; web uploads do not. Forward the
// header untouched so the auth worker can enforce the HWID binding.
function parseHwid(request: Request): string | null {
  const header = request.headers.get("X-HWID");
  if (!header) return null;
  const trimmed = header.trim();
  return trimmed.length > 0 ? trimmed : null;
}

// Validates the bearer token against the auth worker. Fails closed.
async function authenticate(
  request: Request,
  env: Env,
  origin: string | null,
): Promise<AuthResult> {
  const token = parseBearerToken(request);
  if (!token) {
    return {
      ok: false,
      response: errorResponse(
        401,
        "missing_token",
        "Authorization: Bearer <token> header is required.",
        origin,
      ),
    };
  }

  const baseUrl = (env.AUTH_WORKER_URL ?? "").trim().replace(/\/+$/, "");
  if (!baseUrl) {
    return {
      ok: false,
      response: errorResponse(
        503,
        "auth_not_configured",
        "AUTH_WORKER_URL is not configured on the skins worker.",
        origin,
      ),
    };
  }

  const hwid = parseHwid(request);
  const headers: Record<string, string> = {
    Authorization: `Bearer ${token}`,
    Accept: "application/json",
  };
  if (hwid !== null) headers["X-HWID"] = hwid;

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), 5000);
  try {
    const response = await fetch(`${baseUrl}/auth/validate`, {
      method: "GET",
      headers,
      signal: controller.signal,
    });

    if (response.status === 401 || response.status === 403) {
      return {
        ok: false,
        response: errorResponse(401, "invalid_token", "The provided token is not valid.", origin),
      };
    }
    if (!response.ok) {
      console.error(`skins-worker: auth worker returned HTTP ${response.status}`);
      return {
        ok: false,
        response: errorResponse(
          503,
          "auth_unavailable",
          "The authentication service is unavailable. Try again later.",
          origin,
        ),
      };
    }

    let payload: unknown;
    try {
      payload = await response.json();
    } catch {
      console.error("skins-worker: auth worker returned a non-JSON response");
      return {
        ok: false,
        response: errorResponse(502, "bad_auth_response", "Invalid auth response.", origin),
      };
    }

    const body = payload as { valid?: unknown; ok?: unknown; nickname?: unknown } | null;
    const approved = body !== null && (body.valid === true || body.ok === true);
    const nickname = body !== null && typeof body.nickname === "string" ? body.nickname : "";
    if (!approved || !NICKNAME_RE.test(nickname)) {
      return {
        ok: false,
        response: errorResponse(401, "invalid_token", "The provided token is not valid.", origin),
      };
    }

    return { ok: true, nickname };
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error);
    console.error(`skins-worker: auth request failed: ${reason}`);
    return {
      ok: false,
      response: errorResponse(
        503,
        "auth_unavailable",
        "The authentication service is unavailable. Try again later.",
        origin,
      ),
    };
  } finally {
    clearTimeout(timer);
  }
}

function parseNickname(raw: string | null): string | null {
  if (!raw) return null;
  let decoded: string;
  try {
    decoded = decodeURIComponent(raw);
  } catch {
    return null;
  }
  return NICKNAME_RE.test(decoded) ? decoded : null;
}

async function handleGetSkin(
  nicknameRaw: string,
  env: Env,
  origin: string | null,
): Promise<Response> {
  const nickname = parseNickname(nicknameRaw);
  if (!nickname) {
    return errorResponse(
      400,
      "invalid_nickname",
      "Nickname must be 3-16 characters: letters, digits or underscore.",
      origin,
    );
  }

  try {
    const row = await env.DB.prepare(
      "SELECT skin_data, content_type FROM skins WHERE nickname = ?",
    )
      .bind(nickname)
      .first<SkinRow>();

    if (!row) {
      return pngResponse(
        getDefaultSkin(),
        origin,
        "public, max-age=60, stale-while-revalidate=300",
      );
    }

    const bytes = toBytes(row.skin_data);
    if (bytes.byteLength === 0) {
      console.error(`skins-worker: empty skin_data for "${nickname}", serving default`);
      return pngResponse(
        getDefaultSkin(),
        origin,
        "public, max-age=60, stale-while-revalidate=300",
      );
    }

    return pngResponse(
      bytes,
      origin,
      "public, max-age=3600, stale-while-revalidate=86400",
      row.content_type || "image/png",
    );
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error);
    console.error(`skins-worker: D1 read failed for "${nickname}": ${reason}`);
    // Never fail the game client: fall back to the default skin.
    return pngResponse(
      getDefaultSkin(),
      origin,
      "public, max-age=30",
    );
  }
}

async function handleGetInfo(
  nicknameRaw: string,
  env: Env,
  origin: string | null,
): Promise<Response> {
  const nickname = parseNickname(nicknameRaw);
  if (!nickname) {
    return errorResponse(
      400,
      "invalid_nickname",
      "Nickname must be 3-16 characters: letters, digits or underscore.",
      origin,
    );
  }

  try {
    const row = await env.DB.prepare(
      "SELECT size_bytes, uploaded_at, updated_at FROM skins WHERE nickname = ?",
    )
      .bind(nickname)
      .first<SkinInfoRow>();

    if (!row) {
      return jsonResponse(
        {
          nickname,
          exists: false,
          size_bytes: null,
          uploaded_at: null,
          updated_at: null,
        },
        200,
        origin,
        { "Cache-Control": "no-store" },
      );
    }

    return jsonResponse(
      {
        nickname,
        exists: true,
        size_bytes: row.size_bytes,
        uploaded_at: row.uploaded_at,
        updated_at: row.updated_at,
      },
      200,
      origin,
      { "Cache-Control": "no-store" },
    );
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error);
    console.error(`skins-worker: D1 info query failed for "${nickname}": ${reason}`);
    return errorResponse(500, "storage_error", "Could not read skin metadata.", origin);
  }
}

async function handleUpload(
  request: Request,
  env: Env,
  origin: string | null,
): Promise<Response> {
  const auth = await authenticate(request, env, origin);
  if (!auth.ok) return auth.response;
  const nickname = auth.nickname;

  const declaredLength = Number(request.headers.get("Content-Length") ?? "0");
  if (Number.isFinite(declaredLength) && declaredLength > MAX_REQUEST_BYTES) {
    return errorResponse(
      413,
      "payload_too_large",
      "Skin file is too large. Maximum size is 1 MB.",
      origin,
    );
  }

  let form: FormData;
  try {
    form = await request.formData();
  } catch {
    return errorResponse(
      400,
      "invalid_multipart",
      "Request body must be multipart/form-data with a 'file' field.",
      origin,
    );
  }

  // workers-types types FormData.get() as string | null even though the
  // runtime returns a File for file parts, so widen it explicitly.
  const entry = form.get("file") as unknown as File | string | null;
  if (entry === null || typeof entry === "string") {
    return errorResponse(
      400,
      "missing_file",
      "Form field 'file' with a PNG image is required.",
      origin,
    );
  }

  if (entry.size > MAX_SKIN_BYTES) {
    return errorResponse(
      413,
      "payload_too_large",
      "Skin file is too large. Maximum size is 1 MB.",
      origin,
    );
  }

  if (entry.type && entry.type !== "image/png") {
    return errorResponse(400, "invalid_type", "Only image/png files are accepted.", origin);
  }

  let data: ArrayBuffer;
  try {
    data = await entry.arrayBuffer();
  } catch {
    return errorResponse(400, "unreadable_file", "Could not read the uploaded file.", origin);
  }

  if (data.byteLength === 0) {
    return errorResponse(400, "empty_file", "The uploaded file is empty.", origin);
  }
  if (data.byteLength > MAX_SKIN_BYTES) {
    return errorResponse(
      413,
      "payload_too_large",
      "Skin file is too large. Maximum size is 1 MB.",
      origin,
    );
  }

  const check = inspectPng(new Uint8Array(data));
  if (!check.ok) {
    return errorResponse(400, "invalid_png", check.reason, origin);
  }

  const now = Math.floor(Date.now() / 1000);
  try {
    await env.DB.prepare(
      "INSERT OR REPLACE INTO skins (nickname, skin_data, content_type, size_bytes, uploaded_at, updated_at) " +
        "VALUES (?, ?, 'image/png', ?, COALESCE((SELECT uploaded_at FROM skins WHERE nickname = ?), ?), ?)",
    )
      .bind(nickname, data, data.byteLength, nickname, now, now)
      .run();
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error);
    console.error(`skins-worker: D1 upsert failed for "${nickname}": ${reason}`);
    return errorResponse(500, "storage_error", "Could not save the skin. Try again later.", origin);
  }

  console.log(
    `skins-worker: skin saved for "${nickname}" (${data.byteLength} bytes, ${check.width}x${check.height})`,
  );

  return jsonResponse(
    {
      ok: true,
      nickname,
      url: `/api/skins/${encodeURIComponent(nickname)}.png`,
    },
    200,
    origin,
    { "Cache-Control": "no-store" },
  );
}

async function handleDelete(
  request: Request,
  env: Env,
  origin: string | null,
  url: URL,
): Promise<Response> {
  const auth = await authenticate(request, env, origin);
  if (!auth.ok) return auth.response;

  // The auth context is authoritative; ?nickname= is only a fallback for
  // auth implementations that do not echo the nickname back.
  const queryNickname = parseNickname(url.searchParams.get("nickname"));
  const nickname = NICKNAME_RE.test(auth.nickname) ? auth.nickname : queryNickname;

  if (!nickname) {
    return errorResponse(
      400,
      "invalid_nickname",
      "Nickname must be 3-16 characters: letters, digits or underscore.",
      origin,
    );
  }

  try {
    await env.DB.prepare("DELETE FROM skins WHERE nickname = ?").bind(nickname).run();
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error);
    console.error(`skins-worker: D1 delete failed for "${nickname}": ${reason}`);
    return errorResponse(500, "storage_error", "Could not delete the skin. Try again later.", origin);
  }

  console.log(`skins-worker: skin deleted for "${nickname}"`);
  return jsonResponse({ ok: true }, 200, origin, { "Cache-Control": "no-store" });
}

function handleOptions(request: Request): Response {
  const origin = request.headers.get("Origin");
  if (origin && origin !== ALLOWED_ORIGIN) {
    return errorResponse(403, "origin_not_allowed", "Origin is not allowed.", origin);
  }
  const headers = corsHeaders(origin);
  headers.set("Access-Control-Allow-Methods", "GET, POST, DELETE, OPTIONS");
  headers.set("Access-Control-Allow-Headers", "Authorization, Content-Type, X-HWID");
  headers.set("Access-Control-Max-Age", "86400");
  return new Response(null, { status: 204, headers });
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const origin = request.headers.get("Origin");

    try {
      if (request.method === "OPTIONS") {
        return handleOptions(request);
      }

      const url = new URL(request.url);
      const path = url.pathname;

      // Public: skin bytes.
      const skinMatch = /^\/api\/skins\/([^/]+)\.png$/.exec(path);
      if (skinMatch) {
        if (request.method !== "GET") {
          return errorResponse(405, "method_not_allowed", "Use GET for this endpoint.", origin);
        }
        return await handleGetSkin(skinMatch[1], env, origin);
      }

      // Public: metadata.
      const infoMatch = /^\/api\/skins\/([^/]+)\/info$/.exec(path);
      if (infoMatch) {
        if (request.method !== "GET") {
          return errorResponse(405, "method_not_allowed", "Use GET for this endpoint.", origin);
        }
        return await handleGetInfo(infoMatch[1], env, origin);
      }

      // Authenticated: upload.
      if (path === "/api/skins/upload") {
        if (request.method !== "POST") {
          return errorResponse(405, "method_not_allowed", "Use POST for this endpoint.", origin);
        }
        return await handleUpload(request, env, origin);
      }

      // Authenticated: delete.
      if (path === "/api/skins") {
        if (request.method !== "DELETE") {
          return errorResponse(405, "method_not_allowed", "Use DELETE for this endpoint.", origin);
        }
        return await handleDelete(request, env, origin, url);
      }

      return errorResponse(404, "not_found", "Endpoint not found.", origin);
    } catch (error) {
      // Last-resort guard: the worker must never crash a client request.
      const reason = error instanceof Error ? error.message : String(error);
      console.error(`skins-worker: unhandled error: ${reason}`);
      return errorResponse(500, "internal_error", "Unexpected server error.", origin);
    }
  },
};
