# escapebook-skins

Cloudflare Worker that stores player Minecraft skins **directly in D1** as BLOBs.
No R2, no external object storage, no credit card.

- Public: `GET /api/skins/:nickname.png`, `GET /api/skins/:nickname.json`,
  `GET /api/skins/:nickname/info`
- Authenticated: `POST /api/skins/upload`, `DELETE /api/skins`
- CORS: `*` for public GETs; upload/delete restricted to `https://escapebook.site`
- Limits: PNG only, max 1 MB, 64x64 or 64x32

## One-time D1 setup

Run this in the Cloudflare Dashboard → **D1** → `escapebook-db` → **Console**
(or with `wrangler d1 execute escapebook-db --remote --file=migrations/0001_create_skins.sql`):

```sql
CREATE TABLE IF NOT EXISTS skins (
  nickname TEXT PRIMARY KEY,
  skin_data BLOB NOT NULL,
  content_type TEXT NOT NULL DEFAULT 'image/png',
  size_bytes INTEGER NOT NULL,
  uploaded_at INTEGER NOT NULL,
  updated_at INTEGER NOT NULL
);
```

`uploaded_at` / `updated_at` are Unix timestamps in **seconds**.

## Authentication

`POST /api/skins/upload` and `DELETE /api/skins` require
`Authorization: Bearer <session_token>`.

The worker validates the token by calling:

```
GET /auth/validate
Authorization: Bearer <session_token>
X-HWID: <device id>          (empty when the caller has no device id)
```

Expected `200` body:

```json
{ "valid": true, "nickname": "Steve" }
```

`{ "ok": true, "nickname": "Steve" }` is accepted too. Any `401`/`403` from the
auth worker rejects the request.

Device-bound uploads (the launcher) may send `X-HWID`; the worker forwards it
verbatim so the auth worker can reject a token used on another device
(`401 hwid_mismatch`). Web uploads without `X-HWID` are validated by token
alone.

`/auth/validate` is implemented by `infra/auth-worker`. In production the call
goes through the `AUTH` **service binding** declared in `wrangler.toml`
(`service = "escapebook-auth"`). Calling the auth worker over its public
`*.workers.dev` hostname does not work: both Workers share the same
`*.workers.dev` zone, and Cloudflare answers that same-zone subrequest with a
404 before it reaches the auth worker.

`AUTH_WORKER_URL` is only a fallback for local development (for example when
`wrangler dev` has no local auth worker to bind to). When neither is available,
uploads/deletes fail closed with `503 auth_unavailable` (`AUTH_WORKER_URL
missing`).

## API

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| `GET` | `/api/skins/:nickname.png` | no | Skin PNG, or the default Steve skin |
| `GET` | `/api/skins/:nickname.json` | no | CustomSkinLoader profile (see below) |
| `GET` | `/api/skins/:nickname/info` | no | `{ nickname, exists, size_bytes, uploaded_at, updated_at }` |
| `POST` | `/api/skins/upload` | yes | `multipart/form-data`, field `file` |
| `DELETE` | `/api/skins` | yes | Deletes the caller's skin (`?nickname=` fallback) |

Upload success:

```json
{ "ok": true, "nickname": "Steve", "url": "/api/skins/Steve.png" }
```

Errors are JSON: `{ "ok": false, "error": "code", "message": "..." }`.

Status codes: `400` invalid PNG/dimensions/nickname, `401` missing/invalid
token, `413` file over 1 MB, `503` auth unavailable (missing config or the auth
worker is down).

## Local development

```bash
npm install
npx wrangler d1 execute escapebook-db --local --file=migrations/0001_create_skins.sql
npx wrangler dev
npm run typecheck
```

## CustomSkinLoader

CustomSkinLoader requests `<username>.json` from a skin source and expects a
profile document. Point the mod's source at:

```
https://<worker-host>/api/skins
```

`GET /api/skins/Steve.json` returns `200 application/json`:

```json
{
  "skin": {
    "url": "https://escapebook-skins.fireman777sasha.workers.dev/api/skins/Steve.png",
    "model": "default"
  },
  "cape": null
}
```

If the player has no stored skin the endpoint returns `{}` with `200`, which
CSL treats as "no custom skin" and falls back to its default behaviour. The
`.png` endpoint still serves the image directly (and the default Steve PNG when
no skin is stored), so browser checks and direct image requests keep working.
