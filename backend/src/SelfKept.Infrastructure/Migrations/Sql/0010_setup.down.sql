-- Reverse of 0010_setup. Manual, reviewed rollback only; never auto-run.
-- Remove the matching row from schemaversions after applying. Settings saved from the app go,
-- so an Oura application entered there has to be entered again (or set in the .env file).
ALTER TABLE oura_auth_state DROP COLUMN IF EXISTS return_path;
ALTER TABLE user_account DROP COLUMN IF EXISTS setup_completed_at;
ALTER TABLE user_account DROP COLUMN IF EXISTS setup_plan;
DROP TABLE IF EXISTS server_setting;
