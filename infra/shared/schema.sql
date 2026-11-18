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

-- Мост Discord -> Telegram: сопоставление сообщений для reply-тредов.
CREATE TABLE IF NOT EXISTS message_map_ds_to_tg (
    discord_message_id  TEXT PRIMARY KEY,
    telegram_message_id TEXT NOT NULL,
    created_at          INTEGER NOT NULL
);

-- Мост Telegram -> Discord (обратное сопоставление).
CREATE TABLE IF NOT EXISTS message_map_tg_to_ds (
    telegram_message_id TEXT PRIMARY KEY,
    discord_message_id  TEXT NOT NULL,
    created_at          INTEGER NOT NULL
);
