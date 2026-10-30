# Ingest contract (Package 2, Collect)

Phones collect; the server stores. Every reading is a `Sample` of a catalogued metric. Batches are idempotent on `(sourceId, sourceUid)` so a phone can resend after a crash or a dropped connection and nothing doubles.

## Sources

| Method | Path | Body | Returns |
| --- | --- | --- | --- |
| GET | `/api/sources` | | `SourceDto[]` for the caller's person |
| POST | `/api/sources` | `{ kind, deviceName, platform, appVersion }` | `SourceDto` (new or the existing one for this device install) |
| PATCH | `/api/sources/{id}` | `{ enabled?, deviceName? }` | `SourceDto` |
| DELETE | `/api/sources/{id}` | | 204, samples are kept and marked orphaned |

`SourceDto = { id, kind, deviceName, platform, enabled, lastSyncAt, lastSampleAt, sampleCount, cursor }`

`kind = "healthkit" | "health_connect" | "oura" | "google_health" | "manual" | "lab_scan"`

`cursor` is an opaque string the client stores on the server after each successful batch (HealthKit anchor, Health Connect changes token, or an ISO timestamp). It lets a reinstalled app resume.

## Batches

`POST /api/ingest/batch`

```json
{
  "sourceId": "…",
  "cursor": "opaque-after-this-batch",
  "samples": [
    { "sourceUid": "hk-UUID", "metric": "heart_rate_bpm", "start": "2026-10-29T06:00:00Z", "end": null, "value": 58, "unit": "bpm", "meta": { "motion": "sedentary" } },
    { "sourceUid": "hk-UUID", "metric": "sleep_stage", "start": "…", "end": "…", "value": 3, "unit": "stage", "meta": { "stage": "deep" } }
  ],
  "workouts": [
    { "sourceUid": "hk-UUID", "type": "running", "start": "…", "end": "…", "distanceM": 10020, "energyKcal": 612, "avgHr": 148, "maxHr": 171, "routeGeoJson": null, "meta": {} }
  ]
}
```

Response `200 { accepted, duplicates, rejected: [{ sourceUid, reason }], cursor }`. A batch is capped at 5,000 samples; clients page. Unknown metric keys are rejected per sample, never the whole batch.

Rules:

- `start` is required, `end` is null for instant readings and set for intervals.
- `value` is numeric. Categorical readings carry a small integer in `value` and the label in `meta` (see sleep stages below).
- `unit` must match the catalogue unit for that metric; the server converts a short allowlist (`count/min` to `bpm`, `mi` to `m`, `lb` to `kg`, `F` to `C`) and rejects anything else.
- Samples are attributed to the source's person. A client cannot post for another person.
- Daily summaries are never posted by clients; the server derives them.

## Metric catalogue, first release

Keys are stable identifiers. Units are the stored unit.

| Key | Unit | Kind | HealthKit | Health Connect | Oura |
| --- | --- | --- | --- | --- | --- |
| `steps` | count | interval | stepCount | StepsRecord | daily_activity.steps |
| `distance_m` | m | interval | distanceWalkingRunning, distanceCycling | DistanceRecord | |
| `active_energy_kcal` | kcal | interval | activeEnergyBurned | ActiveCaloriesBurnedRecord | daily_activity.active_calories |
| `basal_energy_kcal` | kcal | interval | basalEnergyBurned | BasalMetabolicRateRecord | |
| `exercise_min` | min | interval | appleExerciseTime | ExerciseSessionRecord (derived) | |
| `flights` | count | interval | flightsClimbed | FloorsClimbedRecord | |
| `heart_rate_bpm` | bpm | instant | heartRate | HeartRateRecord samples | heartrate |
| `resting_hr_bpm` | bpm | daily | restingHeartRate | RestingHeartRateRecord | daily_sleep / sleep.lowest_heart_rate |
| `hrv_sdnn_ms` | ms | instant | heartRateVariabilitySDNN | HeartRateVariabilityRmssdRecord (stored as `hrv_rmssd_ms`) | |
| `hrv_rmssd_ms` | ms | instant | | HeartRateVariabilityRmssdRecord | sleep.hrv (5-min) |
| `spo2_pct` | % | instant | oxygenSaturation | OxygenSaturationRecord | daily_spo2.average |
| `respiratory_rate_bpm` | breaths/min | instant | respiratoryRate | RespiratoryRateRecord | sleep.average_breath |
| `wrist_temp_delta_c` | C | daily | appleSleepingWristTemperature | SkinTemperatureRecord (delta) | daily_readiness.temperature_deviation |
| `body_temp_c` | C | instant | bodyTemperature | BodyTemperatureRecord | |
| `sleep_stage` | stage | interval | sleepAnalysis | SleepSessionRecord stages | sleep.sleep_phase_5_min |
| `vo2max` | mL/kg/min | instant | vo2Max | Vo2MaxRecord | daily_cardiovascular_age (no) |
| `weight_kg` | kg | instant | bodyMass | WeightRecord | |
| `body_fat_pct` | % | instant | bodyFatPercentage | BodyFatRecord | |
| `height_cm` | cm | instant | height | HeightRecord | |
| `menstrual_flow` | level | interval | menstrualFlow | MenstruationFlowRecord | |
| `basal_body_temp_c` | C | instant | basalBodyTemperature | BasalBodyTemperatureRecord | |
| `blood_glucose_mmol` | mmol/L | instant | bloodGlucose | BloodGlucoseRecord | |
| `blood_pressure_sys` | mmHg | instant | bloodPressureSystolic | BloodPressureRecord | |
| `blood_pressure_dia` | mmHg | instant | bloodPressureDiastolic | BloodPressureRecord | |
| `oura_readiness` | score | daily | | | daily_readiness.score |
| `oura_sleep_score` | score | daily | | | daily_sleep.score |
| `oura_activity_score` | score | daily | | | daily_activity.score |
| `oura_resilience` | level | daily | | | daily_resilience.level |
| `oura_stress_high_s` | s | daily | | | daily_stress.stress_high |
| `checkin_mood` | level | instant | | | app check-in, 1 to 6 |

Sleep stage values: `0 in_bed`, `1 awake`, `2 light` (HealthKit core), `3 deep`, `4 rem`, `5 unspecified`.

Menstrual flow values: `1 light`, `2 medium`, `3 heavy`, `4 unspecified`, `0 none`.

Lab markers live in the same catalogue with a `lab.` prefix (for example `lab.apob`, unit mg/dL) and arrive through the labs endpoints in Package 4, not through ingest.

## Client behaviour

- **iOS**: `HKObserverQuery` with `enableBackgroundDelivery` for each type, `HKAnchoredObjectQuery` to pull deltas, anchor persisted locally and mirrored to `cursor`. On every foreground, run a full delta pass. Respect that iOS may wake the app roughly hourly.
- **Android**: Health Connect `getChangesToken` per record type, `WorkManager` periodic job every 15 minutes with network constraint, plus a foreground pass. Request `READ_HEALTH_DATA_IN_BACKGROUND` and `READ_HEALTH_DATA_HISTORY`.
- Both: write to the Drift outbox first, flush in batches of up to 2,000, mark sent on 200, keep on any failure. Show the last successful flush on the Sources screen.
- Initial backfill: 90 days on first connect, paged, lowest priority, so the first sync does not take an hour of battery.

## Check-ins

| Method | Path | Body | Returns |
| --- | --- | --- | --- |
| POST | `/api/checkins` | `{ at?, mood, note? }` mood 1 to 6, `at` defaults to now | `CheckinDto` |
| GET | `/api/checkins?from&to&personId?` | | `CheckinDto[]` newest first, max 500 |
| DELETE | `/api/checkins/{id}` | | 204, own check-ins only |

`CheckinDto = { id, personId, at, mood, note, tags: string[] }`. The note is encrypted at rest. A check-in also writes a `checkin_mood` sample so it shows on trends.

## Reading data

All read endpoints take an optional `personId`. Absent means the caller. Present means a household member who has granted the matching `ShareCategory` to the caller, otherwise 404 (not 403, so the existence of data is not leaked).

| Method | Path | Returns |
| --- | --- | --- |
| GET | `/api/data/daily?metric&from&to&personId?` | `{ metric, unit, days: [{ day, value, min?, max?, count }] }` one row per calendar day in the person's time zone |
| GET | `/api/data/samples?metric&from&to&limit?&cursor?&personId?` | `{ items: SampleDto[], nextCursor? }` newest first, limit max 2,000 |
| GET | `/api/data/overview?personId?` | `{ metrics: [{ metric, unit, firstAt, lastAt, count, lastValue }] }` for the Sources and Trends screens |
| GET | `/api/catalogue/metrics` | the metric catalogue: `{ key, unit, kind, category: ShareCategory, displayName, blurb, goodDirection }[]` |

`SampleDto = { id, metric, start, end, value, unit, sourceId, meta }`

Metric to `ShareCategory` mapping lives in the catalogue: steps, distance, energy, exercise, flights, workouts are `activity`; sleep_stage is `sleep`; heart rate, HRV, SpO2, respiratory rate, temperatures, blood pressure, glucose, Oura scores are `vitals`; weight, body fat, height, vo2max are `body`; menstrual and basal temperature are `cycle`; checkin_mood is `checkins`; `lab.*` is `labs`.
