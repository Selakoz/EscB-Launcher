-- 002_sessions.sql
-- Migration: persistent session tokens issued by POST /auth/confirm-otp.
-- Before this table the worker generated session tokens but never stored
-- them, so they could not be validated later (e.g. by GET /auth/validate).
--
-- Run (from the repository root):
--   npx wrangler d1 execute escapebook-db --remote --file=infra/shared/migrations/002_sessions.sql

CREATE TABLE IF NOT EXISTS sessions (
    token       TEXT PRIMARY KEY,
    mc_nickname TEXT NOT NULL,
    created_at  INTEGER DEFAULT (strftime('%s','now')),
    expires_at  INTEGER NOT NULL,
    FOREIGN KEY (mc_nickname) REFERENCES players(mc_nickname) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_sessions_nickname ON sessions(mc_nickname);
