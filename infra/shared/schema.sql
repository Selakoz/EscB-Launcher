CREATE TABLE IF NOT EXISTS players (
    mc_nickname TEXT PRIMARY KEY,
    tg_id       TEXT UNIQUE,
    ds_id       TEXT UNIQUE,
    is_banned   INTEGER DEFAULT 0,
    created_at  INTEGER DEFAULT (strftime('%s','now'))
);
CREATE TABLE IF NOT EXISTS devices (
    mc_nickname TEXT NOT NULL,
    hwid        TEXT NOT NULL,
    trusted     INTEGER DEFAULT 0,
    last_seen   INTEGER DEFAULT (strftime('%s','now')),
    PRIMARY KEY (mc_nickname, hwid),
    FOREIGN KEY (mc_nickname) REFERENCES players(mc_nickname) ON DELETE CASCADE
);
-- Session tokens are bound to the device HWID that created them. A successful
-- login invalidates every previous token for the same player (strict
-- single-session policy). Tokens expire after 30 days (expires_at, unix seconds).
CREATE TABLE IF NOT EXISTS sessions (
    token       TEXT PRIMARY KEY,
    ds_id       TEXT,
    tg_id       TEXT,
    mc_nickname TEXT NOT NULL,
    hwid        TEXT,
    created_at  INTEGER NOT NULL DEFAULT (strftime('%s','now')),
    expires_at  INTEGER NOT NULL,
    is_valid    INTEGER NOT NULL DEFAULT 1,
    FOREIGN KEY (mc_nickname) REFERENCES players(mc_nickname) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_sessions_nickname ON sessions(mc_nickname);
CREATE INDEX IF NOT EXISTS idx_sessions_ds_id ON sessions(ds_id);
CREATE INDEX IF NOT EXISTS idx_sessions_tg_id ON sessions(tg_id);

CREATE TABLE IF NOT EXISTS otp_codes (
    mc_nickname TEXT PRIMARY KEY,
    code        TEXT NOT NULL,
    expires_at  INTEGER NOT NULL,
    confirmed   INTEGER DEFAULT 0,
    attempts    INTEGER DEFAULT 0,
    FOREIGN KEY (mc_nickname) REFERENCES players(mc_nickname) ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS rate_limits (
    key         TEXT PRIMARY KEY,
    count       INTEGER DEFAULT 1,
    window_end  INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS message_map_ds_to_tg (
    discord_message_id  TEXT PRIMARY KEY,
    telegram_message_id TEXT NOT NULL,
    created_at          INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS message_map_tg_to_ds (
    telegram_message_id TEXT PRIMARY KEY,
    discord_message_id  TEXT NOT NULL,
    created_at          INTEGER NOT NULL
);
