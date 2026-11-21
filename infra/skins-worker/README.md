# escapebook-skins

Cloudflare Worker that stores player Minecraft skins **directly in D1** as BLOBs.
No R2, no external object storage, no credit card.

- Public: `GET /api/skins/:nickname.png`, `GET /api/skins/:nickname/info`
- Authenticated: `POST /api/skins/upload`, `DELETE /api/skins`
- CORS: `https://escapebook.site` only
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
GET ${AUTH_WORKER_URL}/auth/validate
Authorization: Bearer <session_token>
```

Expected `200` body:

```json
{ "valid": true, "nickname": "Steve" }
```

`{ "ok": true, "nickname": "Steve" }` is accepted too. Any `401`/`403` from the
auth worker rejects the request.

> **Placeholder:** `infra/auth-worker` does not expose `/auth/validate` yet
> (it currently only issues `session_token` from `/auth/confirm-otp`). Until that
> endpoint is added, `AUTH_WORKER_URL` stays empty and uploads/deletes return
> `503 auth_not_configured` (fail closed). Set the variable in the Dashboard
> under **Workers → escapebook-skins → Settings → Variables** after the auth
> worker is updated.

## API

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| `GET` | `/api/skins/:nickname.png` | no | Skin PNG, or the default Steve skin |
| `GET` | `/api/skins/:nickname/info` | no | `{ nickname, exists, size_bytes, uploaded_at, updated_at }` |
| `POST` | `/api/skins/upload` | yes | `multipart/form-data`, field `file` |
| `DELETE` | `/api/skins` | yes | Deletes the caller's skin (`?nickname=` fallback) |

Upload success:

```json
{ "ok": true, "nickname": "Steve", "url": "/api/skins/Steve.png" }
```

Errors are JSON: `{ "ok": false, "error": "code", "message": "..." }`.

Status codes: `400` invalid PNG/dimensions/nickname, `401` missing/invalid
token, `413` file over 1 MB, `503` auth not configured/unavailable.

## Local development

```bash
npm install
npx wrangler d1 execute escapebook-db --local --file=migrations/0001_create_skins.sql
npx wrangler dev
npm run typecheck
```

## CustomSkinLoader

Point the mod's skin source at:

```
https://<worker-host>/api/skins/{username}.png
```

Missing skins return the default Steve PNG instead of 404, so the client always
gets a usable image.
