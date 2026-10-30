-- 0006_labs: lab reports and results, the marker alias table and the population reference table.
-- docs/labs-design.md is the design; the EF model in SelfKeptDbContext.ConfigureLabs mirrors this
-- file. The alias and reference rows arrive in 0007 (generated from Data/LabSeed.cs).

-- One confirmed panel. The source image and the notes are encrypted at rest (IEncryptionService);
-- image_sha256 is the hash of the plain image so a re-upload of the same photo is recognisable.
CREATE TABLE IF NOT EXISTS lab_report (
  id                  uuid        NOT NULL,
  person_id           uuid        NOT NULL,
  provider            varchar(16) NOT NULL,
  collected_on        date        NOT NULL,
  received_on         date        NULL,
  source_image_cipher bytea       NULL,
  image_sha256        varchar(64) NULL,
  notes_cipher        bytea       NULL,
  created_at          timestamptz NOT NULL DEFAULT now(),
  confirmed_at        timestamptz NULL,
  CONSTRAINT pk_lab_report PRIMARY KEY (id),
  CONSTRAINT fk_lab_report_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE,
  CONSTRAINT ck_lab_report_provider CHECK (provider IN ('rythm', 'manual', 'other'))
);
CREATE INDEX IF NOT EXISTS ix_lab_report_person_id_collected_on ON lab_report (person_id, collected_on DESC);

-- One marker on one report, stored in the catalogue unit. flag is what the lab printed;
-- reference_low/high is the lab's own range when it was visible; derived marks a value the
-- server computed from other results (the three ratios).
CREATE TABLE IF NOT EXISTS lab_result (
  id             uuid             NOT NULL,
  report_id      uuid             NOT NULL,
  person_id      uuid             NOT NULL,
  metric_key     varchar(64)      NOT NULL,
  value          double precision NOT NULL,
  unit           varchar(32)      NOT NULL,
  flag           varchar(8)       NOT NULL DEFAULT 'none',
  raw_text       text             NULL,
  confirmed      boolean          NOT NULL DEFAULT true,
  reference_low  double precision NULL,
  reference_high double precision NULL,
  derived        boolean          NOT NULL DEFAULT false,
  created_at     timestamptz      NOT NULL DEFAULT now(),
  CONSTRAINT pk_lab_result PRIMARY KEY (id),
  CONSTRAINT fk_lab_result_report_id FOREIGN KEY (report_id) REFERENCES lab_report (id) ON DELETE CASCADE,
  CONSTRAINT fk_lab_result_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE,
  CONSTRAINT fk_lab_result_metric_key FOREIGN KEY (metric_key) REFERENCES metric (key),
  CONSTRAINT ck_lab_result_flag CHECK (flag IN ('low', 'high', 'normal', 'none'))
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_lab_result_report_id_metric_key ON lab_result (report_id, metric_key);
CREATE INDEX IF NOT EXISTS ix_lab_result_person_id_metric_key ON lab_result (person_id, metric_key);

-- Names a lab prints for a marker (Rythm display names plus common synonyms), for the parser.
CREATE TABLE IF NOT EXISTS lab_marker_alias (
  metric_key varchar(64) NOT NULL,
  alias      varchar(80) NOT NULL,
  CONSTRAINT pk_lab_marker_alias PRIMARY KEY (metric_key, alias),
  CONSTRAINT fk_lab_marker_alias_metric_key FOREIGN KEY (metric_key) REFERENCES metric (key)
);

-- Approximate adult population ranges per marker and sex (docs/lab-ranges.md). A blank bound
-- means the source gives only the other one. tier is how an out-of-range value is flagged.
CREATE TABLE IF NOT EXISTS lab_reference (
  metric_key     varchar(64)      NOT NULL,
  sex            varchar(8)       NOT NULL,
  low            double precision NULL,
  high           double precision NULL,
  optimal_low    double precision NULL,
  optimal_high   double precision NULL,
  good_direction varchar(16)      NOT NULL DEFAULT 'range',
  tier           varchar(16)      NOT NULL DEFAULT 'watch',
  source_url     text             NOT NULL,
  CONSTRAINT pk_lab_reference PRIMARY KEY (metric_key, sex),
  CONSTRAINT fk_lab_reference_metric_key FOREIGN KEY (metric_key) REFERENCES metric (key),
  CONSTRAINT ck_lab_reference_sex CHECK (sex IN ('both', 'female', 'male')),
  CONSTRAINT ck_lab_reference_good_direction CHECK (good_direction IN ('up', 'down', 'range')),
  CONSTRAINT ck_lab_reference_tier CHECK (tier IN ('watch', 'clinician', 'none'))
);
