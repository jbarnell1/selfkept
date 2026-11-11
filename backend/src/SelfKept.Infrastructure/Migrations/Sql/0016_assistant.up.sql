-- 0016_assistant: the in-app assistant (docs/assistant-design.md). Conversations and their
-- messages, the evening check-in (a conversation of kind checkin, one per local day), the cached
-- morning brief, and the evening nudge settings with a log of what was sent. Mirrored by
-- AssistantModel.Configure. Everything here is the person's own and never shared. Free text
-- (titles, message content, the brief, the ntfy address and token) is encrypted with
-- IEncryptionService; state and meta hold keys and ids only.

CREATE TABLE IF NOT EXISTS assistant_conversation (
  id            uuid        NOT NULL,
  person_id     uuid        NOT NULL,
  kind          varchar(16) NOT NULL DEFAULT 'chat',
  title_cipher  text        NULL,
  day           date        NULL,
  state         jsonb       NOT NULL DEFAULT '{}',
  created_at    timestamptz NOT NULL DEFAULT now(),
  updated_at    timestamptz NOT NULL DEFAULT now(),
  ended_at      timestamptz NULL,
  CONSTRAINT pk_assistant_conversation PRIMARY KEY (id),
  CONSTRAINT fk_assistant_conversation_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE,
  CONSTRAINT ck_assistant_conversation_kind CHECK (kind IN ('chat', 'checkin')),
  CONSTRAINT ck_assistant_conversation_day CHECK (kind <> 'checkin' OR day IS NOT NULL)
);
CREATE INDEX IF NOT EXISTS ix_assistant_conversation_person_id_updated_at ON assistant_conversation (person_id, updated_at DESC);
-- One evening check-in per person and local day.
CREATE UNIQUE INDEX IF NOT EXISTS ux_assistant_conversation_person_id_day_checkin ON assistant_conversation (person_id, day) WHERE kind = 'checkin';

CREATE TABLE IF NOT EXISTS assistant_message (
  id              uuid        NOT NULL,
  conversation_id uuid        NOT NULL,
  person_id       uuid        NOT NULL,
  role            varchar(16) NOT NULL,
  content_cipher  text        NOT NULL,
  origin          varchar(16) NOT NULL DEFAULT 'code',
  meta            jsonb       NOT NULL DEFAULT '{}',
  created_at      timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT pk_assistant_message PRIMARY KEY (id),
  CONSTRAINT fk_assistant_message_conversation_id FOREIGN KEY (conversation_id) REFERENCES assistant_conversation (id) ON DELETE CASCADE,
  CONSTRAINT fk_assistant_message_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE,
  CONSTRAINT ck_assistant_message_role CHECK (role IN ('user', 'assistant')),
  CONSTRAINT ck_assistant_message_origin CHECK (origin IN ('model', 'template', 'guard', 'code'))
);
CREATE INDEX IF NOT EXISTS ix_assistant_message_conversation_id_created_at ON assistant_message (conversation_id, created_at);

CREATE TABLE IF NOT EXISTS assistant_brief (
  person_id    uuid        NOT NULL,
  day          date        NOT NULL,
  facts_hash   varchar(64) NOT NULL,
  text_cipher  text        NOT NULL,
  generated    boolean     NOT NULL DEFAULT false,
  computed_at  timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT pk_assistant_brief PRIMARY KEY (person_id, day),
  CONSTRAINT fk_assistant_brief_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS nudge_setting (
  person_id          uuid        NOT NULL,
  enabled            boolean     NOT NULL DEFAULT false,
  ntfy_url_cipher    text        NULL,
  ntfy_token_cipher  text        NULL,
  evening_time       time        NOT NULL DEFAULT '20:00',
  updated_at         timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT pk_nudge_setting PRIMARY KEY (person_id),
  CONSTRAINT fk_nudge_setting_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE
);

-- At most one nudge of a kind per person and local day.
CREATE TABLE IF NOT EXISTS nudge_log (
  person_id  uuid        NOT NULL,
  kind       varchar(16) NOT NULL,
  day        date        NOT NULL,
  sent_at    timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT pk_nudge_log PRIMARY KEY (person_id, kind, day),
  CONSTRAINT fk_nudge_log_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE,
  CONSTRAINT ck_nudge_log_kind CHECK (kind IN ('evening'))
);
