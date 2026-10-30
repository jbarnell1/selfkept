-- Reverse of 0009_oura. Manual, reviewed rollback only; never auto-run.
-- Remove the matching row from schemaversions after applying. Drops the Oura links; samples
-- already brought in stay under their source.
DROP TABLE IF EXISTS oura_auth_state;
DROP TABLE IF EXISTS oura_connection;
