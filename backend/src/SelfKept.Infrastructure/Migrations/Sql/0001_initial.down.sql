-- Manual, reviewed rollback of 0001_initial. Never run automatically.
-- After applying by hand, delete the journal row:
--   DELETE FROM schemaversions WHERE scriptname LIKE '%0001_initial.up.sql';

DROP TABLE IF EXISTS refresh_token;
DROP TABLE IF EXISTS share_grant;
DROP TABLE IF EXISTS household_invite;
DROP TABLE IF EXISTS household_member;
DROP TABLE IF EXISTS user_account;
DROP TABLE IF EXISTS household;
DROP TABLE IF EXISTS person;
