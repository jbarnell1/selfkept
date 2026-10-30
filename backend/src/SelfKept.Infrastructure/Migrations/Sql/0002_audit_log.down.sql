-- Manual, reviewed rollback of 0002_audit_log. Never run automatically.
-- After applying by hand, delete the journal row:
--   DELETE FROM schemaversions WHERE scriptname LIKE '%0002_audit_log.up.sql';

DROP TABLE IF EXISTS audit_log;
