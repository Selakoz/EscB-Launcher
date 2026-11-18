-- 001_ids_to_text.sql
-- Migration: players.tg_id / players.ds_id from INTEGER to TEXT.
-- Reason: Telegram/Discord IDs are Snowflakes that exceed JS
-- Number.MAX_SAFE_INTEGER (2^53-1); INTEGER storage corrupts them and
-- Discord client-side rejects them for INTEGER (type 4) slash options.
--
-- SQLite table-rebuild pattern; existing linked accounts are PRESERVED
-- via CAST and do NOT need re-linking. The PRIMARY KEY is part of the
-- table definition; there are no separate indexes to recreate.
--
-- Run (from the repository root):
--   npx wrangler d1 execute escapebook-db --remote --file=infra/shared/migrations/001_ids_to_text.sql

CREATE TABLE players_new (
    mc_nickname TEXT PRIMARY KEY,
    tg_id       TEXT UNIQUE,
    ds_id       TEXT UNIQUE,
    is_banned   INTEGER DEFAULT 0,
    created_at  INTEGER DEFAULT (strftime('%s','now'))
);

INSERT INTO players_new (mc_nickname, tg_id, ds_id, is_banned, created_at)
SELECT mc_nickname, CAST(tg_id AS TEXT), CAST(ds_id AS TEXT), is_banned, created_at
FROM players;

DROP TABLE players;

ALTER TABLE players_new RENAME TO players;
