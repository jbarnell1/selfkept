-- 0021_lookup: the cloud lookup (docs/lookup-design.md). Each person's own switch, and one row per
-- question sent to the cloud model with the exact outgoing text and the sources, both encrypted
-- with IEncryptionService. The answer is an assistant_message with origin 'cloud' whose meta holds
-- the lookup id only. Mirrored by LookupModel.Configure. Own-only: never shared.

CREATE TABLE IF NOT EXISTS lookup_setting (
  person_id   uuid        NOT NULL,
  enabled     boolean     NOT NULL DEFAULT false,
  enabled_at  timestamptz NULL,
  updated_at  timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT pk_lookup_setting PRIMARY KEY (person_id),
  CONSTRAINT fk_lookup_setting_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE
);

-- Deleting the conversation deletes its lookups. The message links are cleared, not cascaded, so
-- deleting one message never takes the record of what was sent with it.
CREATE TABLE IF NOT EXISTS assistant_lookup (
  id                   uuid        NOT NULL,
  person_id            uuid        NOT NULL,
  conversation_id      uuid        NOT NULL,
  question_message_id  uuid        NULL,
  answer_message_id    uuid        NULL,
  mode                 varchar(16) NOT NULL,
  status               varchar(16) NOT NULL DEFAULT 'sent',
  question_cipher      text        NOT NULL,
  sources_cipher       text        NULL,
  provider             varchar(32) NOT NULL,
  model                varchar(64) NOT NULL,
  input_tokens         integer     NULL,
  output_tokens        integer     NULL,
  created_at           timestamptz NOT NULL DEFAULT now(),
  answered_at          timestamptz NULL,
  CONSTRAINT pk_assistant_lookup PRIMARY KEY (id),
  CONSTRAINT fk_assistant_lookup_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE,
  CONSTRAINT fk_assistant_lookup_conversation_id FOREIGN KEY (conversation_id) REFERENCES assistant_conversation (id) ON DELETE CASCADE,
  CONSTRAINT fk_assistant_lookup_question_message_id FOREIGN KEY (question_message_id) REFERENCES assistant_message (id) ON DELETE SET NULL,
  CONSTRAINT fk_assistant_lookup_answer_message_id FOREIGN KEY (answer_message_id) REFERENCES assistant_message (id) ON DELETE SET NULL,
  CONSTRAINT ck_assistant_lookup_mode CHECK (mode IN ('anonymized', 'detailed')),
  CONSTRAINT ck_assistant_lookup_status CHECK (status IN ('sent', 'answered', 'failed'))
);
CREATE INDEX IF NOT EXISTS ix_assistant_lookup_person_id_created_at ON assistant_lookup (person_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_assistant_lookup_conversation_id ON assistant_lookup (conversation_id);

-- A cloud answer is a message of its own origin. Widening a check is additive: every row that
-- passed the old check passes this one.
ALTER TABLE assistant_message DROP CONSTRAINT IF EXISTS ck_assistant_message_origin;
ALTER TABLE assistant_message ADD CONSTRAINT ck_assistant_message_origin CHECK (origin IN ('model', 'template', 'guard', 'code', 'cloud'));
