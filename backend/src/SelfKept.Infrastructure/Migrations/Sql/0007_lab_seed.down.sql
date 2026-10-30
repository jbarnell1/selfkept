-- Reverse of 0007_lab_seed. Manual, reviewed rollback only; never auto-run.
-- Remove the matching row from schemaversions after applying.
DELETE FROM lab_marker_alias WHERE metric_key LIKE 'lab.%';
DELETE FROM lab_reference WHERE metric_key LIKE 'lab.%';
