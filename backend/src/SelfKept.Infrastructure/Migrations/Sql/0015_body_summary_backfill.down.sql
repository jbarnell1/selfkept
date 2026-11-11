-- Manual, reviewed rollback of 0015_body_summary_backfill. Never run automatically.
-- Removes the daily figures of the body metrics that gained a summary rule. They are derived
-- from sample rows and are rebuilt by the next ingest or by re-running the up script.
-- After applying by hand, delete the journal row:
--   DELETE FROM schemaversions WHERE scriptname LIKE '%0015_body_summary_backfill.up.sql';

DELETE FROM daily_summary
WHERE metric_key IN ('body_fat_pct', 'waist_cm', 'lean_mass_kg', 'bone_mass_kg', 'body_water_kg', 'vo2max');
