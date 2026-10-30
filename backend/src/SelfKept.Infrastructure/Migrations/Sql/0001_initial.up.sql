-- 0001_initial: accounts, people, households, invites, sharing and refresh tokens.
-- The runner wraps this script in a transaction and journals it to schemaversions.
-- Table and column names are snake_case; the EF model in SelfKeptDbContext mirrors this file.

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS person (
  id            uuid        NOT NULL,
  display_name  varchar(80) NOT NULL,
  sex_at_birth  varchar(16) NOT NULL DEFAULT 'unspecified',
  birth_date    date        NULL,
  units         varchar(16) NOT NULL DEFAULT 'metric',
  time_zone     varchar(64) NOT NULL DEFAULT 'UTC',
  created_at    timestamptz NOT NULL DEFAULT now(),
  updated_at    timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT pk_person PRIMARY KEY (id),
  CONSTRAINT ck_person_sex_at_birth CHECK (sex_at_birth IN ('female', 'male', 'unspecified')),
  CONSTRAINT ck_person_units CHECK (units IN ('metric', 'imperial'))
);

CREATE TABLE IF NOT EXISTS household (
  id                 uuid        NOT NULL,
  name               varchar(80) NOT NULL,
  created_by_user_id uuid        NOT NULL,
  created_at         timestamptz NOT NULL DEFAULT now(),
  updated_at         timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT pk_household PRIMARY KEY (id)
);

-- "user" is a reserved word in PostgreSQL, so the account table is user_account.
CREATE TABLE IF NOT EXISTS user_account (
  id            uuid         NOT NULL,
  email         varchar(254) NOT NULL,
  password_hash text         NOT NULL,
  display_name  varchar(80)  NOT NULL,
  person_id     uuid         NOT NULL,
  household_id  uuid         NULL,
  created_at    timestamptz  NOT NULL DEFAULT now(),
  updated_at    timestamptz  NOT NULL DEFAULT now(),
  last_login_at timestamptz  NULL,
  CONSTRAINT pk_user_account PRIMARY KEY (id),
  CONSTRAINT fk_user_account_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE RESTRICT,
  CONSTRAINT fk_user_account_household_id FOREIGN KEY (household_id) REFERENCES household (id) ON DELETE SET NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_user_account_email ON user_account (email);
CREATE UNIQUE INDEX IF NOT EXISTS ix_user_account_person_id ON user_account (person_id);

CREATE TABLE IF NOT EXISTS household_member (
  id           uuid        NOT NULL,
  household_id uuid        NOT NULL,
  person_id    uuid        NOT NULL,
  user_id      uuid        NULL,
  role         varchar(16) NOT NULL DEFAULT 'member',
  joined_at    timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT pk_household_member PRIMARY KEY (id),
  CONSTRAINT fk_household_member_household_id FOREIGN KEY (household_id) REFERENCES household (id) ON DELETE CASCADE,
  CONSTRAINT fk_household_member_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE,
  CONSTRAINT fk_household_member_user_id FOREIGN KEY (user_id) REFERENCES user_account (id) ON DELETE SET NULL,
  CONSTRAINT ck_household_member_role CHECK (role IN ('admin', 'member'))
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_household_member_household_id_person_id ON household_member (household_id, person_id);

-- One-time sign-up codes. Only the SHA-256 hash (hex) of the code is stored.
CREATE TABLE IF NOT EXISTS household_invite (
  id                 uuid        NOT NULL,
  household_id       uuid        NOT NULL,
  code_hash          varchar(64) NOT NULL,
  created_by_user_id uuid        NOT NULL,
  created_at         timestamptz NOT NULL DEFAULT now(),
  expires_at         timestamptz NOT NULL,
  used_by_user_id    uuid        NULL,
  used_at            timestamptz NULL,
  CONSTRAINT pk_household_invite PRIMARY KEY (id),
  CONSTRAINT fk_household_invite_household_id FOREIGN KEY (household_id) REFERENCES household (id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_household_invite_code_hash ON household_invite (code_hash);

-- "owner lets viewer see category". No row means no access; a person always sees their own data.
CREATE TABLE IF NOT EXISTS share_grant (
  id               uuid        NOT NULL,
  household_id     uuid        NOT NULL,
  owner_person_id  uuid        NOT NULL,
  viewer_person_id uuid        NOT NULL,
  category         varchar(16) NOT NULL,
  created_at       timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT pk_share_grant PRIMARY KEY (id),
  CONSTRAINT fk_share_grant_household_id FOREIGN KEY (household_id) REFERENCES household (id) ON DELETE CASCADE,
  CONSTRAINT fk_share_grant_owner_person_id FOREIGN KEY (owner_person_id) REFERENCES person (id) ON DELETE CASCADE,
  CONSTRAINT fk_share_grant_viewer_person_id FOREIGN KEY (viewer_person_id) REFERENCES person (id) ON DELETE CASCADE,
  CONSTRAINT ck_share_grant_category CHECK (category IN ('activity', 'sleep', 'vitals', 'body', 'labs', 'cycle', 'checkins', 'insights')),
  CONSTRAINT ck_share_grant_not_self CHECK (owner_person_id <> viewer_person_id)
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_share_grant_owner_person_id_viewer_person_id_category ON share_grant (owner_person_id, viewer_person_id, category);
CREATE INDEX IF NOT EXISTS ix_share_grant_viewer_person_id ON share_grant (viewer_person_id);

-- Rotating refresh tokens, stored hashed. family groups one login's rotation chain.
CREATE TABLE IF NOT EXISTS refresh_token (
  id                     uuid        NOT NULL,
  user_id                uuid        NOT NULL,
  token_hash             varchar(64) NOT NULL,
  family                 uuid        NOT NULL,
  expires_at             timestamptz NOT NULL,
  created_at             timestamptz NOT NULL DEFAULT now(),
  revoked_at             timestamptz NULL,
  replaced_by_token_hash varchar(64) NULL,
  CONSTRAINT pk_refresh_token PRIMARY KEY (id),
  CONSTRAINT fk_refresh_token_user_id FOREIGN KEY (user_id) REFERENCES user_account (id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_refresh_token_token_hash ON refresh_token (token_hash);
CREATE INDEX IF NOT EXISTS ix_refresh_token_user_id ON refresh_token (user_id);
CREATE INDEX IF NOT EXISTS ix_refresh_token_family ON refresh_token (family);
