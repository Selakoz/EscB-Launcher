// EscapeBook skins worker.
//
// Serves Minecraft skins stored directly in Cloudflare D1 as BLOBs (no R2,
// no external object storage). Public reads are cacheable; uploads/deletes
// require a bearer token issued by the EscapeBook auth worker.
//
// Routes:
//   GET    /api/skins/:nickname.png    -> PNG bytes (default Steve if absent)
//   GET    /api/skins/:nickname.json   -> CustomSkinLoader profile (see below)
//   GET    /api/skins/:nickname/info   -> JSON metadata
//   POST   /api/skins/upload           -> multipart/form-data, field "file"
//   DELETE /api/skins                  -> removes the caller's skin
//
// Auth contract (implemented by the auth worker):
//   GET  /auth/validate
//        Authorization: Bearer <session_token>
//        X-HWID: <device id>   (empty string when the caller sends no device id)
//   200  { "valid": true, "nickname": "<MC nickname>" }
//        ({ "ok": true, "nickname": "..." } is also accepted)
//   401  anything else
//
// In production the call goes through the AUTH service binding. Fetching
// another Worker over the public *.workers.dev hostname is answered with a 404
// by Cloudflare's same-zone routing, so AUTH_WORKER_URL is only a fallback for
// local development. While neither is configured, uploads/deletes fail closed
// with 503.

interface Env {
  DB: D1Database;
  // Service binding to the auth worker (see wrangler.toml). This is the only
  // reliable way to call another Worker from Cloudflare: same-zone subrequests
  // over the public *.workers.dev hostname are answered with a 404 at the edge.
  AUTH?: Fetcher;
  // Base URL of the auth worker, without a trailing slash. Fallback for local
  // development where the service binding cannot be resolved.
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
// Cold starts on the auth worker plus a D1 round-trip can exceed a few seconds,
// so allow a little slack before treating the call as a network failure.
const AUTH_TIMEOUT_MS = 10_000;
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

// Public skin reads are consumed by Minecraft mods and the launcher, not just
// the website, so they must be readable from any origin. These endpoints never
// accept credentials, which is what makes the wildcard safe here.
function publicCorsHeaders(): Headers {
  const headers = new Headers();
  headers.set("Access-Control-Allow-Origin", "*");
  headers.set("X-Content-Type-Options", "nosniff");
  return headers;
}

function publicJsonResponse(
  data: unknown,
  status: number,
  extraHeaders?: Record<string, string>,
): Response {
  const headers = publicCorsHeaders();
  headers.set("Content-Type", "application/json; charset=utf-8");
  headers.set("Cache-Control", "no-store");
  if (extraHeaders) {
    for (const [key, value] of Object.entries(extraHeaders)) headers.set(key, value);
  }
  return new Response(JSON.stringify(data), { status, headers });
}

function publicErrorResponse(status: number, code: string, message: string): Response {
  return publicJsonResponse({ ok: false, error: code, message }, status);
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
  cacheControl: string,
  contentType = "image/png",
): Response {
  const headers = publicCorsHeaders();
  headers.set("Content-Type", contentType);
  headers.set("Cache-Control", cacheControl);
  return new Response(toArrayBuffer(bytes), { status: 200, headers });
}

// CustomSkinLoader reads skins from a profile endpoint that must answer with a
// JSON document, not an image. A missing/empty object tells the mod "no custom
// skin here", so it falls back to its default behaviour instead of erroring.
function cslJsonResponse(data: unknown): Response {
  const headers = publicCorsHeaders();
  headers.set("Content-Type", "application/json; charset=utf-8");
  headers.set("Cache-Control", "no-cache, must-revalidate");
  return new Response(JSON.stringify(data), { status: 200, headers });
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

interface AuthValidatePayload {
  valid?: unknown;
  ok?: unknown;
  nickname?: unknown;
  error?: unknown;
}

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

// Normalizes the configured auth worker base URL. Variables edited in the
// Dashboard are easy to break (wrapping quotes, missing scheme, trailing
// slashes) and every one of those mistakes makes fetch() throw, which then
// surfaces as a generic 503.
function resolveAuthBaseUrl(env: Env): string | null {
  const raw = env.AUTH_WORKER_URL;
  if (typeof raw !== "string") return null;
  const trimmed = raw.trim().replace(/^["']+|["']+$/g, "").trim();
  if (trimmed.length === 0) return null;
  const withScheme = /^https?:\/\//i.test(trimmed) ? trimmed : `https://${trimmed}`;
  return withScheme.replace(/\/+$/, "");
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

  const authBinding = env.AUTH;
  const authUrl = resolveAuthBaseUrl(env);
  if (!authBinding && !authUrl) {
    console.error(
      "skins-worker: neither the AUTH service binding nor AUTH_WORKER_URL is configured",
    );
    return {
      ok: false,
      response: errorResponse(
        503,
        "auth_unavailable",
        "AUTH_WORKER_URL missing",
        origin,
      ),
    };
  }

  // A service binding only uses the path; the hostname is ignored. When the
  // binding is absent (local dev) the configured URL is used instead.
  const validateUrl = authUrl
    ? `${authUrl}/auth/validate`
    : "https://escapebook-auth/auth/validate";
  const route = authBinding ? "AUTH service binding" : validateUrl;
  const hwid = parseHwid(request);

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), AUTH_TIMEOUT_MS);
  try {
    const init: RequestInit = {
      method: "GET",
      headers: {
        Authorization: `Bearer ${token}`,
        // Always send the header (empty means "no device binding") so the
        // auth worker sees a consistent request shape.
        "X-HWID": hwid ?? "",
        Accept: "application/json",
      },
      signal: controller.signal,
    };

    let response: Response | null = null;
    if (authBinding) {
      try {
        response = await authBinding.fetch(validateUrl, init);
      } catch (error) {
        const reason = error instanceof Error ? `${error.name}: ${error.message}` : String(error);
        console.error("Auth validation failed:", error);
        console.error(
          `skins-worker: AUTH service binding call failed (${reason}); falling back to ${validateUrl}`,
        );
      }
    }
    if (response === null) {
      response = await fetch(validateUrl, init);
    }

    let authData: AuthValidatePayload = {};
    let bodyParsed = false;
    try {
      authData = (await response.json()) as AuthValidatePayload;
      bodyParsed = true;
    } catch {
      console.error(
        `skins-worker: auth worker returned a non-JSON response (HTTP ${response.status}) via ${route}`,
      );
    }

    const upstreamError =
      typeof authData.error === "string" ? authData.error : `http_${response.status}`;

    if (!response.ok) {
      if (response.status === 401 || response.status === 403) {
        console.error(
          `skins-worker: auth rejected the token (HTTP ${response.status}: ${upstreamError})`,
        );
        return {
          ok: false,
          response: errorResponse(401, "invalid_token", "The provided token is not valid.", origin),
        };
      }
      console.error(
        `skins-worker: auth worker returned HTTP ${response.status} (${upstreamError}) via ${route}`,
      );
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

    if (!bodyParsed) {
      return {
        ok: false,
        response: errorResponse(502, "bad_auth_response", "Invalid auth response.", origin),
      };
    }

    const approved = authData.valid === true || authData.ok === true;
    const nickname = typeof authData.nickname === "string" ? authData.nickname : "";
    if (!approved || !NICKNAME_RE.test(nickname)) {
      console.error(`skins-worker: auth rejected the token (${upstreamError})`);
      return {
        ok: false,
        response: errorResponse(401, "invalid_token", "The provided token is not valid.", origin),
      };
    }

    return { ok: true, nickname };
  } catch (error) {
    const reason = error instanceof Error ? `${error.name}: ${error.message}` : String(error);
    console.error("Auth validation failed:", error);
    console.error(`skins-worker: auth request via ${route} failed (${reason})`);
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
): Promise<Response> {
  const nickname = parseNickname(nicknameRaw);
  if (!nickname) {
    return publicErrorResponse(
      400,
      "invalid_nickname",
      "Nickname must be 3-16 characters: letters, digits or underscore.",
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
        "public, max-age=60, stale-while-revalidate=300",
      );
    }

    const bytes = toBytes(row.skin_data);
    if (bytes.byteLength === 0) {
      console.error(`skins-worker: empty skin_data for "${nickname}", serving default`);
      return pngResponse(
        getDefaultSkin(),
        "public, max-age=60, stale-while-revalidate=300",
      );
    }

    // Revalidate on every join so a changed skin is picked up immediately
    // instead of being served from a stale hour-long cache.
    return pngResponse(
      bytes,
      "no-cache, must-revalidate",
      row.content_type || "image/png",
    );
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error);
    console.error(`skins-worker: D1 read failed for "${nickname}": ${reason}`);
    // Never fail the game client: fall back to the default skin.
    return pngResponse(getDefaultSkin(), "public, max-age=30");
  }
}

// Serves the CustomSkinLoader profile for `<nickname>.json`. Only existence
// matters here: the actual pixels are fetched from the canonical `.png` URL.
async function handleGetSkinJson(
  nicknameRaw: string,
  env: Env,
): Promise<Response> {
  const cleanNickname = nicknameRaw.replace(/\.json$/i, "");

  // Mirror the .png behaviour: never fail the game client. An unrecognised
  // nickname is reported as "no skin" rather than as an error.
  const nickname = parseNickname(cleanNickname);
  if (!nickname) {
    console.warn(`skins-worker: invalid nickname in .json request: "${nicknameRaw}"`);
    return cslJsonResponse({});
  }

  try {
    const row = await env.DB.prepare("SELECT 1 AS present FROM skins WHERE nickname = ?")
      .bind(nickname)
      .first<{ present: number }>();

    if (!row) {
      return cslJsonResponse({});
    }

    // CustomSkinLoader cannot resolve relative paths, so hand it the absolute
    // canonical host rather than deriving the URL from the incoming request.
    const pngUrl = `https://escapebook-skins.fireman777sasha.workers.dev/api/skins/${encodeURIComponent(nickname)}.png`;
    return cslJsonResponse({
      skin: {
        url: pngUrl,
        model: "default",
      },
      cape: null,
    });
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error);
    console.error(`skins-worker: D1 json read failed for "${nickname}": ${reason}`);
    return cslJsonResponse({});
  }
}

async function handleGetInfo(
  nicknameRaw: string,
  env: Env,
): Promise<Response> {
  const nickname = parseNickname(nicknameRaw);
  if (!nickname) {
    return publicErrorResponse(
      400,
      "invalid_nickname",
      "Nickname must be 3-16 characters: letters, digits or underscore.",
    );
  }

  try {
    const row = await env.DB.prepare(
      "SELECT size_bytes, uploaded_at, updated_at FROM skins WHERE nickname = ?",
    )
      .bind(nickname)
      .first<SkinInfoRow>();

    if (!row) {
      return publicJsonResponse({
        nickname,
        exists: false,
        size_bytes: null,
        uploaded_at: null,
        updated_at: null,
      }, 200);
    }

    return publicJsonResponse({
      nickname,
      exists: true,
      size_bytes: row.size_bytes,
      uploaded_at: row.uploaded_at,
      updated_at: row.updated_at,
    }, 200);
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error);
    console.error(`skins-worker: D1 info query failed for "${nickname}": ${reason}`);
    return publicErrorResponse(500, "storage_error", "Could not read skin metadata.");
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

      // Authenticated: upload. Checked before the public skin branch below so
      // that "upload" is never mistaken for a nickname.
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

      // Public skin endpoints. Parse the pathname by hand instead of relying on
      // router parameter matching: CustomSkinLoader requests `<nickname>.json`
      // and the extension must be stripped before the D1 lookup, while the
      // plain `.png` form keeps serving the image bytes.
      if (path.startsWith("/api/skins/")) {
        if (request.method !== "GET") {
          return publicErrorResponse(405, "method_not_allowed", "Use GET for this endpoint.");
        }

        const fullPath = path.slice("/api/skins/".length);

        // Metadata: /api/skins/<nickname>/info
        if (/\/info$/i.test(fullPath)) {
          return await handleGetInfo(fullPath.slice(0, -"/info".length), env);
        }

        const isJsonRequest = /\.json$/i.test(fullPath);
        const cleanNickname = fullPath.replace(/\.(json|png)$/i, "");
        if (!cleanNickname) {
          return publicErrorResponse(
            400,
            "missing_nickname",
            "A nickname is required after /api/skins/.",
          );
        }

        if (isJsonRequest) {
          // CustomSkinLoader profile (always JSON, even for unknown players).
          return await handleGetSkinJson(cleanNickname, env);
        }

        // PNG bytes (or the default Steve skin when nothing is stored).
        return await handleGetSkin(cleanNickname, env);
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
