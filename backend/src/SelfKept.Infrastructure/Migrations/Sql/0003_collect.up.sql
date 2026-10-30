-- 0003_collect: metric catalogue, data sources, samples (partitioned by month), workouts,
-- check-ins and daily summaries. docs/ingest-contract.md is the contract; the EF model in
-- SelfKeptDbContext.ConfigureCollect mirrors this file. The catalogue rows arrive in 0004.

-- The catalogue. key is the stable id clients post; unit is the stored unit; category is the
-- share_grant category a viewer needs.
CREATE TABLE IF NOT EXISTS metric (
  key               varchar(64) NOT NULL,
  unit              varchar(32) NOT NULL,
  kind              varchar(16) NOT NULL,
  category          varchar(16) NOT NULL,
  display_name      varchar(80) NOT NULL,
  blurb             text        NOT NULL,
  good_direction    varchar(16) NOT NULL,
  loinc             varchar(16) NULL,
  value_labels      jsonb       NULL,
  sex_applicability varchar(8)  NOT NULL DEFAULT 'both',
  CONSTRAINT pk_metric PRIMARY KEY (key),
  CONSTRAINT ck_metric_kind CHECK (kind IN ('instant', 'interval', 'daily')),
  CONSTRAINT ck_metric_category CHECK (category IN ('activity', 'sleep', 'vitals', 'body', 'labs', 'cycle', 'checkins', 'insights')),
  CONSTRAINT ck_metric_good_direction CHECK (good_direction IN ('up', 'down', 'neutral', 'range')),
  CONSTRAINT ck_metric_sex_applicability CHECK (sex_applicability IN ('both', 'female', 'male'))
);

-- One row per device install (or the per-person system row that check-ins are written under).
-- Deleting is soft: deleted_at is set, the samples stay and keep pointing here.
CREATE TABLE IF NOT EXISTS source (
  id             uuid        NOT NULL,
  person_id      uuid        NOT NULL,
  kind           varchar(16) NOT NULL,
  device_name    varchar(80) NOT NULL,
  platform       varchar(32) NOT NULL,
  app_version    varchar(32) NULL,
  enabled        boolean     NOT NULL DEFAULT true,
  cursor         text        NULL,
  last_sync_at   timestamptz NULL,
  last_sample_at timestamptz NULL,
  sample_count   bigint      NOT NULL DEFAULT 0,
  created_at     timestamptz NOT NULL DEFAULT now(),
  deleted_at     timestamptz NULL,
  CONSTRAINT pk_source PRIMARY KEY (id),
  CONSTRAINT fk_source_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE,
  CONSTRAINT ck_source_kind CHECK (kind IN ('healthkit', 'health_connect', 'oura', 'google_health', 'manual', 'lab_scan'))
);
CREATE INDEX IF NOT EXISTS ix_source_person_id ON source (person_id);
CREATE UNIQUE INDEX IF NOT EXISTS ix_source_person_id_kind_device_name_platform
  ON source (person_id, kind, device_name, platform) WHERE deleted_at IS NULL;

-- Every reading. Range-partitioned by month on start_at: the ingest service calls
-- ensure_sample_partition(date) for each month a batch touches, so no default partition exists
-- (rows in a default partition would block creating the real one later). Postgres requires the
-- partition key in every unique constraint, hence (id, start_at) and (source_id, source_uid,
-- start_at); a source_uid names one reading with a fixed start, so the contract's
-- (sourceId, sourceUid) idempotency holds. Inserting by hand needs the partition first:
--   SELECT ensure_sample_partition('2026-10-01');
CREATE TABLE IF NOT EXISTS sample (
  id         uuid             NOT NULL,
  person_id  uuid             NOT NULL,
  source_id  uuid             NOT NULL,
  source_uid varchar(128)     NOT NULL,
  metric_key varchar(64)      NOT NULL,
  start_at   timestamptz      NOT NULL,
  end_at     timestamptz      NULL,
  value      double precision NOT NULL,
  unit       varchar(32)      NOT NULL,
  meta       jsonb            NULL,
  created_at timestamptz      NOT NULL DEFAULT now(),
  CONSTRAINT pk_sample PRIMARY KEY (id, start_at),
  CONSTRAINT fk_sample_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE,
  CONSTRAINT fk_sample_source_id FOREIGN KEY (source_id) REFERENCES source (id) ON DELETE RESTRICT,
  CONSTRAINT fk_sample_metric_key FOREIGN KEY (metric_key) REFERENCES metric (key),
  CONSTRAINT ck_sample_end_after_start CHECK (end_at IS NULL OR end_at >= start_at)
) PARTITION BY RANGE (start_at);
CREATE UNIQUE INDEX IF NOT EXISTS ix_sample_source_id_source_uid_start_at ON sample (source_id, source_uid, start_at);
CREATE INDEX IF NOT EXISTS ix_sample_person_id_metric_key_start_at ON sample (person_id, metric_key, start_at DESC);

-- Creates the monthly partition that holds the given day, if it does not exist yet. Bounds are
-- written with an explicit +00 offset so the session time zone cannot shift them. Concurrent
-- callers for the same month are serialised on an advisory lock: CREATE TABLE IF NOT EXISTS
-- alone can still race on the catalogue.
CREATE OR REPLACE FUNCTION ensure_sample_partition(day date) RETURNS void
LANGUAGE plpgsql AS $$
DECLARE
  month_start date := date_trunc('month', day)::date;
  month_end   date := (date_trunc('month', day) + interval '1 month')::date;
  part_name   text := 'sample_' || to_char(month_start, 'YYYYMM');
BEGIN
  PERFORM pg_advisory_xact_lock(hashtext(part_name));
  IF to_regclass(part_name) IS NULL THEN
    EXECUTE format(
      'CREATE TABLE %I PARTITION OF sample FOR VALUES FROM (%L) TO (%L)',
      part_name,
      to_char(month_start, 'YYYY-MM-DD') || ' 00:00:00+00',
      to_char(month_end, 'YYYY-MM-DD') || ' 00:00:00+00');
  END IF;
END;
$$;

-- Workouts. The route (GeoJSON) is encrypted at rest: a route is a home address.
CREATE TABLE IF NOT EXISTS workout (
  id           uuid             NOT NULL,
  person_id    uuid             NOT NULL,
  source_id    uuid             NOT NULL,
  source_uid   varchar(128)     NOT NULL,
  type         varchar(64)      NOT NULL,
  start_at     timestamptz      NOT NULL,
  end_at       timestamptz      NOT NULL,
  distance_m   double precision NULL,
  energy_kcal  double precision NULL,
  avg_hr       double precision NULL,
  max_hr       double precision NULL,
  route_cipher bytea            NULL,
  meta         jsonb            NULL,
  created_at   timestamptz      NOT NULL DEFAULT now(),
  CONSTRAINT pk_workout PRIMARY KEY (id),
  CONSTRAINT fk_workout_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE,
  CONSTRAINT fk_workout_source_id FOREIGN KEY (source_id) REFERENCES source (id) ON DELETE RESTRICT,
  CONSTRAINT ck_workout_end_after_start CHECK (end_at >= start_at)
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_workout_source_id_source_uid ON workout (source_id, source_uid);
CREATE INDEX IF NOT EXISTS ix_workout_person_id_start_at ON workout (person_id, start_at DESC);

-- Mood check-ins. The note is encrypted at rest. Each check-in also writes a checkin_mood sample.
CREATE TABLE IF NOT EXISTS checkin (
  id          uuid        NOT NULL,
  person_id   uuid        NOT NULL,
  at          timestamptz NOT NULL,
  mood        smallint    NOT NULL,
  note_cipher bytea       NULL,
  tags        text[]      NOT NULL DEFAULT '{}',
  created_at  timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT pk_checkin PRIMARY KEY (id),
  CONSTRAINT fk_checkin_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE,
  CONSTRAINT ck_checkin_mood CHECK (mood BETWEEN 1 AND 6)
);
CREATE INDEX IF NOT EXISTS ix_checkin_person_id_at ON checkin (person_id, at DESC);

-- One row per (person, metric, local calendar day), derived by the server after every write.
CREATE TABLE IF NOT EXISTS daily_summary (
  person_id   uuid             NOT NULL,
  metric_key  varchar(64)      NOT NULL,
  day         date             NOT NULL,
  value       double precision NOT NULL,
  min         double precision NULL,
  max         double precision NULL,
  count       integer          NOT NULL,
  computed_at timestamptz      NOT NULL DEFAULT now(),
  CONSTRAINT pk_daily_summary PRIMARY KEY (person_id, metric_key, day),
  CONSTRAINT fk_daily_summary_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE,
  CONSTRAINT fk_daily_summary_metric_key FOREIGN KEY (metric_key) REFERENCES metric (key)
);
