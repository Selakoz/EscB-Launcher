-- 003_sessions_hwid.sql
-- Migration: bind session tokens to the device HWID and enforce the strict
-- single-session policy (a successful login invalidates all previous tokens).
--
-- The `sessions` table created by 002_sessions.sql only had token/mc_nickname/
-- created_at/expires_at. This migration adds the account IDs, HWID and the
-- validity flag required by GET /auth/validate and POST /auth/logout.
--
-- SQLite has no "ADD COLUMN IF NOT EXISTS", so run this file exactly once
-- against an existing database:
--
--   npx wrangler d1 execute escapebook-db --remote \
--     --file=infra/shared/migrations/003_sessions_hwid.sql
--
-- Fresh databases can use infra/shared/schema.sql instead (it already
-- contains the final table shape).

ALTER TABLE sessions ADD COLUMN ds_id TEXT;
ALTER TABLE sessions ADD COLUMN tg_id TEXT;
ALTER TABLE sessions ADD COLUMN hwid TEXT;
ALTER TABLE sessions ADD COLUMN is_valid INTEGER NOT NULL DEFAULT 1;

-- Existing rows receive is_valid = 1 from the column default; keep them valid
-- until the player logs in again (their next login rotates the token).
UPDATE sessions SET is_valid = 1 WHERE is_valid IS NULL;

-- Backfill the account IDs so old sessions are also invalidated by the
-- strict single-session UPDATE (which matches ds_id/tg_id/nickname).
UPDATE sessions
SET ds_id = (SELECT p.ds_id FROM players p WHERE p.mc_nickname = sessions.mc_nickname),
    tg_id = (SELECT p.tg_id FROM players p WHERE p.mc_nickname = sessions.mc_nickname)
WHERE ds_id IS NULL AND tg_id IS NULL;

CREATE INDEX IF NOT EXISTS idx_sessions_ds_id ON sessions(ds_id);
CREATE INDEX IF NOT EXISTS idx_sessions_tg_id ON sessions(tg_id);
