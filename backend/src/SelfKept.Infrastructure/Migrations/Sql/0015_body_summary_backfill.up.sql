-- 0015_body_summary_backfill: daily figures for body metrics that gained a summary rule
-- (docs/body-design.md, "Daily figures").
--
-- Body fat, VO2 max and the other body composition metrics are now averaged per day like weight.
-- New readings get their daily row at ingest; this fills in the days already stored, so the
-- Progress charts have history from the first open. A time zone Postgres does not know falls back
-- to UTC, the same as the application does. Days that already have a row are left alone.
INSERT INTO daily_summary (person_id, metric_key, day, value, min, max, count, computed_at)
SELECT
  s.person_id,
  s.metric_key,
  (s.start_at AT TIME ZONE COALESCE((SELECT z.name FROM pg_timezone_names z WHERE z.name = p.time_zone), 'UTC'))::date AS day,
  avg(s.value),
  min(s.value),
  max(s.value),
  count(*)::integer,
  now()
FROM sample s
JOIN person p ON p.id = s.person_id
WHERE s.metric_key IN ('body_fat_pct', 'waist_cm', 'lean_mass_kg', 'bone_mass_kg', 'body_water_kg', 'vo2max')
GROUP BY s.person_id, s.metric_key, 3
ON CONFLICT (person_id, metric_key, day) DO NOTHING;
