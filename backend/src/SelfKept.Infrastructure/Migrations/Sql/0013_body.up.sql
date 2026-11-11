-- 0013_body: body, goals and the day log (docs/body-design.md). A person's height, their main
-- and secondary goal, the daily and weekly targets on the way there, the day log (food, drink,
-- symptoms, training notes), the medicines and supplements they take, and a strength log.
-- Mirrored by SelfKeptDbContext.ConfigureBody. Free text is encrypted with IEncryptionService;
-- tags and substance flags are plain so targets, patterns and rules can count them.

ALTER TABLE person ADD COLUMN IF NOT EXISTS height_cm double precision NULL;

CREATE TABLE IF NOT EXISTS goal (
  id            uuid             NOT NULL,
  person_id     uuid             NOT NULL,
  slot          varchar(16)      NOT NULL,
  kind          varchar(32)      NOT NULL,
  title_cipher  text             NULL,
  metric_key    varchar(64)      NULL,
  start_value   double precision NULL,
  target_value  double precision NULL,
  pace_per_week double precision NULL,
  started_on    date             NOT NULL,
  target_date   date             NULL,
  status        varchar(16)      NOT NULL DEFAULT 'active',
  created_at    timestamptz      NOT NULL DEFAULT now(),
  updated_at    timestamptz      NOT NULL DEFAULT now(),
  ended_at      timestamptz      NULL,
  CONSTRAINT pk_goal PRIMARY KEY (id),
  CONSTRAINT fk_goal_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE,
  CONSTRAINT ck_goal_slot CHECK (slot IN ('main', 'secondary')),
  CONSTRAINT ck_goal_status CHECK (status IN ('active', 'done', 'dropped'))
);
-- One active goal per slot.
CREATE UNIQUE INDEX IF NOT EXISTS ux_goal_person_id_slot_active ON goal (person_id, slot) WHERE status = 'active';
CREATE INDEX IF NOT EXISTS ix_goal_person_id_started_on ON goal (person_id, started_on DESC);

CREATE TABLE IF NOT EXISTS daily_target (
  id          uuid             NOT NULL,
  person_id   uuid             NOT NULL,
  goal_id     uuid             NULL,
  label       varchar(48)      NOT NULL,
  kind        varchar(16)      NOT NULL,
  metric_key  varchar(64)      NULL,
  tag         varchar(32)      NULL,
  comparator  varchar(16)      NOT NULL DEFAULT 'at_least',
  value       double precision NOT NULL,
  period      varchar(8)       NOT NULL DEFAULT 'day',
  active      boolean          NOT NULL DEFAULT true,
  sort_order  smallint         NOT NULL DEFAULT 0,
  created_at  timestamptz      NOT NULL DEFAULT now(),
  updated_at  timestamptz      NOT NULL DEFAULT now(),
  CONSTRAINT pk_daily_target PRIMARY KEY (id),
  CONSTRAINT fk_daily_target_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE,
  CONSTRAINT fk_daily_target_goal_id FOREIGN KEY (goal_id) REFERENCES goal (id) ON DELETE SET NULL,
  CONSTRAINT ck_daily_target_kind CHECK (kind IN ('metric', 'journal', 'strength')),
  CONSTRAINT ck_daily_target_comparator CHECK (comparator IN ('at_least', 'at_most')),
  CONSTRAINT ck_daily_target_period CHECK (period IN ('day', 'week'))
);
CREATE INDEX IF NOT EXISTS ix_daily_target_person_id ON daily_target (person_id, sort_order);

CREATE TABLE IF NOT EXISTS journal_entry (
  id          uuid        NOT NULL,
  person_id   uuid        NOT NULL,
  at          timestamptz NOT NULL,
  day         date        NOT NULL,
  kind        varchar(16) NOT NULL,
  text_cipher bytea       NULL,
  tags        text[]      NOT NULL DEFAULT '{}',
  portion     varchar(8)  NULL,
  source      varchar(16) NOT NULL DEFAULT 'typed',
  message_id  uuid        NULL,
  created_at  timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT pk_journal_entry PRIMARY KEY (id),
  CONSTRAINT fk_journal_entry_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE,
  CONSTRAINT ck_journal_entry_kind CHECK (kind IN ('food', 'drink', 'symptom', 'training', 'sleep', 'note')),
  CONSTRAINT ck_journal_entry_portion CHECK (portion IS NULL OR portion IN ('small', 'usual', 'large')),
  CONSTRAINT ck_journal_entry_source CHECK (source IN ('chip', 'typed', 'assistant', 'checkin'))
);
CREATE INDEX IF NOT EXISTS ix_journal_entry_person_id_day ON journal_entry (person_id, day DESC);
CREATE INDEX IF NOT EXISTS ix_journal_entry_message_id ON journal_entry (message_id) WHERE message_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS medication (
  id            uuid        NOT NULL,
  person_id     uuid        NOT NULL,
  kind          varchar(16) NOT NULL,
  detail_cipher text        NOT NULL,
  generic       boolean     NULL,
  schedule      varchar(16) NOT NULL DEFAULT 'daily',
  substances    text[]      NOT NULL DEFAULT '{}',
  started_on    date        NULL,
  stopped_on    date        NULL,
  created_at    timestamptz NOT NULL DEFAULT now(),
  updated_at    timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT pk_medication PRIMARY KEY (id),
  CONSTRAINT fk_medication_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE,
  CONSTRAINT ck_medication_kind CHECK (kind IN ('medication', 'supplement')),
  CONSTRAINT ck_medication_schedule CHECK (schedule IN ('daily', 'weekly', 'as_needed'))
);
CREATE INDEX IF NOT EXISTS ix_medication_person_id ON medication (person_id, created_at DESC);

CREATE TABLE IF NOT EXISTS strength_set (
  id         uuid             NOT NULL,
  person_id  uuid             NOT NULL,
  day        date             NOT NULL,
  lift       varchar(32)      NOT NULL,
  weight_kg  double precision NOT NULL,
  reps       smallint         NOT NULL,
  rpe        double precision NULL,
  created_at timestamptz      NOT NULL DEFAULT now(),
  CONSTRAINT pk_strength_set PRIMARY KEY (id),
  CONSTRAINT fk_strength_set_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE,
  CONSTRAINT ck_strength_set_reps CHECK (reps BETWEEN 1 AND 100),
  CONSTRAINT ck_strength_set_weight_kg CHECK (weight_kg >= 0 AND weight_kg <= 1000)
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_strength_set_person_id_day_lift ON strength_set (person_id, day, lift);
