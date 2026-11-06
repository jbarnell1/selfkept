-- Reverse of 0012_sample_retention. Manual, reviewed rollback only; never auto-run.
-- Remove the matching row from schemaversions after applying. Dropping sample_minute loses the
-- folded heart rate minutes for good (the raw rows are already gone; daily summaries keep the
-- daily values). Take a dump with selfhost/backup.sh first.
DROP TABLE IF EXISTS sample_rollup_month;
DROP TABLE IF EXISTS sample_minute;
