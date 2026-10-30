-- 0010_setup: first-run setup (docs/setup-design.md).
--
-- server_setting holds the few settings an admin can change from the app instead of the .env
-- file: today the Oura application's client id and secret. Secrets are stored encrypted with
-- IEncryptionService, never in plain text. A value set in the environment wins over a row here.
CREATE TABLE IF NOT EXISTS server_setting (
  key         varchar(100) NOT NULL,
  value       text         NOT NULL,
  updated_at  timestamptz  NOT NULL DEFAULT now(),
  updated_by  uuid         NULL,
  CONSTRAINT pk_server_setting PRIMARY KEY (key),
  CONSTRAINT fk_server_setting_updated_by FOREIGN KEY (updated_by) REFERENCES user_account (id) ON DELETE SET NULL
);

-- Each account's answers to the first-run questions and when it finished them. Accounts that
-- existed before this release have already found their way around, so they count as finished.
ALTER TABLE user_account ADD COLUMN IF NOT EXISTS setup_plan varchar(32)[] NOT NULL DEFAULT '{}';
ALTER TABLE user_account ADD COLUMN IF NOT EXISTS setup_completed_at timestamptz NULL;
UPDATE user_account SET setup_completed_at = created_at WHERE setup_completed_at IS NULL;

-- Where the browser goes after Oura sends it back: the Sources screen or the setup steps.
ALTER TABLE oura_auth_state ADD COLUMN IF NOT EXISTS return_path varchar(32) NULL;
