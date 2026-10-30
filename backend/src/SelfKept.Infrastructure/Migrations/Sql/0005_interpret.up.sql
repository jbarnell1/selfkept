-- 0005_interpret: baselines, insight cards, model call log and the Today sentence cache.
-- docs/insights-design.md is the design; the EF model in SelfKeptDbContext.ConfigureInterpret
-- mirrors this file.

-- One person's normal for one metric as of one day. A row per day so history is replayable.
-- Percentiles are null until the window has a value; n7 and n28 count days with data.
CREATE TABLE IF NOT EXISTS baseline (
  person_id    uuid             NOT NULL,
  metric_key   varchar(64)      NOT NULL,
  computed_for date             NOT NULL,
  median7      double precision NULL,
  median28     double precision NULL,
  p10_28       double precision NULL,
  p25_28       double precision NULL,
  p75_28       double precision NULL,
  p90_28       double precision NULL,
  slope28      double precision NULL,
  sd28         double precision NULL,
  n7           integer          NOT NULL DEFAULT 0,
  n28          integer          NOT NULL DEFAULT 0,
  computed_at  timestamptz      NOT NULL DEFAULT now(),
  CONSTRAINT pk_baseline PRIMARY KEY (person_id, metric_key, computed_for),
  CONSTRAINT fk_baseline_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE,
  CONSTRAINT fk_baseline_metric_key FOREIGN KEY (metric_key) REFERENCES metric (key)
);
CREATE INDEX IF NOT EXISTS ix_baseline_person_id_computed_for ON baseline (person_id, computed_for DESC);

-- A card: one rule's finding for one person on one day, created once per (person, kind, day).
-- templated always exists; generated is the model's rewording when it passed validation.
CREATE TABLE IF NOT EXISTS insight (
  id           uuid        NOT NULL,
  person_id    uuid        NOT NULL,
  day          date        NOT NULL,
  kind         varchar(32) NOT NULL,
  severity     varchar(16) NOT NULL,
  metrics      text[]      NOT NULL DEFAULT '{}',
  evidence     jsonb       NOT NULL DEFAULT '{}'::jsonb,
  templated    text        NOT NULL,
  generated    text        NULL,
  model        varchar(80) NULL,
  created_at   timestamptz NOT NULL DEFAULT now(),
  shown_at     timestamptz NULL,
  dismissed_at timestamptz NULL,
  reaction     varchar(16) NULL,
  CONSTRAINT pk_insight PRIMARY KEY (id),
  CONSTRAINT fk_insight_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE,
  CONSTRAINT ck_insight_severity CHECK (severity IN ('notice', 'watch', 'clinician')),
  CONSTRAINT ck_insight_reaction CHECK (reaction IS NULL OR reaction IN ('helpful', 'knew', 'wrong'))
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_insight_person_id_kind_day ON insight (person_id, kind, day);
CREATE INDEX IF NOT EXISTS ix_insight_person_id_day ON insight (person_id, day DESC);

-- Every model call: that it happened, how long it took, whether it worked. Never the text.
CREATE TABLE IF NOT EXISTS ai_call (
  id                uuid        NOT NULL,
  person_id         uuid        NULL,
  purpose           varchar(32) NOT NULL,
  prompt_hash       varchar(64) NOT NULL,
  prompt_tokens     integer     NULL,
  completion_tokens integer     NULL,
  latency_ms        integer     NOT NULL,
  ok                boolean     NOT NULL,
  error             text        NULL,
  created_at        timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT pk_ai_call PRIMARY KEY (id),
  CONSTRAINT fk_ai_call_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE SET NULL
);
CREATE INDEX IF NOT EXISTS ix_ai_call_person_id_created_at ON ai_call (person_id, created_at DESC);

-- The Today sentence, kept for an hour unless a check-in, a batch or a recompute drops it.
CREATE TABLE IF NOT EXISTS today_cache (
  person_id   uuid        NOT NULL,
  computed_at timestamptz NOT NULL,
  sentence    text        NOT NULL,
  payload     jsonb       NOT NULL DEFAULT '{}'::jsonb,
  CONSTRAINT pk_today_cache PRIMARY KEY (person_id),
  CONSTRAINT fk_today_cache_person_id FOREIGN KEY (person_id) REFERENCES person (id) ON DELETE CASCADE
);
