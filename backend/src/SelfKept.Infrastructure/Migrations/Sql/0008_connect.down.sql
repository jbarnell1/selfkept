-- Reverse of 0008_connect. Manual, reviewed rollback only; never auto-run.
-- Remove the matching row from schemaversions after applying. Drops the MCP grants, personal
-- tokens and assistant memory.
DROP TABLE IF EXISTS ai_memory;
DROP TABLE IF EXISTS personal_access_token;
DROP TABLE IF EXISTS oauth_refresh_token;
DROP TABLE IF EXISTS oauth_authorization_request;
DROP TABLE IF EXISTS oauth_client;
