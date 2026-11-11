-- 0017_labs_review: draw conditions on a panel, an age split on the reference table, and trials
-- (docs/labs-design.md, "Draw conditions"; docs/cross-reference.md). Mirrored by
-- SelfKeptDbContext.ConfigureLabs. Every new column is nullable or defaulted, so existing rows
-- read as "never asked".

ALTER TABLE lab_report ADD COLUMN IF NOT EXISTS fasting varchar(8) NULL
  CONSTRAINT ck_lab_report_fasting CHECK (fasting IS NULL OR fasting IN ('yes', 'no', 'unknown'));
ALTER TABLE lab_report ADD COLUMN IF NOT EXISTS fasting_hours smallint NULL
  CONSTRAINT ck_lab_report_fasting_hours CHECK (fasting_hours IS NULL OR fasting_hours BETWEEN 0 AND 72);
ALTER TABLE lab_report ADD COLUMN IF NOT EXISTS draw_time time NULL;
ALTER TABLE lab_report ADD COLUMN IF NOT EXISTS draw_period varchar(16) NULL
  CONSTRAINT ck_lab_report_draw_period CHECK (draw_period IS NULL OR draw_period IN ('morning', 'afternoon', 'evening', 'unknown'));
ALTER TABLE lab_report ADD COLUMN IF NOT EXISTS hard_training boolean NULL;
ALTER TABLE lab_report ADD COLUMN IF NOT EXISTS unwell boolean NULL;
ALTER TABLE lab_report ADD COLUMN IF NOT EXISTS conditions_defaulted boolean NOT NULL DEFAULT false;

-- A second interval from an age upwards (cystatin C at 50 years).
ALTER TABLE lab_reference ADD COLUMN IF NOT EXISTS age_split_at smallint NULL;
ALTER TABLE lab_reference ADD COLUMN IF NOT EXISTS older_low double precision NULL;
ALTER TABLE lab_reference ADD COLUMN IF NOT EXISTS older_high double precision NULL;

CREATE TABLE IF NOT EXISTS trial (
  id             uuid        NOT NULL,
  person_id      uuid        NOT NULL,
  title_cipher   text        NOT NULL,
  change_cipher  text        NULL,
  started_on     date        NOT NULL,
  read_on        date        NOT NULL,
  metric_keys    text[]      NOT NULL DEFAULT '{}',
  status         varchar(16) NOT NULL DEFAULT 'active',
  outcome_cipher text        NULL,
  created_at     timestamptz NOT NULL DEFAULT now(),
  updated_at     timestamptz NOT NULL DEFAULT now(),
  ended_at       timestamptz NULL,
  CONSTRAINT pk_trial PRIMARY KEY (id),
  CONSTRAINT fk_trial_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE,
  CONSTRAINT ck_trial_status CHECK (status IN ('active', 'done', 'dropped')),
  CONSTRAINT ck_trial_dates CHECK (read_on >= started_on)
);
CREATE INDEX IF NOT EXISTS ix_trial_person_id_started_on ON trial (person_id, started_on DESC);
