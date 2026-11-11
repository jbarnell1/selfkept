-- Reverse of 0021_lookup. Applied by hand; see README.md. Cloud answers must go before the origin
-- check can be narrowed again: they are removed with their lookups.
DELETE FROM assistant_message WHERE origin = 'cloud';
ALTER TABLE assistant_message DROP CONSTRAINT IF EXISTS ck_assistant_message_origin;
ALTER TABLE assistant_message ADD CONSTRAINT ck_assistant_message_origin CHECK (origin IN ('model', 'template', 'guard', 'code'));
DROP TABLE IF EXISTS assistant_lookup;
DROP TABLE IF EXISTS lookup_setting;
