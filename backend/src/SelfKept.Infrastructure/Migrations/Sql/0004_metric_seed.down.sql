-- Manual, reviewed rollback of 0004_metric_seed. Never run automatically.
-- Empties the catalogue. The foreign keys from sample and daily_summary refuse while any data
-- references a metric, which is the right guard: roll 0003 back first if that is the intent.
-- After applying by hand, delete the journal row:
--   DELETE FROM schemaversions WHERE scriptname LIKE '%0004_metric_seed.up.sql';

DELETE FROM metric WHERE key IS NOT NULL;
