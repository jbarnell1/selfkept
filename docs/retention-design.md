# Sample retention

The database must not grow without bound. Measured on a live install in November 2026: 1.9 GB
after three months for one person, of which heart rate is 3.54 million of 3.65 million samples
(about 28 a minute from a Pixel Watch). That is about 600 MB a month per person, around 15 GB a
year for a two-person household. Everything else together is a few megabytes a month.

## Rules

- **Downsample, never delete.** Old heart rate is folded into one row per minute per source
  (`avg`, `min`, `max`, `count`). No reading disappears without leaving its minute behind.
- **Only dense intraday metrics.** Today that is `heart_rate_bpm` alone
  (`SampleRetention.DenseMetrics`). A metric qualifies only if it is `instant` and its daily
  summary is an average, so the daily row can be rebuilt exactly from minutes. Daily summaries,
  sleep stages, HRV, resting heart rate, SpO2, steps and other interval metrics, workouts, labs
  and check-ins are never touched.
- **Raw for 12 months by default.** The admin can change it (below). Months are UTC calendar
  months, the same bounds as the `sample_YYYYMM` partitions. A month is folded once all of it is
  older than the window: with 12 months on 4 November 2026, October 2025 and earlier are folded,
  November 2025 onwards stays raw.
- **The floor is 4 months.** A phone's first Health Connect read goes back 90 days and HealthKit
  can go back further, so raw rows must outlive any re-read; four calendar months always covers
  90 days. `0` keeps raw rows for ever.

## Storage

`sample_minute (id, person_id, source_id, metric_key, minute_at, avg, min, max, count)`, primary
key `(person_id, metric_key, minute_at, source_id)`. The key leads with what every read filters
on and ends with the source, so a phone and a watch that both measured the same minute keep their
own rows. `id` is only for keyset paging next to raw samples. Not partitioned: one person's year
is about 500,000 rows, around 100 MB with the index, against 7 GB raw.

`sample_rollup_month (month, rolled_at, raw_rows, minute_rows, compacted_at)` records each folded
month: how many raw rows went into it (summed over every run that touched the month) and when the
partition was last rewritten to give the space back.

## Why the partition is rewritten, not dropped

A `sample_YYYYMM` partition holds every metric for every person in that month. Dropping it would
take that month's sleep, HRV and steps with it. So a month is handled in two steps:

1. **Fold** (one transaction, one statement): delete the month's dense rows from `sample` and
   insert their minutes into `sample_minute` from the deleted rows themselves
   (`WITH gone AS (DELETE ... RETURNING ...) INSERT ... SELECT ... FROM gone GROUP BY ...`).
   One statement sees one snapshot, so a reading that arrives during the run is either folded or
   left raw for the next run; it is never deleted without being counted. A minute already in
   `sample_minute` (a later run for the same month) is merged: counts add, the average is
   weighted by count, min and max widen. The month's row in `sample_rollup_month` is written in
   the same transaction.
2. **Compact**: `VACUUM (FULL, ANALYZE) sample_YYYYMM`. Deleted rows only free space inside the
   partition, and nothing new is ever written to an old month, so the file would never shrink.
   `VACUUM FULL` rewrites the partition with only the rows left (a few megabytes) and returns the
   rest to the filesystem. It takes an exclusive lock on that one old partition for a second or
   two; `lock_timeout` is 10 seconds, so if a long read holds it the compaction gives up and the
   next night tries again. `compacted_at` is set when it succeeds.

A failed fold rolls back whole. A failed compaction leaves `compacted_at` null. Either way the
next run does the rest, and running it again when everything is done changes nothing.

## When it runs

From the interpret nightly job (`InterpretNightlyJob`), after the people due that tick, once a
day at or after 04:00 in the time zone of the first person on the server (the admin who
installed it), so it runs while the household is asleep. Each run looks at every partition older
than the window, folds any that still hold dense rows (including rows a new source sent later),
and compacts any not yet compacted since its last fold. The admin can also run it at once from
`POST /api/retention/run`.

Every month folded writes `retention.rollup` to the audit log (target `sample_month`, the month,
with `rawRows`, `minuteRows`, `compacted`). Changing the setting writes `retention.changed` with
the old and new values. No health values are written to the audit log.

## Readers

| Reader | What it does with folded months |
| --- | --- |
| Daily summaries (`DailySummaryService`) | A recompute for a heart rate day reads raw samples and minutes in the window and combines them: count is the sum, the mean is weighted by count, min and max over both. A late batch for an old day therefore never overwrites the day's average with just the new rows. |
| Baselines, insights, Today, Trends | Read `daily_summary` only. Unchanged. |
| `GET /api/data/samples` | For a dense metric, minutes are merged with raw rows, newest first, same keyset cursor. A minute comes back as a `SampleDto` with `start` the minute, `end` one minute later, `value` the average, and `meta: { "rollup": { "min", "max", "count" } }`. Raw rows have no `rollup` key. |
| `GET /api/data/overview` and the MCP `list_metrics` tool | `firstAt`, `lastAt` and `count` include folded minutes (count is readings, the sum of `count`), so history does not appear to start 12 months ago. |
| Full JSON export and the redacted summary | Built from daily summaries. Unchanged. |
| Sources (`sampleCount`) | A running total of accepted readings. Unchanged by folding. |

No Flutter screen reads raw samples today; the Trends charts are daily.

## Ingest after a month is folded

The unique index that makes `(sourceId, sourceUid)` idempotent only sees raw rows, so a resent
reading for a folded month would otherwise be stored again and counted twice. Ingest therefore
drops a dense sample when `sample_minute` already has a row for the same person, metric, source
and minute, and reports it as a duplicate. A source never seen before (a new phone that reads
years of HealthKit history) has no minutes there, so its readings are kept raw and folded into
their own rows on the next run.

## The setting

`Retention:RawMonths` in the environment (`SELFKEPT_RETENTION_MONTHS` in `.env`) wins. Otherwise
the admin's value from the app (`server_setting` key `retention.raw_months`, encrypted like every
stored setting), else 12. Values are `0` (keep raw for ever) or 4 to 600.

| Method | Path | Body | Returns |
| --- | --- | --- | --- |
| GET | `/api/retention` | | `RetentionDto` |
| PUT | `/api/retention` | `{ rawMonths }` | `RetentionDto`; 400 outside the allowed values, 409 when the environment sets it |
| POST | `/api/retention/run` | | `RetentionRunDto`: what this run folded and compacted |

`RetentionDto = { rawMonths, source: "default" | "environment" | "app", minimumMonths, rawSince: date|null, months: [{ month, rolledAt, rawRows, minuteRows, compactedAt? }] }`
where `rawSince` is the first day still kept raw (null when raw is kept for ever).
`RetentionRunDto = { rawSince, folded: [{ month, rawRows, minuteRows, compacted }], compacted: string[] }`.
Household admin only; members get 403.

## Disk space on the host

Postgres returns the space to the filesystem. A WSL 2 or Docker Desktop virtual disk (`ext4.vhdx`)
does not shrink by itself when files inside it get smaller; see `selfhost/README.md`.
