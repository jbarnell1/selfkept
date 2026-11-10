-- 0018_context: context events (docs/context-design.md). Something going on in a person's life
-- that explains a reading: an illness, a point in the cycle, a new goal, a new scale, travel, a
-- change of medicine, hard training. Rows are what the person logged (source user) or what a rule
-- recorded (source rule); derived context is worked out on read and never stored here.
-- Mirrored by SelfKeptDbContext.ConfigureContext. The note is encrypted with IEncryptionService.

CREATE TABLE IF NOT EXISTS context_event (
  id          uuid        NOT NULL,
  person_id   uuid        NOT NULL,
  kind        varchar(32) NOT NULL,
  started_on  date        NOT NULL,
  ended_on    date        NULL,
  source      varchar(16) NOT NULL DEFAULT 'user',
  note_cipher bytea       NULL,
  created_at  timestamptz NOT NULL DEFAULT now(),
  updated_at  timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT pk_context_event PRIMARY KEY (id),
  CONSTRAINT fk_context_event_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE,
  CONSTRAINT ck_context_event_kind CHECK (kind IN ('illness', 'cycle_phase', 'new_goal', 'scale_change', 'travel', 'medication_change', 'hard_training', 'other')),
  CONSTRAINT ck_context_event_source CHECK (source IN ('user', 'rule', 'derived')),
  CONSTRAINT ck_context_event_dates CHECK (ended_on IS NULL OR ended_on >= started_on)
);
CREATE INDEX IF NOT EXISTS ix_context_event_person_id_started_on ON context_event (person_id, started_on DESC);
