-- 0012_sample_retention: old heart rate folded into one row per minute (docs/retention-design.md).
--
-- sample_minute holds what SampleRetention folded out of the raw sample table: per person, metric,
-- minute and source, the mean, min, max and count of the readings. The key leads with what every
-- read filters on; id exists only so the samples page can keyset-page minutes next to raw rows.
-- sample_rollup_month records each folded month and when its partition was last rewritten.
CREATE TABLE IF NOT EXISTS sample_minute (
  id          uuid             NOT NULL DEFAULT gen_random_uuid(),
  person_id   uuid             NOT NULL,
  source_id   uuid             NOT NULL,
  metric_key  varchar(64)      NOT NULL,
  minute_at   timestamptz      NOT NULL,
  avg         double precision NOT NULL,
  min         double precision NOT NULL,
  max         double precision NOT NULL,
  count       integer          NOT NULL,
  CONSTRAINT pk_sample_minute PRIMARY KEY (person_id, metric_key, minute_at, source_id),
  CONSTRAINT fk_sample_minute_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE,
  CONSTRAINT fk_sample_minute_source_id FOREIGN KEY (source_id) REFERENCES source (id) ON DELETE RESTRICT,
  CONSTRAINT fk_sample_minute_metric_key FOREIGN KEY (metric_key) REFERENCES metric (key),
  CONSTRAINT ck_sample_minute_count CHECK (count > 0)
);

CREATE TABLE IF NOT EXISTS sample_rollup_month (
  month        date        NOT NULL,
  rolled_at    timestamptz NOT NULL,
  raw_rows     bigint      NOT NULL,
  minute_rows  bigint      NOT NULL,
  compacted_at timestamptz NULL,
  CONSTRAINT pk_sample_rollup_month PRIMARY KEY (month),
  CONSTRAINT ck_sample_rollup_month_first_day CHECK (extract(day FROM month) = 1)
);
