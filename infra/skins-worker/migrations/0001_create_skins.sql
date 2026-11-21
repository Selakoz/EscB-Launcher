-- EscapeBook skins storage (D1-only, no R2).
-- Each nickname owns at most one skin. skin_data holds the raw PNG bytes.
CREATE TABLE IF NOT EXISTS skins (
  nickname TEXT PRIMARY KEY,
  skin_data BLOB NOT NULL,
  content_type TEXT NOT NULL DEFAULT 'image/png',
  size_bytes INTEGER NOT NULL,
  uploaded_at INTEGER NOT NULL,
  updated_at INTEGER NOT NULL
);
