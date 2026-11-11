-- Reverse of 0013_body. Applied by hand; see README.md.
DROP TABLE IF EXISTS strength_set;
DROP TABLE IF EXISTS medication;
DROP TABLE IF EXISTS journal_entry;
DROP TABLE IF EXISTS daily_target;
DROP TABLE IF EXISTS goal;
ALTER TABLE person DROP COLUMN IF EXISTS height_cm;
