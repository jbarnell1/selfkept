# Insights design (Package 3, Interpret)

Rules decide what is notable. The local model only writes the sentence. If the model is down, a templated sentence ships instead, so the Today screen never depends on inference.

## Baselines

A nightly job (job-locked hosted service, 03:30 in the person's time zone, and on demand after a large backfill) computes per person and metric from `daily_summary`:

| Field | Definition |
| --- | --- |
| `median7`, `median28` | median of the last 7 and 28 daily values |
| `p10_28`, `p25_28`, `p75_28`, `p90_28` | percentiles over the last 28 days |
| `slope28` | least-squares slope per day over 28 days, in metric units |
| `n7`, `n28` | how many days had data |
| `sd28` | standard deviation over 28 days |

Baselines are stored in `baseline (person_id, metric_key, computed_for date, ...)` with one row per day so history is replayable. A metric needs `n28 >= 14` before any rule may fire on it; before that the Today sentence says the app is still learning the person's normal.

Metrics with baselines in v0.1: `sleep_stage` (as nightly minutes asleep), `hrv_sdnn_ms` or `hrv_rmssd_ms` (whichever the person has), `resting_hr_bpm`, `respiratory_rate_bpm`, `spo2_pct`, `wrist_temp_delta_c`, `steps`, `active_energy_kcal`, `exercise_min`, `weight_kg`, `checkin_mood`, `oura_readiness`, `oura_sleep_score`.

## Rule set

Each rule yields zero or one candidate per person per day with `kind`, `severity` (`notice`, `watch`, `clinician`), `metrics`, `evidence` (the numbers it used) and `templated` (the fallback sentence). Candidates are ranked by severity then by how far outside the band, and at most two cards are shown on Today.

| Kind | Fires when | Severity | Templated fallback |
| --- | --- | --- | --- |
| `hrv_low_run` | HRV below `p10_28` for 3 consecutive nights | watch | "HRV has sat below your usual range for three nights. That often follows poor sleep, a hard training block or the start of an illness." |
| `hrv_recovered` | HRV back above `p25_28` after an `hrv_low_run` | notice | "HRV is back inside your normal range." |
| `rhr_elevated` | resting HR above `p90_28` by 5 bpm or more for 2 nights | watch | "Resting heart rate is up on your normal. Worth an easier day." |
| `illness_pattern` | `rhr_elevated` and wrist temperature above `p90_28` on the same night, or respiratory rate above `p90_28` too | watch | "Heart rate, temperature and breathing are all up tonight. This pattern often goes with coming down with something." |
| `sleep_debt` | nightly minutes asleep below `p25_28` on 3 of the last 4 nights | watch | "Three short nights in four. Sleep debt shows up in HRV and mood within days." |
| `sleep_good_week` | 7-day median asleep above 28-day median by 20 min or more | notice | "A better week of sleep than your usual." |
| `activity_dip` | 7-day median steps below half of `median28` with `n7 >= 5` | notice | "A quieter week than your norm. Nothing wrong with that; just noting it." |
| `training_spike` | 7-day exercise minutes above 1.5 times `median28` | notice | "Training load is well above your usual this week. Recovery markers will tell if it is too much." |
| `weight_trend` | `slope28` of weight implies more than 1 kg change over 28 days with `n28 >= 10` | notice | "Weight has moved by about {delta} kg over the month." |
| `mood_low_run` | check-in mood at 1 or 2 on 3 of the last 5 days | watch | "Three low days this week. If sleep has been short too, that is the usual link; if not, it may be worth talking to someone." |
| `mood_sleep_link` | `mood_low_run` and `sleep_debt` both active | watch | "Low mood and short sleep have lined up this week." |
| `spo2_low` | nightly SpO2 average below 93 percent on 2 nights | clinician | "Overnight oxygen has dipped on two nights. Worth raising with a clinician, especially with snoring or daytime tiredness." |
| `lab_out_of_range` | a lab value outside the population range for the person's sex (Package 4) | watch or clinician by marker | "{marker} came back {above or below} the usual range." |
| `lab_trend` | a lab marker moved 15 percent or more across two panels in the direction of worse (Package 4) | watch | "{marker} has moved {percent} since last month." |
| `learning` | fewer than 14 days of any baseline metric | notice | "Still learning your normal. A couple more weeks of data and the comparisons start." |

Rules never use the word diagnosis. Clinician-tier cards always name what to bring: the metric history and the dates.

## Insight cards

`insight (id, person_id, day, kind, severity, metrics text[], evidence jsonb, templated text, generated text nullable, model text nullable, shown_at, dismissed_at, reaction)` with `reaction` in `helpful`, `knew`, `wrong`. A card is created once per `(person, kind, day)` and re-ranked, not duplicated, when its rule keeps firing. Reactions feed ranking: a kind the person marked `wrong` twice in 30 days is demoted to the Insights archive and off Today.

## The Today sentence

Composed on request (cached per person per hour) from: last night's sleep, the HRV and resting HR position versus baseline, any active cards, and the most recent check-in. The sentence is one or two lines in the calm voice. Templated version first; if a model is configured, the model is asked to rewrite the templated facts into one paragraph without adding any number that is not in the input.

## Model adapter

`IChatModel.CompleteAsync(system, user, options)` with one implementation, `OpenAiCompatibleChatModel`, pointed at `Ai:BaseUrl` (default `http://host.docker.internal:11434/v1` for Ollama), `Ai:Model` (pinned, no default), `Ai:TimeoutSeconds` (default 20), `Ai:Enabled` (default false until a model is configured). Prompt and completion are logged to `ai_call (person_id, purpose, prompt_hash, tokens, latency_ms, ok)` without the text, so the audit shows that a call happened and how long it took. The system prompt forbids new numbers, advice beyond the input, and diagnostic language, and the output is validated: every number in the output must appear in the input, otherwise the templated sentence is used.

## Notification

One optional morning push, the Today sentence, at a time the person picks. Nothing else in v0.1.

## Trends screen

One metric at a time. The 28-day band is `p25_28` to `p75_28` drawn behind the line, `median28` as a hairline, and points outside `p10_28` to `p90_28` marked. Tap a point for that day's summary. Metrics offered are those with `n28 >= 7`. Cycle phase appears as a quiet band when `menstrual_flow` data exists.

## API

Package 3 adds these endpoints. Every one needs a bearer token. `personId` defaults to the caller; another household member's data needs a share grant: the `insights` category for Today and the insights archive, the metric's own category for trends. A missing grant is a 404, never a 403, so nothing leaks. Times are ISO 8601 UTC, days are `YYYY-MM-DD` in the person's time zone, JSON is camelCase.

```
InsightDto = {
  id, day, kind, severity: "notice" | "watch" | "clinician",
  metrics: string[], evidence: object (the numbers the rule used, plus bandDistance),
  text (generated ?? templated), generated: bool,
  shownAt?, dismissedAt?, reaction?: "helpful" | "knew" | "wrong"
}
band = "below" | "inside" | "above"   (against p25_28 to p75_28 of the latest baseline; null until n28 >= 14)
```

| Method | Path | Shape |
| --- | --- | --- |
| GET | `/api/today?personId?` | `{ day, sentence, generated: bool, sleepMinutes?, readiness: { hrv: band|null, restingHr: band|null, sleep: band|null }, cards: InsightDto[] (max 2), lastCheckin: CheckinDto|null, learning: bool }` |
| GET | `/api/insights?personId?&from?&to?&includeDismissed?` | `InsightDto[]` newest first; default the last 30 days, at most 366; dismissed cards hidden unless `includeDismissed=true` |
| POST | `/api/insights/{id}/reaction` | body `{ reaction }`; own cards only; returns the `InsightDto`; 400 on an unknown reaction |
| POST | `/api/insights/{id}/dismiss` | own cards only; returns the `InsightDto` with `dismissedAt` set; idempotent |
| POST | `/api/insights/recompute` | body `{ personId?, day? }` (defaults: the caller, yesterday in their zone); the caller may always run themselves, an admin may run a member of their household; returns `{ personId, day, baselines, insights, kinds[] }`; 5 calls a minute per user |
| GET | `/api/trends?personId?` | `[{ metric, displayName, unit, latestDay?, latestValue?, band: band|null, baselineDays, recent: [{ day, value }], range: { median28, p25, p75, p10, p90 } | null }]` for metrics with `n28 >= 7` in the latest baseline, filtered to the categories the viewer may see. `recent` is the last 28 days for the row's small line |
| GET | `/api/trends/{metric}?personId?&days=28|90` | `{ metric, unit, points: [{ day, value, min?, max? }], band: { median28, p25, p75, p10, p90 } | null, baselineDays: n28 }`; the band is the latest baseline and is null below 7 days |

Notes:

- `sleep_stage` trends carry minutes asleep with unit `min` (the daily summary is minutes, not a stage code).
- Today cards are the two best stored cards for the most recent interpreted day, ranked by severity then `bandDistance`, skipping dismissed cards, the `learning` kind and any kind the person marked `wrong` twice in 30 days. Reading your own Today sets `shownAt` on the cards it shows.
- `lastCheckin` follows the `checkins` grant: a viewer with only `insights` gets `null` there, and still gets the sentence.
- The sentence is cached per person for 60 minutes in `today_cache` and dropped by a check-in, an ingest batch that accepted samples, or a recompute. `generated` is true only when a configured model's rewording passed validation; otherwise the templated sentence is returned.
- The nightly job runs from 03:30 in each person's time zone and interprets yesterday once; `recompute` does the same on demand for any day up to today.
- Audit actions: `insight.reaction`, `insight.dismiss`, `interpret.recompute`. Model calls go to `ai_call`, never to the audit log.
- `GET /api/system/version` lists `insights.rules` and, when `Ai:Enabled` is true with a base URL and model set, `insights.model`.
