# Context, patterns and new rules design

This package teaches the record to explain itself. A context engine says what is going on in a
person's life (a cold, a trip, a new goal, a hard session) and the rules read it before they
alarm. Personal patterns compare nights after a day log tag with the other nights. The Systems
tab, sleep and workout read-backs and a weekly review read the same record back in the shapes the
proposal asks for. Everything is worked out on the host; nothing leaves it.

Rules decide, the model only words them, exactly as in `docs/insights-design.md`. No sentence here
is a diagnosis. A watch-level card says what was seen and that it is a pattern worth watching.

## Figures

A number from the literature appears in code or copy only when it was checked at its source
(`.dev/redesign/verified-figures.md`, marked VERIFIED there):

| Figure | Source | Used by |
| --- | --- | --- |
| Cardiac autonomic recovery up to 24 h after low-intensity, 24 to 48 h after threshold-intensity, at least 48 h after high-intensity exercise | Stanley 2013, Sports Med 43(12):1259 | `recovery_strain` (2-day window and its sentence) |
| Caffeine cut total sleep by 45 min; coffee at least 8.8 h before bed | Gardiner 2023, Sleep Med Rev 69:101764 | `yesterday_last_night` (late caffeine with a short night) |
| Ring readings flagged infection with sensitivity 82 % and specificity 63 % | Mason 2022, Sci Rep 12:3463 | `early_signs` (the sentence says 82 % and 37 %, which is 100 less 63) |
| Readings 3 or more breaths a minute above a person's usual | Natarajan 2021, npj Digit Med 4:136 | the breathing signal inside `early_signs` and `cold_starting` |
| Adults should sleep 7 or more hours a night | Watson 2015, Sleep 38(6):843 (AASM and SRS) | `short_sleep_diet`, the weekly review's sleep section |

Every other number in this package is a plain default, named in code and described as a starting
point, not a published threshold. They are listed with each rule below and gathered under
"Defaults".

## Table

Migration `0018_context` adds one table. It hangs off `person` with `ON DELETE CASCADE`.

`context_event`

| Column | Notes |
| --- | --- |
| id, person_id | |
| kind | varchar 32: `illness`, `cycle_phase`, `new_goal`, `scale_change`, `travel`, `medication_change`, `hard_training`, `other` (check) |
| started_on | date |
| ended_on | date null; null while it is still going on (check: not before `started_on`) |
| source | `user` (written by `POST /api/context`), `rule` (reserved for a rule that records context), `derived` (allowed by the check, never written: derived context is not stored) |
| note_cipher | bytea null, the person's own words encrypted with `IEncryptionService` |
| created_at, updated_at | |

Index on `(person_id, started_on DESC)`.

An event with no end date counts for 14 days from its start (`ContextKinds.OpenEndedDays`, a
starting point), so a forgotten event stops explaining readings on its own. An ended event counts
from `started_on` to `ended_on`, both included.

## Derived context

Worked out at interpret time (for the day being interpreted) and on `GET /api/context` (for today),
never stored. Each window is a starting point.

| Kind | When | Since | Reason sentence |
| --- | --- | --- | --- |
| `new_goal` | an active goal started 0 to 13 days before the day | the goal's start | "Your goal to lose fat started on Oct 27, 2026" |
| `medication_change` | a medicine or supplement with `startedOn` or `stoppedOn` 0 to 13 days before the day | that date | "A medicine on your list started on Nov 3, 2026" / "A supplement on your list stopped on ..." |
| `illness` | a day log tag `sore_throat`, `congestion`, `cough`, `fever`, `nausea` or `stomach_upset` on the day or the two days before (stress, low mood, soreness and pain are left out: they have other usual causes) | the earliest such day | "You logged a sore throat on Nov 7, 2026" |
| `hard_training` | the day before: a strength set, a `hard_session` tag, or a workout of 60 minutes or more | that day | "You logged a strength session on ..." / "You logged a hard session on ..." / "A 75 minute workout on ..." |

A stored event active on the day gives "You logged {phrase} on {start}", with the phrases
feeling unwell, a point in your cycle, a new goal, a change of scale, travel, a change of medicine,
hard training, something out of the ordinary.

Not derived, and why:

- Travel from a time-zone change: samples carry no time zone, so the data cannot show one. Travel
  is the person's own `travel` event.
- Cycle phase from temperature or flow: no checked figure places a phase. `cycle_phase` is the
  person's own event.
- Season, altitude and vaccines (the proposal's "expected swing"): no checked figures; left out.

On `GET /api/context` each derived piece is read under its own source's grant: goals under `body`,
medicines under `labs`, the day log under `checkins`, the strength log and workouts under
`activity`. A viewer without that grant gets nothing derived from it.

## The context state

`ContextLoader` (server-internal, own data only) builds a `ContextState` for one person and day,
passed to the rules as `RuleContext.Context` (empty when nothing applies):

- `Active`: stored and derived context active on the day, newest first.
- `Logged`: every day of the last 128 days with a day log entry, and that day's tags.
- `HardTrainingDays`: days of the last 14 with hard training (strength set, hard session tag, a
  workout of 60 minutes or more, or a stored `hard_training` event).
- `Strength`: Epley estimates of the strength sets of the last 120 days.
- `Targets`, `TargetsYesterday`, `TargetsWeekAgo`: the tally of the active targets for the seven
  days ending on the day, the day before and a week before.
- `NewPatterns`: personal patterns established on the day and on none of the seven days before.

## How rules use context

When a context that explains a card is active, the card gains the line "{reason}, which can explain
readings like these." and steps down one severity (watch to notice, clinician to watch; notice
stays notice). Its evidence gains `context: { kind, since, source, detail }` and, when the severity
moved, `demotedFrom`. With no context active a rule behaves exactly as before. The first kind in
each list that is active is used.

| Rule | Explained by | Demoted |
| --- | --- | --- |
| `hrv_low_run`, `rhr_elevated` | illness, hard training, travel, cycle phase, medication change | yes |
| `illness_pattern` | illness, cycle phase, travel, hard training | yes |
| `sleep_debt` | illness, travel | yes |
| `activity_dip` | illness, travel | notice already |
| `weight_trend` | scale change, illness, cycle phase, medication change, new goal | notice already |
| `spo2_low` | illness, travel | never: a clinician card keeps its tier and is only annotated |
| `early_signs` | illness, cycle phase, travel, hard training | notice already |
| `cold_starting` | cycle phase, travel | yes |
| `yesterday_last_night` | illness, travel | notice already |

Illness also silences rules where a missed day is expected: `target_slipping`, `short_sleep_diet`,
`recovery_strain`, `strength_on_track` and `mood_deficit` stay quiet while illness is active.

## Rules

All nine read context, the day log or the targets. None waits for the 28-day gate of the engine
(each returns no metrics from `MetricsUsed`, except `recovery_strain`, which needs the HRV baseline)
and each checks its own minimum.

| Kind | Fires when | Quiet when | Severity | Source or label |
| --- | --- | --- | --- | --- |
| `yesterday_last_night` | a tag yesterday among `alcohol`, `late_caffeine`, `late_meal`, `hard_session` (or hard training from the record), and last night's sleep, HRV or resting heart rate below its `p10_28` or above its `p90_28`, with 21 or more nights in the baseline | no tag; an ordinary night; under 21 nights | notice | the proposal's 21-night floor is a starting point; late caffeine with a short night quotes Gardiner 2023 |
| `target_slipping` | a day target missed on 4 or more of the last 7 days that have an answer, and not the day before; or on a Sunday a week target missed this week and the week before | illness; already slipping yesterday; on a Sunday, the target `target_week` speaks about | notice | 4 of 7 and two weeks are the proposal's, a starting point |
| `early_signs` | 2 or more of: resting heart rate above `p90_28`, HRV below `p10_28`, wrist temperature above `p90_28`, breathing rate 3 or more above `median28`; each with 21 or more nights of baseline; no cold-like symptom in the last 3 days | one signal; a cold-like symptom (then `cold_starting` speaks) | notice | 2 signals is a starting point; breathing figure Natarajan 2021; sentence quotes Mason 2022 |
| `cold_starting` | a `sore_throat`, `congestion`, `cough` or `fever` tag on the day or the two days before, and 1 or more of the same signals | no symptom; no signal | watch | detection figures Mason 2022; the 3-day window is a starting point |
| `short_sleep_diet` | an active `lose_fat` or `recomposition` goal, 5 or more nights recorded in the last 7, under 420 minutes on 4 or more, and fewer than 4 in the 7 nights ending the day before | no such goal; illness; already true the day before | watch | 7 hours Watson 2015; "4 of 7" and "5 nights" are starting points |
| `recovery_strain` | hard training yesterday or the day before, and HRV last night below `p25_28` | illness; HRV inside the band; no hard training in 2 days | notice | the 2-day window and the sentence are Stanley 2013 |
| `strength_on_track` | a `lose_fat` or `recomposition` goal on its weekly check day (the weekday it started, from two weeks in), a lift with two estimated sets since the goal began, the latest in the last 7 days, at 97 % or more of the first | illness; another weekday; falling below 97 %; no set in a week | notice | 97 % and 7 days are starting points; Epley as in `docs/body-design.md` |
| `mood_deficit` | a fat-loss goal on its weekly check day, the weekly weight rate below zero, check-ins on 21 or more of the last 28 days (7 or more in each half), and the last 14 days' mean mood 1 or more below the 14 before | illness; weight not coming down; too few check-ins | watch | 1 point, two weeks and 21 check-ins are the proposal's, a starting point |
| `personal_pattern` | a pattern established today and on none of the 7 days before (see "Patterns") | nothing new | notice | our own floor; always "a pattern, not proof" |

Breathing rate up is never a card on its own. It is one signal inside `early_signs` and
`cold_starting`: a rise alone says little, and most ill people in the source showed none.

Cycle and illness act as context for other rules through the `cycle_phase` and `illness` kinds
above.

Sentences, with the evidence each card carries:

- `yesterday_last_night`: "You logged alcohol yesterday, and last night your resting heart rate was
  66 bpm, above your usual range (52 bpm to 60 bpm). Nights like this are what your patterns compare
  over time." With late caffeine and a short night it adds "A review of trials found caffeine cut
  total sleep by about 45 minutes, and coffee still cost sleep when drunk up to 8.8 hours before bed
  (Gardiner 2023)." Evidence: tag, day, readings `[{ metric, value, p10, p90 }]`.
- `target_slipping`: "\"Protein\" was missed on 4 of the last 6 days that have an answer. What got
  in the way? If it does not fit, change it." or "\"Lifting days\" was missed this week and the week
  before. What got in the way? If it does not fit, change it." Evidence: targetId, label, period,
  missedDays and knownDays, or weeksMissed.
- `early_signs`: "Your readings look unusual for you last night: resting heart rate is up and HRV is
  down. A drink, stress, travel or a hard day can look the same, so it is a pattern worth watching.
  In one study of ring wearers, readings like these picked up 82 % of infections but also flagged
  37 % of people who were not ill (Mason 2022)." Evidence: one `{ value, usual }` per signal metric,
  signals, day.
- `cold_starting`: "You logged a sore throat on Oct 28, 2026, and last night resting heart rate is
  up. Together these can fit the start of a cold. It is a pattern worth watching: an easier day
  helps, and if you feel very unwell or it gets worse, talk to a clinician." Evidence: signal
  metrics, symptom, symptomDay, signals.
- `short_sleep_diet`: "Sleep was under 7 hours on 4 of the last 7 nights while you are working on
  losing fat. Sleep researchers' consensus for adults is 7 or more hours a night." Evidence:
  shortNights, nights, thresholdMinutes.
- `recovery_strain`: "HRV was below your usual range last night, a day after hard training. That is
  expected: a review found the heart's recovery takes 24 to 48 hours after threshold-intensity
  sessions and at least 48 hours after high-intensity ones (Stanley 2013)." Evidence: hrv, p25,
  trainingDay.
- `strength_on_track`: "Your estimated best squat is 102.5 kg, against 100 kg when your goal to lose
  fat started: strength is rising while you lose fat." ("holding" at or below the first estimate.)
  Evidence: lift, firstKg, firstDay, latestKg, latestDay.
- `mood_deficit`: "Mood at check-ins averaged 3.5 over the last two weeks, against 5 the two weeks
  before, while your weight has been coming down. It is a pattern worth watching. If it stays low,
  an easier pace or a talk with someone may help." Evidence: recentMean, previousMean, days,
  weeklyRateKg.
- `personal_pattern`: "A new pattern in your own data. " followed by the pattern sentence.
  Evidence: tag, metric, nightsWith, nightsWithout, medianWith, medianWithout, difference, overlap.

## Patterns

For each day log tag seen in the last 120 days, and for each overnight reading (`sleep_stage`
minutes asleep, `hrv_rmssd_ms`, `hrv_sdnn_ms`, `resting_hr_bpm`, `wrist_temp_delta_c`):

1. Tag days are the logged days from 120 days before the reference day up to the day before it.
   The night after day D is the daily figure of D + 1 (a night belongs to the day it ended).
2. "With" nights follow a logged day carrying the tag. "Without" nights follow a logged day without
   it. A day with nothing in the day log is unknown and never counts as "without", the same rule
   the tally uses. A day with no figure the night after is left out of both.
3. Medians, and the 25th and 75th percentiles ("range") of each group, by the same R-7 percentile
   as the baselines.
4. Difference = median with minus median without.
5. Noise line = half the interquartile range of the "without" nights.
6. Overlap = the share of "with" nights that sit inside the "without" nights' 25th to 75th
   percentile.
7. Overlaps named in the sentence: "with" nights whose tag day was a Friday or Saturday, and those
   whose tag day or night fell in logged illness or travel or on a day with an illness symptom tag.
8. Rank test: a two-sided Mann-Whitney U test between the "with" and "without" nights, by the
   normal approximation with the tie correction and a continuity correction of a half.
9. Established when there are at least 8 "with" nights and 16 "without" nights, the difference is
   larger than the noise line (and not zero), and the rank test's |z| is at least 2.58 (two-sided
   p below 0.01). When every value is the same the test has nothing to go on and the pattern is not
   established.

The noise line asks whether the difference is big enough to matter; the rank test asks whether it
is bigger than the nights would throw up by chance with no effect behind the tag. The noise line
alone let too many chance differences through at these counts, so both must hold.

The 8 and 16 are the proposal's own floor: no published standard exists for a minimum count in one
person's data. The 120 days, the half interquartile range and the 2.58 are ours. All are starting
points.

Patterns are worked out on every read (two queries: the day log's days and tags, never the text,
and the overnight figures). That is cheap, so nothing is cached. Every pattern says "A pattern in
your own data, not proof."

## Read-backs

- Sleep: the stored `sleep_stage` samples, night by night. A night belongs to the local day it
  ended. Of several apps or devices the one with the most minutes asleep is used (the daily summary
  rule). Bedtime is the first sample's start, wake time the last sample's end, in-bed minutes the
  span between them. Stage minutes merge overlapping samples of a stage. A night with no light, deep
  or REM minutes has `hasStages` false and null stage minutes.
- Workouts: the stored workouts by start, with duration from start and end, distance, energy and
  average and maximum heart rate as the phone sent them. `group` is `strength` when the type names
  strength, weight, functional or core; `mixed` for HIIT, interval, cross, circuit or mixed; else
  `cardio`. Weeks start on Monday.

## Systems

| Key | Name | Readings | Lab markers |
| --- | --- | --- | --- |
| `heart` | Heart and fitness | resting_hr_bpm, hrv_rmssd_ms, hrv_sdnn_ms, vo2max, respiratory_rate_bpm, spo2_pct, blood_pressure_sys, blood_pressure_dia, steps, exercise_min | lab.apob, lab.ldl, lab.hdl, lab.ldl_apob_ratio, lab.total_cholesterol, lab.remnant_cholesterol, lab.tc_hdl_ratio, lab.hscrp |
| `sleep` | Sleep | sleep_stage, oura_sleep_score, wrist_temp_delta_c | |
| `metabolism` | Metabolism | blood_glucose_mmol, active_energy_kcal, energy_intake_kcal, protein_g | lab.triglycerides, lab.tg_hdl_ratio, lab.fructosamine, lab.uric_acid, lab.albumin, lab.alp, lab.ggt, lab.creatinine, lab.egfr |
| `hormones` | Hormones | | lab.total_testosterone, lab.free_testosterone, lab.estradiol, lab.progesterone, lab.shbg, lab.cortisol, lab.tsh, lab.free_t3, lab.free_t4 |
| `body` | Body | weight_kg, body_fat_pct, waist_cm, lean_mass_kg | lab.vitamin_d, lab.ferritin, lab.hemoglobin, lab.hematocrit |
| `cycle` | Cycle | menstrual_flow, basal_body_temp_c | |

Per reading: the latest daily figure of the last 120 days, the latest baseline's `median28`, `p25`
and `p75` once `n28 >= 14` (`usual`), and the band of the latest value against it. A metric with no
figure and no baseline is left out. Per lab marker: the latest and previous confirmed value and the
flag as reported. Cards: the stored insights of the last 7 days (30 on the detail page), not
dismissed, whose metrics touch the system.

State line, built by code: "One card worth a look." or "{n} cards worth a look." when watch or
clinician cards are open, then the first reading outside its usual range ("Resting heart rate is
above your usual range." plus "{n} more reading(s) ... outside it too."), else a flagged lab
("LDL cholesterol came back high on Oct 21, 2026."), else "Readings are inside your usual range.",
else "Not enough days yet for a usual range." when something is there, else "No readings yet."

Access: each reading is read under its metric's own category, lab markers under `labs`, cards
under `insights`. A system is listed when the viewer may read at least one of its readings'
categories, or its labs, or it has a card the viewer may see. The answer is a 404 when the viewer
may read no category of the person at all. `GET /api/trends` is unchanged.

## Weekly review

One Monday to Sunday week. `weekOf` may be any day in it; without it the review covers the week
ending today when today is Sunday, else the last complete week. A week starting after today is a
400. `complete` is true once the Sunday is today or past.

| Section | Grant | Built from |
| --- | --- | --- |
| `targets` | `body`, then each target's input under its own grant | the tally of the active targets for the seven days ending Sunday. A day target is kept when met on more than half its known days, missed otherwise, unknown with no known day; a week target by its weekly verdict |
| `goals` | `body`; the goal's metric under its own category | each active goal's metric over the week: first, last, change, count, and a sentence |
| `sleep` | `sleep` | nights recorded, the median minutes asleep, the week before's median, nights of 7 hours or more |
| `standouts` | each metric's own category; null when none is shared | resting heart rate, HRV, breathing rate, sleep, steps and exercise minutes whose week median differs from the 28 days before by more than half their interquartile range (a starting point), needing 3 days in the week and 14 before |
| `insights` | `insights` | the week's cards, not dismissed |
| `patterns` | `insights` | patterns established as of the Sunday |

A section the viewer may not read is `null`, never empty, so a client can tell "not shared" from
"nothing this week".

## Defaults

Every number below is a starting point, not a published threshold.

| Name | Value | Where |
| --- | --- | --- |
| `ContextKinds.OpenEndedDays` | 14 days | an open event counts this long |
| `ContextDerivation.NewGoalDays` | 14 days | new goal |
| `ContextDerivation.MedicationChangeDays` | 14 days | medicine start or stop |
| `ContextDerivation.SymptomDays` | 3 days | symptom tags |
| `ContextDerivation.LongWorkoutMinutes` | 60 min | hard training from a workout |
| `EarlySignals.MinBaselineNights` | 21 nights | early signs, cold starting, yesterday to last night |
| `EarlySignsRule.MinSignals` | 2 | early signs |
| `TargetSlippingRule.MinMissedDays` | 4 of 7 | target slipping |
| `ShortSleepDietRule.MinShortNights`, `MinNights` | 4, 5 | short sleep during a diet |
| `StrengthOnTrackRule.HoldingShare`, `RecentDays` | 97 %, 7 days | strength on track |
| `MoodDeficitRule.MinDrop`, `MinDays`, `MinDaysPerHalf` | 1 point, 21, 7 | mood during a deficit |
| `PatternMath.WindowDays` | 120 days | patterns |
| `PatternMath.MinNightsWith`, `MinNightsWithout` | 8, 16 | patterns |
| `PatternMath.NoiseLineIqrShare` | 0.5 | patterns |
| `PatternMath.MinRankTestZ` | 2.58 | patterns |
| `WeeklyReviewService.StandoutIqrShare` | 0.5 | weekly standouts |

Left out of this package: the proposal's longer "recovery strain" (heart rate up 5 or HRV down half
an SD for 5 days) and the "expected swing" for season, altitude and vaccines, for want of checked
figures; the alcohol sizes from Pietila 2018, which are not in the checked list.

## API

Every route needs a bearer token and is rate limited like the rest of `/api`. `personId` defaults
to the caller. Days are `yyyy-MM-dd` in the target person's time zone; times are ISO 8601 UTC;
values are metric; JSON is camelCase. Null fields are sent as `null`, never left out.

Errors are `application/problem+json` with a `traceId`:

- A read for a person who does not exist, or who has not shared what the route needs: 404, title
  "Not found", detail "No such person, or they have not shared this with you." (for
  `/api/systems/{key}` also an unknown key: "No such system or person, or they have not shared this
  with you."). Never a 403.
- A write or delete on an event that is not the caller's: 404, detail "No such context event.".
- A failed check: 400, title "Validation failed", the message below as the detail.
- A field that fails the request record (missing `kind`, a note over 500 characters): the
  framework's 400 validation problem with an `errors` object keyed by field.

### Shared shapes

```
InsightDto = {
  "id": "uuid", "day": "2026-11-09", "kind": "rhr_elevated",
  "severity": "notice" | "watch" | "clinician",
  "metrics": ["resting_hr_bpm"], "evidence": { ... , "bandDistance": 1.2 },
  "text": "string", "generated": false,
  "shownAt": "2026-11-10T07:01:02Z" | null, "dismissedAt": null, "reaction": null | "helpful" | "knew" | "wrong",
  "metricNames": ["Resting heart rate"]
}
```

(The same `InsightDto` as `docs/insights-design.md`. A card that context explains carries
`evidence.context = { "kind", "since", "source", "detail" }` and, when stepped down,
`evidence.demotedFrom`.)

```
TrendPointDto = { "day": "2026-11-09", "value": 57 }        // plus "min", "max" when the day has them
PersonalPatternDto = {
  "tag": "alcohol", "tagLabel": "Alcohol",
  "metric": "resting_hr_bpm", "metricName": "Resting heart rate", "unit": "bpm",
  "nightsWith": 9, "nightsWithout": 31,
  "medianWith": 62, "medianWithout": 58, "difference": 4,
  "rangeWith": { "low": 60, "high": 65 }, "rangeWithout": { "low": 56, "high": 59 },
  "overlap": 0.22, "noiseLine": 1.5,
  "weekendNights": 6, "contextNights": 0,
  "sentence": "On 9 nights after alcohol, against 31 without, your resting heart rate ran 4 bpm higher (60 to 65 bpm on those nights, 56 to 59 bpm on the rest). 6 of the 9 were Friday or Saturday nights. A pattern in your own data, not proof."
}
```

`unit` is `min` for `sleep_stage`. `difference` is with minus without: negative means lower
(for sleep, less). `overlap` is 0 to 1.

### Context

| Method | Path | Body | Returns |
| --- | --- | --- | --- |
| GET | `/api/context?from?&to?&personId?` | | `ContextDto` |
| POST | `/api/context` | `SaveContextEventRequest` | `ContextEventDto` (200) |
| PATCH | `/api/context/{id}` | `UpdateContextEventRequest` | `ContextEventDto` |
| DELETE | `/api/context/{id}` | | 204 |

Grant: `insights` for the list; derived items each need their own source's grant (see "Derived
context"). Writes are the caller's own. Every write drops the Today cache. Audit:
`context.saved` and `context.deleted`, with the kind only.

```
SaveContextEventRequest = {
  "kind": "illness",                 // required
  "startedOn": "2026-11-08",         // optional, default today
  "endedOn": "2026-11-10",           // optional
  "note": "Streaming cold"           // optional, at most 500 characters
}

UpdateContextEventRequest = {        // every field optional; a missing field keeps its value
  "kind": "travel",
  "startedOn": "2026-11-08",
  "endedOn": "2026-11-12",
  "note": "",                        // "" clears the note
  "reopen": true                     // true clears endedOn (wins over endedOn)
}

ContextEventDto = {
  "id": "uuid",
  "kind": "illness",
  "label": "Illness",
  "startedOn": "2026-11-08",
  "endedOn": "2026-11-10" | null,
  "source": "user" | "rule",
  "note": "Streaming cold" | null,   // only the person themself gets it back
  "active": true                     // counts today (an open event: 14 days from its start)
}

ActiveContextDto = {
  "kind": "illness",
  "label": "Illness",
  "since": "2026-11-08",
  "until": "2026-11-10" | null,
  "source": "user" | "rule" | "derived",
  "reason": "You logged a sore throat on Nov 8, 2026",
  "eventId": "uuid" | null,          // set for a stored event
  "detail": "sore_throat" | null     // derived: the tag, "started" or "stopped", the goal kind, "strength", "hard_session" or "long_workout"
}

ContextDto = {
  "day": "2026-11-10",               // today in the person's time zone; "active" is for this day
  "from": "2026-10-12",              // default 29 days before "to"
  "to": "2026-11-10",                // default today
  "active": [ActiveContextDto],      // newest first
  "events": [ContextEventDto]        // events overlapping from..to, newest start first
}
```

Kind labels: Illness, Cycle phase, New goal, Scale change, Travel, Medication change, Hard
training, Other.

Checks:

| Check | Message |
| --- | --- |
| kind | "kind must be one of: illness, cycle_phase, new_goal, scale_change, travel, medication_change, hard_training, other." |
| startedOn more than 60 days ahead | "startedOn cannot be more than 60 days ahead." |
| startedOn more than 5 years ago | "startedOn cannot be more than 5 years ago." |
| endedOn before startedOn | "endedOn cannot be before startedOn." |
| note over 500 characters | "A note is at most 500 characters." |
| a 501st event | "At most 500 context events. Remove one first." |
| list with from after to | "from must not be after to." |
| list over 366 days | "A list covers at most 366 days." |

### Patterns

| Method | Path | Returns |
| --- | --- | --- |
| GET | `/api/patterns?personId?` | `PatternsDto` |

Grant: `insights`.

```
PatternsDto = {
  "day": "2026-11-10",               // patterns as of today
  "from": "2026-07-13",              // the window start, 120 days back
  "windowDays": 120,
  "minNightsWith": 8,
  "minNightsWithout": 16,
  "note": "A pattern in your own data, not proof.",
  "patterns": [PersonalPatternDto],  // established only, strongest first (difference over noise line)
  "building": [                      // tags seen whose counts are below a floor, most tagged nights first
    { "tag": "late_meal", "tagLabel": "Late meal", "nightsWith": 3, "nightsWithout": 40 }
  ]
}
```

A tag with enough nights but no difference past the noise line, or one the rank test cannot tell
from chance, is in neither list.

### Sleep read-back

| Method | Path | Returns |
| --- | --- | --- |
| GET | `/api/sleep?from?&to?&personId?` | `SleepNightsDto` |

Grant: `sleep`. Default the last 14 nights, at most 92 days; "from must not be after to." and "A
read-back covers at most 92 days." are 400s.

```
SleepNightsDto = {
  "from": "2026-10-28",
  "to": "2026-11-10",
  "nights": [{
    "day": "2026-11-09",                 // the day the night ended
    "bedtime": "2026-11-08T23:00:00Z",
    "wakeTime": "2026-11-09T06:30:00Z",
    "inBedMinutes": 450,
    "asleepMinutes": 435,                // the same figure as the daily summary
    "awakeMinutes": 15 | null,           // null when the night has no stages
    "lightMinutes": 120 | null,
    "deepMinutes": 120 | null,
    "remMinutes": 195 | null,
    "unspecifiedMinutes": null | 450,    // asleep without a stage; null when none
    "hasStages": true
  }]                                     // oldest first; nights with no sleep left out
}
```

### Workouts read-back

| Method | Path | Returns |
| --- | --- | --- |
| GET | `/api/workouts?from?&to?&personId?` | `WorkoutsDto` |

Grant: `activity`. Default the last 28 days, at most 92.

```
WorkoutsDto = {
  "from": "2026-10-14",
  "to": "2026-11-10",
  "workouts": [{
    "id": "uuid",
    "type": "running",                   // as the phone sent it
    "group": "strength" | "cardio" | "mixed",
    "day": "2026-11-09",                 // local day of the start
    "startAt": "2026-11-09T09:00:00Z",
    "endAt": "2026-11-09T09:45:00Z",
    "durationMinutes": 45,
    "distanceM": 7200 | null,
    "energyKcal": 480 | null,
    "avgHr": 150 | null,
    "maxHr": 171 | null
  }],                                    // oldest first
  "weeks": [{
    "weekOf": "2026-11-09",              // Monday
    "count": 2, "minutes": 105,
    "strength": 1, "cardio": 1, "mixed": 0
  }]                                     // only weeks with a workout, oldest first
}
```

### Systems

| Method | Path | Returns |
| --- | --- | --- |
| GET | `/api/systems?personId?` | `SystemsDto` |
| GET | `/api/systems/{key}?personId?` | `SystemDto`, with `recent` and `history` filled and 30 days of cards |

Keys: `heart`, `sleep`, `metabolism`, `hormones`, `body`, `cycle` (any case).

```
SystemsDto = {
  "day": "2026-11-10",
  "systems": [SystemDto],             // in key order above; systems with nothing shared left out
  "labs": {                           // null without the labs grant
    "latestPanelOn": "2026-10-21" | null,
    "panels": 3,
    "markers": 22,                    // confirmed results on the latest panel
    "outsideRange": 2                 // of those, flagged high or low
  }
}

SystemDto = {
  "key": "heart",
  "name": "Heart and fitness",
  "state": "One card worth a look. Resting heart rate is above your usual range.",
  "readings": [{
    "metric": "resting_hr_bpm",
    "displayName": "Resting heart rate",
    "unit": "bpm",                    // "min" for sleep_stage
    "category": "vitals",
    "latestDay": "2026-11-10" | null,
    "latestValue": 66 | null,
    "usual": { "median28": 57.5, "p25": 56.5, "p75": 58.5, "days": 28 } | null,
    "band": "below" | "inside" | "above" | null,
    "recent": [TrendPointDto] | null  // last 28 days on /api/systems/{key}; null on the list
  }],
  "labs": [{
    "metric": "lab.ldl",
    "displayName": "LDL cholesterol",
    "unit": "mg/dL",
    "value": 160,
    "collectedOn": "2026-10-21",
    "flag": "low" | "high" | "normal" | "none",
    "previousValue": 171 | null,
    "previousOn": "2026-07-20" | null,
    "history": [{ "collectedOn": "2026-07-20", "value": 171, "flag": "high" }] | null  // up to 6, oldest first, detail only
  }],
  "insights": [InsightDto]            // newest first
}
```

### Weekly review

| Method | Path | Returns |
| --- | --- | --- |
| GET | `/api/review/weekly?weekOf?&personId?` | `WeeklyReviewDto` |

404 when the person does not exist or has shared nothing with the viewer. "weekOf cannot be in the
future." is a 400.

```
WeeklyReviewDto = {
  "weekOf": "2026-11-02",             // Monday
  "weekEnd": "2026-11-08",            // Sunday
  "complete": true,
  "targets": {                        // null without the body grant
    "kept":    [ReviewTargetDto],
    "missed":  [ReviewTargetDto],
    "unknown": [ReviewTargetDto]      // nothing known, or the input is not shared
  } | null,
  "goals": [{                         // null without the body grant
    "goalId": "uuid", "slot": "main", "kind": "lose_fat", "title": "Lose fat",
    "metric": "weight_kg" | null, "unit": "kg" | null,
    "first": 82.4 | null, "last": 81.9 | null, "change": -0.5 | null, "readings": 2,
    "summary": "82.4 kg to 81.9 kg over 2 readings, 0.5 kg down."
  }] | null,
  "sleep": {                          // null without the sleep grant
    "nights": 7,
    "medianMinutes": 455 | null,
    "previousMedianMinutes": 448 | null,
    "nightsSevenHoursOrMore": 7,
    "summary": "7 nights recorded, a median of 7 h 35 min; 7 of them 7 hours or more. The week before had a median of 7 h 28 min. Sleep researchers' consensus for adults is 7 or more hours a night."
  } | null,
  "standouts": [{                     // null when none of the standout metrics' categories is shared
    "metric": "resting_hr_bpm", "displayName": "Resting heart rate", "unit": "bpm",
    "weekMedian": 63, "usualMedian": 57, "difference": 6, "direction": "up" | "down",
    "summary": "Resting heart rate ran higher than the four weeks before: a median of 63 bpm against 57 bpm."
  }] | null,
  "insights": [InsightDto] | null,    // null without the insights grant
  "patterns": [PersonalPatternDto] | null   // established as of the Sunday; null without the insights grant
}

ReviewTargetDto = {
  "targetId": "uuid", "label": "Sleep", "period": "day" | "week",
  "metDays": 6, "knownDays": 7,       // for a week target: days that added to it, and known days
  "met": true | false | null
}
```

Goal summaries: "No number is set for this goal. The targets show how the week went.", "Not
shared with you.", "No readings this week.", "One reading this week: {value}.", "{first} to {last}
over {n} readings, {change}." Units follow the target person's unit system.

## What this package does not do

- It does not diagnose. "This can fit the start of a cold" is the limit, and readings alone say
  "unusual for you".
- It does not claim causes from patterns: "on nights after drinks your heart rate ran higher",
  never "alcohol raises your heart rate".
- It stores nothing derived: derived context, patterns, systems, read-backs and the review are
  worked out on read.
- It sends nothing off the host.
- It does not let one person write another's context.
