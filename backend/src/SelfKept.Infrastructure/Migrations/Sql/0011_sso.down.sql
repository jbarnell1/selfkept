-- Reverse of 0011_sso. Manual, reviewed rollback only; never auto-run.
-- Remove the matching row from schemaversions after applying. Drops the provider links: people
-- sign in with their passwords again.
DROP TABLE IF EXISTS sso_handoff;
DROP TABLE IF EXISTS sso_auth_state;
DROP TABLE IF EXISTS user_identity;
