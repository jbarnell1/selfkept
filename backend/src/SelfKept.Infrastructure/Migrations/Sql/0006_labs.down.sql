-- Reverse of 0006_labs. Manual, reviewed rollback only; never auto-run.
-- Remove the matching row from schemaversions after applying. Apply 0007's down first.
DROP TABLE IF EXISTS lab_reference;
DROP TABLE IF EXISTS lab_marker_alias;
DROP TABLE IF EXISTS lab_result;
DROP TABLE IF EXISTS lab_report;
