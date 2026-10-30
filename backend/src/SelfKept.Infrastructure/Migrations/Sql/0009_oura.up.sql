-- 0009_oura: a person's link to Oura's cloud (docs/connect-design.md, "Oura"). The tokens are
-- encrypted with IEncryptionService; Oura refresh tokens are single use, so every refresh writes
-- both again. The poller reads next_pull_at; a revoked grant clears it and sets last_error.

CREATE TABLE IF NOT EXISTS oura_connection (
  person_id          uuid         NOT NULL,
  user_id            uuid         NOT NULL,
  source_id          uuid         NOT NULL,
  access_cipher      text         NOT NULL,
  refresh_cipher     text         NOT NULL,
  access_expires_at  timestamptz  NOT NULL,
  scope              varchar(256) NULL,
  connected_at       timestamptz  NOT NULL DEFAULT now(),
  last_pull_at       timestamptz  NULL,
  last_pull_through  date         NULL,
  next_pull_at       timestamptz  NULL,
  last_error         varchar(500) NULL,
  CONSTRAINT pk_oura_connection PRIMARY KEY (person_id),
  CONSTRAINT fk_oura_connection_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE,
  CONSTRAINT fk_oura_connection_user_id FOREIGN KEY (user_id) REFERENCES user_account (id) ON DELETE CASCADE,
  CONSTRAINT fk_oura_connection_source_id FOREIGN KEY (source_id) REFERENCES source (id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS ix_oura_connection_next_pull_at ON oura_connection (next_pull_at) WHERE next_pull_at IS NOT NULL;

-- One Oura authorize round trip in flight: the state is stored hashed, the PKCE verifier encrypted.
CREATE TABLE IF NOT EXISTS oura_auth_state (
  state_hash      varchar(64) NOT NULL,
  user_id         uuid        NOT NULL,
  person_id       uuid        NOT NULL,
  verifier_cipher text        NOT NULL,
  created_at      timestamptz NOT NULL DEFAULT now(),
  expires_at      timestamptz NOT NULL,
  CONSTRAINT pk_oura_auth_state PRIMARY KEY (state_hash),
  CONSTRAINT fk_oura_auth_state_user_id FOREIGN KEY (user_id) REFERENCES user_account (id) ON DELETE CASCADE
);
