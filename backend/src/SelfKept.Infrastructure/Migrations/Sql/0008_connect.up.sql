-- 0008_connect: what an outside assistant (Open WebUI, Claude, ChatGPT) needs to reach a person's
-- data over MCP. docs/connect-design.md is the design; SelfKeptDbContext.ConfigureConnect mirrors
-- this file.
--
-- Three ways in, all ending at /mcp and never at /api:
--   1. OAuth 2.1 with a dynamically registered client (RFC 7591), the way Open WebUI connects.
--   2. OAuth 2.1 with a Client ID Metadata Document (the client_id is an https URL), the way
--      Claude and ChatGPT connect.
--   3. A personal access token the person creates in the app, for a single-user setup.

-- A known OAuth client. kind is dcr (registered through /api/oauth/register), cimd (client_id is a
-- metadata document URL, fetched and cached) or configured (from Mcp:Clients at startup).
-- secret_hash is set only for a confidential client that asked for client_secret_post or
-- client_secret_basic at registration; public clients (token_endpoint_auth_method none) rely on PKCE.
CREATE TABLE IF NOT EXISTS oauth_client (
  client_id                  varchar(512) NOT NULL,
  kind                       varchar(16)  NOT NULL,
  client_name                varchar(200) NOT NULL,
  client_uri                 varchar(512) NULL,
  logo_uri                   varchar(512) NULL,
  redirect_uris              text         NOT NULL,
  token_endpoint_auth_method varchar(32)  NOT NULL DEFAULT 'none',
  secret_hash                varchar(64)  NULL,
  metadata_fetched_at        timestamptz  NULL,
  created_at                 timestamptz  NOT NULL DEFAULT now(),
  last_used_at               timestamptz  NULL,
  CONSTRAINT pk_oauth_client PRIMARY KEY (client_id),
  CONSTRAINT ck_oauth_client_kind CHECK (kind IN ('dcr', 'cimd', 'configured')),
  CONSTRAINT ck_oauth_client_auth_method CHECK (token_endpoint_auth_method IN ('none', 'client_secret_post', 'client_secret_basic'))
);

-- One /authorize call waiting for the person's consent, then holding a single-use code. The code
-- is stored hashed; user_id is bound when the person decides, never taken from the client.
CREATE TABLE IF NOT EXISTS oauth_authorization_request (
  id                    uuid         NOT NULL,
  client_id             varchar(512) NOT NULL,
  redirect_uri          varchar(2048) NOT NULL,
  scope                 varchar(256) NOT NULL,
  state                 varchar(1024) NULL,
  code_challenge        varchar(128) NOT NULL,
  resource              varchar(512) NULL,
  user_id               uuid         NULL,
  approved              boolean      NULL,
  code_hash             varchar(64)  NULL,
  code_expires_at       timestamptz  NULL,
  consumed              boolean      NOT NULL DEFAULT false,
  created_at            timestamptz  NOT NULL DEFAULT now(),
  expires_at            timestamptz  NOT NULL,
  CONSTRAINT pk_oauth_authorization_request PRIMARY KEY (id),
  CONSTRAINT fk_oauth_authorization_request_client_id FOREIGN KEY (client_id) REFERENCES oauth_client (client_id) ON DELETE CASCADE,
  CONSTRAINT fk_oauth_authorization_request_user_id FOREIGN KEY (user_id) REFERENCES user_account (id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_oauth_authorization_request_code_hash ON oauth_authorization_request (code_hash) WHERE code_hash IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_oauth_authorization_request_expires_at ON oauth_authorization_request (expires_at);

-- Rotating refresh tokens for MCP grants, kept apart from the app's own refresh_token table.
-- Reuse of a rotated token revokes the whole family (replaced_by forms the chain).
CREATE TABLE IF NOT EXISTS oauth_refresh_token (
  id          uuid         NOT NULL,
  user_id     uuid         NOT NULL,
  client_id   varchar(512) NOT NULL,
  token_hash  varchar(64)  NOT NULL,
  scope       varchar(256) NOT NULL,
  resource    varchar(512) NULL,
  created_at  timestamptz  NOT NULL DEFAULT now(),
  expires_at  timestamptz  NOT NULL,
  revoked_at  timestamptz  NULL,
  replaced_by uuid         NULL,
  CONSTRAINT pk_oauth_refresh_token PRIMARY KEY (id),
  CONSTRAINT fk_oauth_refresh_token_user_id FOREIGN KEY (user_id) REFERENCES user_account (id) ON DELETE CASCADE,
  CONSTRAINT fk_oauth_refresh_token_client_id FOREIGN KEY (client_id) REFERENCES oauth_client (client_id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_oauth_refresh_token_token_hash ON oauth_refresh_token (token_hash);
CREATE INDEX IF NOT EXISTS ix_oauth_refresh_token_user_id_client_id ON oauth_refresh_token (user_id, client_id);

-- A token the person creates in the app and pastes into an assistant. Shown once, stored hashed.
-- prefix is the first characters, kept so the app can list "skt_ab12..." without the secret.
CREATE TABLE IF NOT EXISTS personal_access_token (
  id           uuid         NOT NULL,
  user_id      uuid         NOT NULL,
  name         varchar(80)  NOT NULL,
  prefix       varchar(16)  NOT NULL,
  token_hash   varchar(64)  NOT NULL,
  scope        varchar(256) NOT NULL,
  created_at   timestamptz  NOT NULL DEFAULT now(),
  last_used_at timestamptz  NULL,
  revoked_at   timestamptz  NULL,
  CONSTRAINT pk_personal_access_token PRIMARY KEY (id),
  CONSTRAINT fk_personal_access_token_user_id FOREIGN KEY (user_id) REFERENCES user_account (id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_personal_access_token_token_hash ON personal_access_token (token_hash);
CREATE INDEX IF NOT EXISTS ix_personal_access_token_user_id ON personal_access_token (user_id);

-- Durable notes an assistant keeps about a person through the remember tool, readable by every
-- connected assistant through recall_memory and listed (and deletable) in the app. value_cipher is
-- encrypted with IEncryptionService: these are health facts.
CREATE TABLE IF NOT EXISTS ai_memory (
  id            uuid         NOT NULL,
  person_id     uuid         NOT NULL,
  memory_type   varchar(16)  NOT NULL,
  memory_key    varchar(80)  NOT NULL,
  value_cipher  text         NOT NULL,
  display_text  varchar(200) NULL,
  source        varchar(200) NULL,
  created_at    timestamptz  NOT NULL DEFAULT now(),
  updated_at    timestamptz  NOT NULL DEFAULT now(),
  CONSTRAINT pk_ai_memory PRIMARY KEY (id),
  CONSTRAINT fk_ai_memory_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE,
  CONSTRAINT ck_ai_memory_type CHECK (memory_type IN ('goal', 'condition', 'medication', 'preference', 'life_event', 'household'))
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_ai_memory_person_id_type_key ON ai_memory (person_id, memory_type, memory_key);
