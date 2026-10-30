-- Reverse of 0005_interpret. Manual, reviewed rollback only; never auto-run.
-- Remove the matching row from schemaversions after applying.
DROP TABLE IF EXISTS today_cache;
DROP TABLE IF EXISTS ai_call;
DROP TABLE IF EXISTS insight;
DROP TABLE IF EXISTS baseline;
