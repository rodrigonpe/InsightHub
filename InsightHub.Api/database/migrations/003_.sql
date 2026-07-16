ALTER TYPE announcement_action
ADD VALUE IF NOT EXISTS 'EXTENDED';

ALTER TABLE bot_announcements
ADD COLUMN IF NOT EXISTS source_announcement_id UUID NULL;

ALTER TABLE bot_announcements
DROP CONSTRAINT IF EXISTS fk_bot_announcements_source;

ALTER TABLE bot_announcements
ADD CONSTRAINT fk_bot_announcements_source
FOREIGN KEY (source_announcement_id)
REFERENCES bot_announcements(id);

CREATE INDEX IF NOT EXISTS ix_bot_announcements_source_announcement_id
ON bot_announcements(source_announcement_id);