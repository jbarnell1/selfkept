-- 0011_sso: signing in through the household's own OpenID Connect provider (docs/sso-design.md).
--
-- user_identity ties a provider login (issuer + subject) to one account. sso_auth_state holds a
-- trip to the provider and back while it is in flight, and sso_handoff a finished phone sign-in
-- until the app collects it. Codes and states are stored as SHA-256 hashes, the PKCE verifier
-- encrypted with IEncryptionService.
CREATE TABLE IF NOT EXISTS user_identity (
  id            uuid          NOT NULL,
  user_id       uuid          NOT NULL,
  issuer        varchar(500)  NOT NULL,
  subject       varchar(255)  NOT NULL,
  email         varchar(254)  NULL,
  created_at    timestamptz   NOT NULL DEFAULT now(),
  last_used_at  timestamptz   NULL,
  CONSTRAINT pk_user_identity PRIMARY KEY (id),
  CONSTRAINT fk_user_identity_user_id FOREIGN KEY (user_id) REFERENCES user_account (id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_user_identity_issuer_subject ON user_identity (issuer, subject);
CREATE UNIQUE INDEX IF NOT EXISTS ix_user_identity_user_id_issuer ON user_identity (user_id, issuer);

CREATE TABLE IF NOT EXISTS sso_auth_state (
  state_hash      varchar(64)  NOT NULL,
  nonce           varchar(100) NOT NULL,
  verifier_cipher text         NOT NULL,
  intent          varchar(16)  NOT NULL,
  client          varchar(16)  NOT NULL,
  user_id         uuid         NULL,
  created_at      timestamptz  NOT NULL DEFAULT now(),
  expires_at      timestamptz  NOT NULL,
  CONSTRAINT pk_sso_auth_state PRIMARY KEY (state_hash),
  CONSTRAINT fk_sso_auth_state_user_id FOREIGN KEY (user_id) REFERENCES user_account (id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS sso_handoff (
  code_hash   varchar(64) NOT NULL,
  user_id     uuid        NOT NULL,
  created_at  timestamptz NOT NULL DEFAULT now(),
  expires_at  timestamptz NOT NULL,
  CONSTRAINT pk_sso_handoff PRIMARY KEY (code_hash),
  CONSTRAINT fk_sso_handoff_user_id FOREIGN KEY (user_id) REFERENCES user_account (id) ON DELETE CASCADE
);
