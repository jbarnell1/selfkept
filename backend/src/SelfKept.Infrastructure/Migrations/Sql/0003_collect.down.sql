-- Manual, reviewed rollback of 0003_collect. Never run automatically. This removes every
-- sample, workout, check-in and summary (sample CASCADE drops the monthly partitions).
-- After applying by hand, delete the journal row:
--   DELETE FROM schemaversions WHERE scriptname LIKE '%0003_collect.up.sql';

DROP TABLE IF EXISTS daily_summary;
DROP TABLE IF EXISTS checkin;
DROP TABLE IF EXISTS workout;
DROP TABLE IF EXISTS sample CASCADE;
DROP FUNCTION IF EXISTS ensure_sample_partition(date);
DROP TABLE IF EXISTS source;
DROP TABLE IF EXISTS metric;
