# Body and goals design

This package is the data and the API behind the Progress tab. A person can hold a main and a
secondary goal, set daily and weekly targets and see a tally of them, keep a day log of tagged
entries, list the medicines and supplements they take, log one top set per lift per day, type in
weight, body fat and waist, and read all of it back in one call. Five rules turn the same data
into insight cards.

It is a record and some arithmetic. It does not count calories (the one exception is the numbers
of a photo the person confirmed, `docs/photo-design.md`), it does not plan training and it does
not advise on medicines. Every number on the Progress tab is worked out on read from daily
summaries; nothing derived is stored.

The sources behind the guidance figures quoted in the copy are in `docs/body-research.md`.

## Tables

Migration `0013_body` adds `person.height_cm` (double, null) and five tables. Every table hangs
off `person` with `ON DELETE CASCADE`. Free text is encrypted with `IEncryptionService`; tags and
substance flags are plain so targets and rules can count them without decrypting anything.

`goal`

| Column | Notes |
| --- | --- |
| id, person_id | |
| slot | `main` or `secondary` (check) |
| kind | varchar 32, one of the goal kinds below; checked in the service, not in SQL |
| title_cipher | text null, encrypted; the person's own title |
| metric_key | varchar 64 null; the metric the goal is measured by |
| start_value, target_value, pace_per_week | double null |
| started_on, target_date | date; target_date null |
| status | `active`, `done` or `dropped` (check), default `active` |
| created_at, updated_at, ended_at | ended_at null while active |

Unique partial index on `(person_id, slot) WHERE status = 'active'`: one active goal per slot.
Index on `(person_id, started_on DESC)`.

`daily_target`

| Column | Notes |
| --- | --- |
| id, person_id | |
| goal_id | null; `ON DELETE SET NULL` |
| label | varchar 48 |
| kind | `metric`, `journal` or `strength` (check) |
| metric_key | null; set for a metric target |
| tag | varchar 32 null; set for a journal target |
| comparator | `at_least` or `at_most` (check), default `at_least` |
| value | double |
| period | `day` or `week` (check), default `day` |
| active | bool, default true |
| sort_order | smallint |
| created_at, updated_at | |

Index on `(person_id, sort_order)`. Nothing here is encrypted.

`journal_entry`

| Column | Notes |
| --- | --- |
| id, person_id | |
| at | timestamptz; when it happened |
| day | date; the local day of `at` in the person's time zone |
| kind | `food`, `drink`, `symptom`, `training`, `sleep` or `note` (check) |
| text_cipher | bytea null, encrypted |
| tags | text[], plain, default empty |
| portion | null, or `small`, `usual`, `large` (check) |
| source | `chip`, `typed`, `assistant`, `checkin` or `photo` (check, `photo` since 0022), default `typed` |
| message_id | uuid null; the assistant message an entry came from |
| nutrition_basis | null, or `estimate` or `label` (check, since 0022); where the numbers came from |
| energy_kcal, protein_g, carbs_g, fat_g | numeric(7,1) null (since 0022); a confirmed photo's numbers |
| created_at | |

Indexes on `(person_id, day DESC)` and on `(message_id)` where it is not null.

`medication`

| Column | Notes |
| --- | --- |
| id, person_id | |
| kind | `medication` or `supplement` (check) |
| detail_cipher | text, not null, encrypted: brand, product, dose, schedule note and ingredients as one JSON value |
| generic | bool null |
| schedule | `daily`, `weekly` or `as_needed` (check), default `daily` |
| substances | text[], plain, default empty; worked out by the server on every save |
| started_on, stopped_on | date null |
| created_at, updated_at | |

Index on `(person_id, created_at DESC)`.

`strength_set`

| Column | Notes |
| --- | --- |
| id, person_id | |
| day | date |
| lift | varchar 32 |
| weight_kg | double, check 0 to 1000 |
| reps | smallint, check 1 to 100 |
| rpe | null |
| created_at | |

Unique on `(person_id, day, lift)`: one set per lift per day. Nothing here is encrypted.

Migration `0014_metric_reseed` seeds the whole metric catalogue again from `MetricSeed.cs`
(idempotent, never removes a key), so an install that ran `0004` gains `waist_cm`,
`lean_mass_kg`, `bone_mass_kg`, `body_water_kg`, `energy_intake_kcal`, `protein_g`, `carbs_g` and
`fat_g`. All eight are in the `body` share category. The four nutrition metrics are read from the
phone's health store when another app logs them there. Migration `0015_body_summary_backfill` is
described under "Daily figures".

## Access

Reads take `(viewerPersonId, targetPersonId)` and the grant is checked in the repository. A
person always reads their own. Writes are always the caller's own; no route writes for someone
else.

| Thing | Share category | Without a grant |
| --- | --- | --- |
| Goals | `body` | 404 |
| Targets (the list) | `body` | 404 |
| Tally | `body` for the targets and goals, then each target's input under its own category | 404 without `body`; a target whose input is not shared comes back unknown |
| Day log | `checkins` | 404 |
| Medicines and supplements | `labs` (they explain blood work) | 404 |
| Strength log | `activity` | 404 |
| Progress | `body` | 404 |
| Typed measurements | own only on these routes | someone else's are read through the usual sample and summary endpoints |

Inside the tally and Progress, each piece is read under the viewer's grant for that piece:

- A metric (a target's metric, a goal's metric, a series) is read under the metric's own category.
  Weight, body fat, waist, VO2 max and the nutrition metrics are `body`; `sleep_stage` is `sleep`;
  steps are `activity`.
- A journal target reads the day log under `checkins`; a strength target reads the strength log
  under `activity`.
- A target whose input the viewer may not read has `current` null, `met` null, a `week` of seven
  nulls, `metDays` 0 and `knownDays` 0. It never shows as met or not met.
- A goal whose metric the viewer may not read has `paceStatus` `unknown` and the summary "Not
  shared with you."
- `strength` on Progress is an empty list unless `activity` is shared too.
- `suggestions` on Progress is an empty list for anyone but the person themself.

## Goals

One main and one secondary goal at a time. The kinds and their plain names:

| Kind | Name | Measured by, when the goal names no metric |
| --- | --- | --- |
| `lose_fat` | Lose fat | `weight_kg` |
| `gain_muscle` | Gain muscle | `weight_kg` |
| `recomposition` | Lose fat and gain muscle | `body_fat_pct` |
| `maintain` | Hold steady | `weight_kg` |
| `keep_strength` | Keep strength | nothing |
| `fitness` | Get fitter | `vo2max` |
| `sleep_better` | Sleep better | `sleep_stage` (minutes asleep) |
| `improve_labs` | Improve blood work | nothing |
| `custom` | Your goal | nothing |

- Starting a goal ends any active goal in the same slot as `dropped`, in the same transaction.
- `startedOn` defaults to today in the person's time zone.
- The title is optional except for `custom`. Without one the goal shows the name of its kind.
- A goal keeps its slot. To move it, end it and start a new one.
- Ending sets `done` or `dropped` and `endedAt`. Ending a goal that has already ended changes
  nothing and returns it. Its targets stay and can be kept, edited or removed.
- A list for a viewer shows active goals first, then by slot, then newest start first.

Checks, each answered with the sentence shown:

| Check | Message |
| --- | --- |
| slot | "slot must be one of: main, secondary." |
| kind | "kind must be one of: ..." (the nine kinds) |
| metricKey not in the catalogue | "Unknown metric '{key}'." |
| title over 80 characters | "A title is at most 80 characters." |
| `custom` without a title | "A goal of your own needs a title." |
| startValue, targetValue or pacePerWeek not a finite number | "{name} must be a number." |
| start, target and a non-zero pace all given, pace pointing the other way | "pacePerWeek points away from the target." |
| targetDate on or before the start | "targetDate must be after the start." |
| update with a different slot | "A goal keeps its slot. End it and start a new one." |
| end with any other status | "status must be done or dropped." |

Audit: `goal.saved` and `goal.ended`, with slot, kind and status only.

## Suggestions

Progress offers five goals a person could pick, with starting numbers worked out from their own
readings. They are shown only to the person themself and nothing is saved until one is chosen.
The weight used is the trend weight, or the latest weigh-in when there is no trend.

| Kind | Metric and start value | Suggested targets | Why text |
| --- | --- | --- | --- |
| `lose_fat` | `weight_kg`, the weight rounded | Protein, Lifting days, Sleep | "A review written for lean, trained people suggests losing about 0.5 to 1 % of body weight a week to hold on to muscle. For you that is {low} to {high} a week." plus the protein sentence. Without a weight: "... Add a weight reading to see what that is for you." |
| `gain_muscle` | `weight_kg`, the weight rounded | Protein, Lifting days, Sleep | The protein sentence plus "Regular lifting and enough sleep do the rest; the strength log shows whether it is working." |
| `keep_strength` | none | Lifting days, Protein | "Log one top set per lift and the estimated best shows whether strength is holding while other things change." |
| `sleep_better` | `sleep_stage` | Sleep, No late caffeine | The sleep sentence plus "Noting late caffeine in the journal lets the patterns page compare those nights with the rest." |
| `maintain` | `weight_kg`, the weight rounded | Lifting days, Sleep | "Holding steady is a goal too. The trend line smooths out day to day swings, so you can see drift early." |

The protein sentence: "In a pooled analysis of training studies, eating more protein stopped
adding muscle at about 1.6 g per kg of body weight a day." The sleep sentence: "Sleep
researchers' consensus for adults is 7 or more hours a night."

The suggested targets:

| Label | Target | Figure |
| --- | --- | --- |
| Protein | metric `protein_g`, at least, per day | weight x 1.6 g, rounded to the nearest 5 g; offered only when a weight exists |
| Lifting days | strength, at least 2, per week, unit "days" | a plain default with no source behind it |
| Sleep | metric `sleep_stage`, at least 420 min, per day | 7 hours |
| No late caffeine | journal tag `late_caffeine`, at most 0, per week, unit "entries" | |

The low and high of the loss range are 0.5 % and 1 % of the weight, written in the reader's
units.

## Targets and the tally

A target is a label, a kind, a comparator (`at_least` or `at_most`), a value and a period (`day`,
or `week` meaning the last seven days). At most 12 per person. A target can point at one of the
person's goals; deleting or ending the goal leaves the target in place.

| Kind | Counts | Unit in the DTO |
| --- | --- | --- |
| `metric` | the metric's daily figure | the metric's unit (`min` for `sleep_stage`) |
| `journal` | day log entries carrying the tag | "entries" |
| `strength` | sets logged | "lifts" for a day target, "days" for a week target |

Checks:

| Check | Message |
| --- | --- |
| label empty or over 48 characters | "A label is 1 to 48 characters." |
| kind, comparator, period | "kind must be one of: metric, journal, strength." and the same form for the other two |
| value below zero | "value must be zero or more." |
| metric target with a tag | "A metric target has no tag." |
| metric not in the catalogue | "Unknown metric '{key}'." |
| a lab marker, or a metric with no daily summary rule | "'{key}' has no daily figure to set a target on." |
| journal target with a metric | "A day log target has no metric." |
| journal target whose tag is not a built-in tag | "Unknown tag '{tag}'." |
| strength target with a metric or tag | "A strength target has no metric or tag." |
| goalId that is not the caller's goal | "No such goal." |
| a thirteenth target | "At most 12 targets. Remove one first." |

On create, `sortOrder` defaults to the number of targets the person already has and `active`
defaults to true. On update, a missing `sortOrder` keeps the stored one; a missing `active` is
taken as true. Audit: `target.saved` and `target.deleted`, with kind and period only. Every
write drops the Today cache.

### The tally

The tally covers the active targets over seven days: today, in the target person's time zone, and
the six days before. The rule that matters: nothing logged is unknown, not a pass.

What a day is worth:

- Metric: that day's daily summary value. No row for the day is unknown.
- Journal: the number of entries that day carrying the tag. A day with no entry of any kind is
  unknown, so a "no alcohol" day only counts when something was written that day.
- Strength: the number of sets that day. A day without a set is 0 once there is a set somewhere
  in the seven days. With no set at all in the seven days every day is unknown, so an "at most"
  target never passes on nothing logged.

A day target gets a verdict per day:

- `at_least`: true as soon as the value reaches the target. Otherwise false once the day is
  final, and unknown while it is still open.
- `at_most`: false as soon as the value passes the target. Otherwise true once the day is final,
  and unknown while it is still open.
- A day is final when it is not today. Today is also final at once for metrics whose value is
  decided when it arrives: `sleep_stage`, `hrv_rmssd_ms`, `hrv_sdnn_ms`, `resting_hr_bpm`,
  `weight_kg`, `body_fat_pct`, `waist_cm`, `oura_sleep_score` and `oura_readiness`. Running totals
  such as steps or protein stay open today.
- `current` is today's value and `met` is today's verdict. `week` holds the seven verdicts, oldest
  first, today last.

A week target gets one verdict for the seven days together, in `met`, and `current` is the number
it was judged on:

- Metric: the average of the days that have a value, not their sum. It needs values on at least 3
  of the 7 days; with fewer, `met` is unknown.
- Journal: the sum of the counts. `at_least` is true once reached, unknown when no day had any
  entry, false otherwise. `at_most` is false once passed, and true only when something was logged
  on at least 3 of the 7 days; otherwise unknown.
- Strength: the number of days with a set. Unknown when there is no set in the seven days.
- For a week target the entries in `week` do not say "met". Each says whether that day added to
  the count: true when the day's value is above zero, false when the day has a value of zero, null
  when the day is unknown.

`metDays` is the number of true entries in `week` and `knownDays` the number that are not null.

## Day log (journal)

The code calls this both the day log and the journal; the table is `journal_entry` and the route
is `/api/journal`. An entry is a kind, optional short text in the person's own words, coarse tags
and, for food and drink, a portion. No weighing of food. The one place amounts come from is a
photo the person confirmed (`docs/photo-design.md`): such a food or drink entry carries its
numbers (see "Numbers from a photo" below).

Built-in tags, by kind:

| Kind | Tags |
| --- | --- |
| `food` | high_protein, vegetables, fruit, whole_grains, sweets, fried_food, takeaway, late_meal, skipped_meal, home_cooked, swap |
| `drink` | alcohol, caffeine, late_caffeine, sugary_drink, water |
| `symptom` | headache, sore_throat, congestion, cough, fever, fatigue, nausea, stomach_upset, soreness, pain, stress, low_mood |
| `training` | lifted, cardio, hard_session, rest_day, walk |
| `sleep` | late_night, nap, poor_sleep, good_sleep |
| `note` | none |

A tag is a plain fact about the day, never an amount. Its label is the tag with underscores as
spaces and a capital first letter ("late_meal" becomes "Late meal"). A person may also use a tag
of their own on an entry; only built-in tags can be the subject of a target.

- Tags are trimmed, lowercased, spaces and hyphens turned into underscores, duplicates dropped.
- `at` defaults to now. `day` is the local day of `at` in the person's time zone.
- `source` is `chip` when the entry has no text and `typed` when it has. The values `assistant`
  and `checkin` are written by server-internal paths (an assistant capture, the evening check-in)
  that have no route in this package; an assistant save can be undone by its message id.
- A list covers `from` to `to`, newest first, at most 500 entries. `to` defaults to today in the
  target person's time zone and `from` to 13 days before `to` (14 days in all).
- Text is encrypted. Kind, tags, portion, source and the numbers are plain.

Checks:

| Check | Message |
| --- | --- |
| kind | "kind must be one of: food, drink, symptom, training, sleep, note." |
| text over 500 characters | "An entry is at most 500 characters." |
| neither text nor tags | "Nothing to save." |
| portion on anything but food or drink | "Only food and drink have a portion." |
| portion value | "portion must be one of: small, usual, large." |
| `at` more than an hour ahead | "at cannot be in the future." |
| more than 8 tags | "At most 8 tags per entry." |
| a tag that is not 1 to 32 letters, digits or underscores | "Tags are 1 to 32 letters, digits or underscores." |
| list with `from` after `to` | "from must not be after to." |

Audit: `journal.created` (kind, tag count, source, and the basis when the entry has numbers) and
`journal.deleted` (kind). Never the text and never a number.

### Numbers from a photo

`POST /api/journal` may carry `nutrition: { basis: "estimate" | "label", energyKcal?, proteinG?,
carbsG?, fatG? }`. The entry's source is then `photo`. Each number is also written as a sample
(`energy_intake_kcal`, `protein_g`, `carbs_g`, `fat_g`) under a per-person source named "Photo"
(created on first use), so the day's totals, the Protein target and Progress count it. The entry,
its samples, the day's summary and the Today cache change in one transaction. Deleting the entry
(directly, or by undoing the assistant message it came from) removes its samples and recomputes
the day. A household member sees the numbers with the entry, through the `checkins` grant; the
samples are read like any other intake sample, under `body`.

| Check | Message |
| --- | --- |
| nutrition on anything but food or drink | "Only food and drink have numbers." |
| basis | "basis must be one of: estimate, label." |
| no number | "Give at least one number, or leave nutrition out." |
| energy outside 0 to 5000 kcal | "Energy must be between 0 and 5000 kcal." |
| protein outside 0 to 400 g | "Protein must be between 0 and 400 g." |
| carbohydrate outside 0 to 1000 g | "Carbohydrate must be between 0 and 1000 g." |
| fat outside 0 to 500 g | "Fat must be between 0 and 500 g." |

## Medicines and supplements

A list of what a person takes: kind (`medication` or `supplement`), brand, product, dose, a
schedule (`daily`, `weekly`, `as_needed`, default `daily`), a schedule note, whether it is a
generic, ingredients (name, amount, unit) and start and stop dates. The list is a record. Nothing
here is advice to start, stop or change a dose.

- Brand, product, dose, schedule note and ingredients are encrypted together. Kind, generic,
  schedule, the dates and the substance tags are plain.
- An item is `active` on a day when it has started (or has no start date) and has not stopped (or
  has no stop date). The list shows active items first, then by kind, then by product.
- A row whose detail cannot be decrypted lists with the product "Unreadable item".
- At most 60 items and 40 ingredients per item.

Substance tags are worked out on every save, on the host, by whole-word keyword match over the
product, the brand and the ingredient names, lowercased. No model, no lookup, nothing leaves the
host. The dose and the schedule note are not scanned. A tag explains a reading; it never advises
on a medicine.

| Tag | Matches |
| --- | --- |
| `testosterone` | testosterone, androgel, cypionate, enanthate |
| `statin` | statin, atorvastatin, rosuvastatin, simvastatin, pravastatin, lovastatin, pitavastatin, lipitor, crestor, zocor |
| `thyroid` | levothyroxine, synthroid, liothyronine, armour thyroid |
| `biotin` | biotin, vitamin b7 |
| `iron` | iron, ferrous, ferric |
| `vitamin_d` | vitamin d, vitamin d3, vitamin d2, d3, cholecalciferol, ergocalciferol |
| `creatine` | creatine |
| `glp1` | semaglutide, ozempic, wegovy, tirzepatide, mounjaro, zepbound, liraglutide, saxenda, victoza |
| `beta_blocker` | metoprolol, atenolol, propranolol, bisoprolol, carvedilol, nebivolol |
| `estrogen` | estradiol, estrogen, ethinyl |
| `metformin` | metformin, glucophage |
| `zinc` | zinc |
| `magnesium` | magnesium |
| `omega_3` | omega-3, omega 3, omega3, fish oil, epa, dha |
| `vitamin_b12` | b12, b-12, cobalamin, methylcobalamin, cyanocobalamin |
| `caffeine` | caffeine |
| `melatonin` | melatonin |

Checks:

| Check | Message |
| --- | --- |
| kind, schedule | "kind must be one of: medication, supplement." and the same form for schedule |
| no product | "Say what the product is." |
| product, brand or ingredient name over 80 characters | "{name} is at most 80 characters." |
| dose or schedule note over 120 characters | "{name} is at most 120 characters." |
| ingredient unit over 12 characters | "ingredient unit is at most 12 characters." |
| ingredient without a name | "Every ingredient needs a name." |
| ingredient amount below zero | "The amount of {name} must be zero or more." |
| more than 40 ingredients | "At most 40 ingredients." |
| stoppedOn before startedOn | "stoppedOn cannot be before startedOn." |
| a sixty-first item | "At most 60 items. Remove one you no longer take." |

The request record also carries length attributes that are checked first and do not all agree
with the service: product 120, dose 80, ingredient unit 16. The tighter of the two applies, so a
product is at most 80 characters, a dose at most 80 and an ingredient unit at most 12.

Audit: `medication.saved` and `medication.deleted`, with the kind only. The audit row says that
the list changed, never what is on it.

## Strength

One top set per lift per day: lift, weight in kilograms, reps and an optional RPE. Enough to see
whether strength is holding, without becoming a gym tracker.

- The lift name is trimmed, lowercased, spaces and hyphens turned into underscores. The common
  lifts have display names: squat, deadlift, bench_press, overhead_press, row, pull_up. Any other
  lift shows as its name with underscores as spaces and a capital first letter.
- `day` defaults to today in the person's time zone. Saving the same lift on the same day replaces
  the set and keeps its id.
- Weight is stored and sent in kilograms. A bodyweight set is weight 0.
- A list covers `from` to `to`. `to` defaults to today and `from` to 119 days before (120 days in
  all). Sets are grouped per lift, common lifts first and the rest alphabetically, oldest set
  first within a lift.
- `estimatedMaxKg` is the Epley estimate: weight x (1 + reps / 30). One rep is the weight itself.
  It is null for a bodyweight set and for more than 12 reps. It is only used to compare a person's
  own sets over time.
- `bestEstimatedMaxKg` is the highest estimate in the window. `changePct` is the last estimate
  against the first, as a percentage, and needs two sets that have an estimate.

Checks:

| Check | Message |
| --- | --- |
| lift | "lift is 1 to 32 letters, digits or underscores." |
| weight | "weightKg must be between 0 and 1000." |
| reps | "reps must be between 1 and 100." |
| rpe | "rpe must be between 1 and 10." |
| day after today | "day cannot be in the future." |
| day too old | "day cannot be more than 5 years ago." |

Audit: `strength.saved` and `strength.deleted`, with the lift.

## Typed measurements

Weight, body fat and waist can be typed in, and so can the readings of a home device: blood
pressure, pulse, temperature, glucose and oxygen (a confirmed photo of a device display arrives
here too, `docs/photo-design.md`). Each reading is stored as an ordinary sample under a
per-person source named "Typed in" (created on first use), so it flows through the same daily
summaries, trends and rules as a reading from a scale.

| Metric | Accepted range |
| --- | --- |
| `weight_kg` | 20 to 400 kg |
| `body_fat_pct` | 2 to 75 % |
| `waist_cm` | 30 to 250 cm |
| `blood_pressure_sys` | 50 to 300 mmHg |
| `blood_pressure_dia` | 20 to 200 mmHg |
| `heart_rate_bpm` | 20 to 250 bpm |
| `body_temp_c` | 30 to 45 C |
| `blood_glucose_mmol` | 1 to 40 mmol/L |
| `spo2_pct` | 50 to 100 % |

- Values are sent in the metric's own unit. `at` defaults to now.
- Saving or deleting a reading recomputes that day's daily summary and drops the Today cache in
  the same transaction.
- The list is the caller's own typed readings of one metric from the last two years, newest
  first. Readings from a device are not in it.
- Height is not a typed measurement. It is `heightCm` on the person's profile, set through the
  profile update: 90 to 250 cm, null leaves the stored height as it is and 0 clears it ("Height
  must be between 90 and 250 cm."). BMI, lean mass index and waist to height need it.

Checks:

| Check | Message |
| --- | --- |
| metricKey | "metricKey must be one of: weight_kg, body_fat_pct, waist_cm, blood_pressure_sys, blood_pressure_dia, heart_rate_bpm, body_temp_c, blood_glucose_mmol, spo2_pct." |
| value outside the range | "{Metric name} must be between {min} and {max} {unit}." |
| `at` more than an hour ahead | "at cannot be in the future." |
| `at` more than 730 days back | "at cannot be more than two years ago." |

Audit: `measurement.created` and `measurement.deleted`, with the metric.

## Daily figures

Everything in the tally, on Progress and in the rules reads `daily_summary`, one row per person,
metric and local day.

| Metrics | The day's figure |
| --- | --- |
| `weight_kg`, `body_fat_pct`, `waist_cm`, `lean_mass_kg`, `bone_mass_kg`, `body_water_kg`, `vo2max` | the mean of the day's readings |
| `energy_intake_kcal`, `protein_g`, `carbs_g`, `fat_g` | the sum of the day's records |
| `sleep_stage` | minutes asleep, unit `min` |

New readings get their daily row at ingest. Migration `0015_body_summary_backfill` writes the
rows for readings that were already stored: average, minimum, maximum and count per local day, in
the person's time zone (UTC when the zone is unknown), for `body_fat_pct`, `waist_cm`,
`lean_mass_kg`, `bone_mass_kg`, `body_water_kg` and `vo2max`. It never overwrites an existing
row. It does not cover the four nutrition metrics.

A metric with no daily summary rule, and any lab marker, cannot be the subject of a target.

## Body maths

Plain arithmetic on a person's own readings. The smoothing and the outlier screen are engineering
choices; none of it is a medical threshold.

| Figure | How |
| --- | --- |
| BMI | weight in kg / (height in m) squared |
| Fat mass | weight x body fat % / 100 |
| Lean mass | weight x (1 - body fat % / 100) |
| Lean mass index | lean mass / (height in m) squared |
| Waist to height | waist / height |

Screening of weigh-ins. With fewer than 4 readings nothing is set aside. Otherwise a reading is
set aside when it is more than 6 % away from the median of up to 3 readings on each side of it
(itself excluded): someone else on the scale, or a bag in hand. Readings set aside are counted in
`readingsSetAside` and left out of the series, the trend and the rate.

Trend. An exponentially weighted line over the kept readings. The first reading seeds it. Each
later reading pulls the line by 10 % for a one-day gap; for a gap of g days the pull is
1 - 0.9^g, so the line does not lag after a holiday.

Weekly rate. The least-squares slope of the kept readings over the 28 days ending today, times 7.
It needs at least 6 readings spanning at least 14 days; otherwise it is null. Negative is down.
The same calculation gives the pace of any other goal metric.

Lean share of loss. Of the weight lost between two points that both have a body fat reading, the
share that was lean mass, from 0 to 1. Null unless at least 2.0 kg has gone. A bathroom scale
estimates body fat, so this is a rough guide.

Units. Everything is stored and sent in metric units. Sentences built on the server use the
reader's unit system: kilograms become pounds and centimetres become inches for an imperial
reader, minutes are written as "7 h 30 min", and a change reads "0.4 kg down" or "1.1 lb up".

## Progress

`GET /api/progress` puts it together for one person over a window of 120 days ending today (the
target person's time zone). Sentences use the viewer's unit system.

Series, in this order, each left out when it has no points: `weight_kg`, `body_fat_pct`,
`waist_cm`, `lean_mass_kg`, `vo2max`, `protein_g`, `energy_intake_kcal`. For weight, `points` are
the kept readings, `trend` is the smoothed line, `latest` is the last kept reading and `change`
is the last trend value less the first. For the others `change` is the last point less the first
and `trend` is null.

The body snapshot:

| Field | From |
| --- | --- |
| weightKg, lastWeighedOn | the latest kept weigh-in |
| trendWeightKg | the trend today |
| weeklyRateKg, weeklyRatePct | the weekly rate, and the rate as a percentage of the trend weight |
| bodyFatPct, waistCm | the latest reading in the window |
| fatMassKg, leanMassKg | the trend weight and the latest body fat |
| bmi, leanMassIndex, waistToHeight, heightCm | the above and the profile height |
| leanShareOfLoss | between the first and last body fat readings in the window, with the trend weight on those days |
| readingsSetAside | weigh-ins the screen left out |

Goal progress. For each active goal:

- The metric is the goal's own, or the usual one for its kind (see "Goals").
- `current` is the trend weight for a weight goal; the latest reading for the sparse metrics
  (`body_fat_pct`, `waist_cm`, `lean_mass_kg`, `bone_mass_kg`, `body_water_kg`, `vo2max`); and
  the mean of the last seven days for anything else.
- `actualPacePerWeek` is the weekly rate of that metric.
- `fraction` is how far from start to target, from 0 to 1. With no start value the start is taken
  as the current value.
- `paceStatus` is one of `reached`, `on_track`, `ahead`, `behind`, `wrong_way`, `unknown`:
  - `reached` when the target has been passed in the direction of travel, or is within 0.5 % of
    the target value.
  - `unknown` when there is no target, no reading or no pace.
  - `behind` when the pace is flat: no more than 0.05 % of the current value a week.
  - `wrong_way` when the pace points away from the target.
  - With a planned pace: `ahead` at 125 % of it or more, `on_track` at 60 % or more, otherwise
    `behind`. With no planned pace, any pace towards the target is `on_track`.
  - A hold-steady goal (target equal to start) is `reached` within 1 % of the target; outside
    that it is `on_track` when drifting back towards it and `wrong_way` otherwise.
- `projectedDate` is given for `on_track`, `ahead` and `behind` when the pace is not zero and the
  target is between 0 and 156 weeks away at that pace.
- `summary` is built from these sentences: "{current} now, aiming for {target}." or "{current}
  now."; "You are there."; "Holding level over the last four weeks."; "Moving {change} a week over
  the last four weeks."; "Not enough readings in the last four weeks to judge the pace."; "That is
  faster than the {planned} a week you planned."; "That is slower than the {planned} a week you
  planned."; "That is away from the target for now."; "At this pace you would get there around
  {month and year}."
- A goal with no metric: "No number is set for this goal. The targets show how the days are
  going." A goal with no readings: "No readings yet. Once a few arrive this shows where you
  stand."

`targets` is the tally, `strength` is the strength log for the window and `suggestions` is the
list under "Suggestions".

## Rules

Five rules join the Interpret rule set (`docs/insights-design.md`). They read a body context
loaded for the person and the interpreted day: active goals with where they stand (main first),
screened weigh-ins and waist readings for 120 days, the trend weight, the weekly rate, the tally
of the week ending on that day, and the substance tags of items active on that day. Weigh-ins and
waist readings do not arrive daily, so these rules do not wait for a 28-day baseline. All five
are severity `notice`. No rule in this package reads the substance tags yet.

A reading is fresh on the day it arrives and the day after.

| Kind | Fires when | Quiet when | Sentence |
| --- | --- | --- | --- |
| `loss_rate_fast` | an active `lose_fat` or `recomposition` goal, a fresh weigh-in, and the weekly rate is a loss of more than 1 % of the trend weight | no such goal, no weekly rate or trend, no fresh weigh-in | "Weight is coming down by about {rate} a week, which is {share} % of your body weight. A review written for lean, trained people puts the pace that keeps muscle at about 0.5 to 1 % a week." |
| `goal_reached` | a goal with a start value and a different target value stands at `reached`, and did not seven days earlier | the goal was already there a week ago; a hold-steady goal; a goal with no start value | "You reached your goal to {goal name}: {current} now, against a target of {target}. You can end it, or set the next one." |
| `goal_off_course` | a goal stands at `wrong_way` on its weekly check day: the weekday the goal started, from two weeks in | any other day; no pace | "Your goal to {goal name} is moving away from its target: {metric name} is {change} a week over the last four weeks. A slower pace or a different target is a fair answer too." |
| `target_week` | the interpreted day is a Sunday and a day target was kept on 5 or more days, or missed on 5 or more, or a week target was met | any other day; fewer than 5 either way and no week target met | kept: "\"{label}\" held on {n} of the {known} days this week that have an answer." missed: "\"{label}\" was missed on {n} of the {known} days this week that have an answer. If it does not fit, change it." week: "\"{label}\" was met this week." |
| `waist_height` | a fresh waist reading, a height on the profile, and waist / height of 0.5 or more | BMI of 35 or more (trend weight, or the latest weigh-in, with the profile height); no height | "Your waist is {ratio} of your height. NICE describes {0.5 to 0.59 as increased central adiposity, or 0.6 or more as high central adiposity}, and suggests keeping your waist to less than half your height." |

Notes:

- `goal_reached` and `goal_off_course` each speak about the first goal that qualifies, main goal
  first.
- `target_week` picks the kept target with the most days, then the missed target with the most
  misses, then the first week target that was met. Unknown days count for neither.
- `waist_height` fires without a BMI check when there is no weight to work one out from.
- `weight_trend` (Package 3) now stays quiet while any active goal is measured by `weight_kg`,
  and writes its amount in the person's units.
- Evidence: `loss_rate_fast` carries ratePerWeekKg, sharePerWeekPct, trendWeightKg;
  `goal_reached` goalId, current, target; `goal_off_course` goalId, pacePerWeek, current;
  `target_week` label, metDays, knownDays, kept; `waist_height` waistCm, heightCm, ratio.

## API

Every route needs a bearer token and is rate limited like the rest of `/api`. `personId` defaults
to the caller. Dates are `yyyy-MM-dd`, times are ISO 8601 UTC, and values are metric.

```
GoalDto = { id, slot, kind, title, metricKey?, startValue?, targetValue?, pacePerWeek?,
            startedOn, targetDate?, status, endedAt? }
SaveGoalRequest = { slot, kind, title?, metricKey?, startValue?, targetValue?, pacePerWeek?,
                    startedOn?, targetDate? }
EndGoalRequest = { status: "done" | "dropped" }

TargetDto = { id, goalId?, label, kind, metricKey?, tag?, comparator, value, period, active,
              sortOrder, unit }
SaveTargetRequest = { goalId?, label, kind, metricKey?, tag?, comparator, value, period,
                      active?, sortOrder? }
TargetTallyDto = { target: TargetDto, current?, met?: bool | null,
                   week: (bool | null)[7] (oldest first, today last), metDays, knownDays }
GoalTallyDto = { day, goals: GoalDto[], targets: TargetTallyDto[] }

JournalEntryDto = { id, at, day, kind, text?, tags: string[], portion?, source, messageId?,
                    nutrition? }
CreateJournalEntryRequest = { kind, text?, tags?, portion?, at?, nutrition? }
JournalNutritionDto = { basis: "estimate" | "label", energyKcal?, proteinG?, carbsG?, fatG? }
JournalTagGroupDto = { kind, tags: [{ tag, label }] }

MedicationIngredientDto = { name, amount?, unit? }
MedicationDto = { id, kind, brand?, product, dose?, scheduleNote?, generic?, schedule,
                  ingredients: MedicationIngredientDto[], substances: string[],
                  startedOn?, stoppedOn?, active }
SaveMedicationRequest = { kind, brand?, product, dose?, scheduleNote?, generic?, schedule?,
                          ingredients?, startedOn?, stoppedOn? }

StrengthSetDto = { id, day, lift, liftName, weightKg, reps, rpe?, estimatedMaxKg? }
SaveStrengthSetRequest = { lift, weightKg, reps, rpe?, day? }
StrengthLiftDto = { lift, liftName, sets: StrengthSetDto[], bestEstimatedMaxKg?, changePct? }

CreateMeasurementRequest = { metricKey, value, at? }
MeasurementDto = { id, metricKey, value, unit, at }

BodyPointDto = { day, value }
BodySeriesDto = { key, displayName, unit, points: BodyPointDto[], trend?: BodyPointDto[],
                  latest?, change? }
BodySnapshotDto = { weightKg?, trendWeightKg?, weeklyRateKg?, weeklyRatePct?, bodyFatPct?,
                    fatMassKg?, leanMassKg?, bmi?, leanMassIndex?, waistCm?, waistToHeight?,
                    leanShareOfLoss?, heightCm?, readingsSetAside, lastWeighedOn? }
GoalProgressDto = { goal: GoalDto, unit?, current?, fraction?, actualPacePerWeek?,
                    paceStatus: "on_track" | "ahead" | "behind" | "wrong_way" | "reached" | "unknown",
                    projectedDate?, summary }
SuggestedTargetDto = { label, kind, metricKey?, tag?, comparator, value, period, unit }
GoalSuggestionDto = { kind, title, why, metricKey?, startValue?, targets: SuggestedTargetDto[] }
ProgressDto = { day, goals: GoalProgressDto[], targets: TargetTallyDto[], body: BodySnapshotDto,
                series: BodySeriesDto[], strength: StrengthLiftDto[],
                suggestions: GoalSuggestionDto[] }
```

| Method | Path | Body | Returns |
| --- | --- | --- | --- |
| GET | `/api/goals?personId?&includeEnded?` | | `GoalDto[]`; active only unless `includeEnded=true` |
| POST | `/api/goals` | `SaveGoalRequest` | `GoalDto` |
| PUT | `/api/goals/{id}` | `SaveGoalRequest` | `GoalDto` |
| POST | `/api/goals/{id}/end` | `EndGoalRequest` | `GoalDto` |
| GET | `/api/targets?personId?` | | `TargetDto[]` in sort order, inactive ones included |
| GET | `/api/targets/tally?personId?` | | `GoalTallyDto`: active goals and active targets |
| POST | `/api/targets` | `SaveTargetRequest` | `TargetDto` |
| PUT | `/api/targets/{id}` | `SaveTargetRequest` | `TargetDto` |
| DELETE | `/api/targets/{id}` | | 204 |
| GET | `/api/journal?from?&to?&personId?` | | `JournalEntryDto[]` newest first |
| GET | `/api/journal/tags` | | `JournalTagGroupDto[]`, the kinds that have built-in tags |
| POST | `/api/journal` | `CreateJournalEntryRequest` | `JournalEntryDto` |
| DELETE | `/api/journal/{id}` | | 204 |
| GET | `/api/medications?personId?` | | `MedicationDto[]` |
| POST | `/api/medications` | `SaveMedicationRequest` | `MedicationDto` |
| PUT | `/api/medications/{id}` | `SaveMedicationRequest` | `MedicationDto` |
| DELETE | `/api/medications/{id}` | | 204 |
| GET | `/api/strength?from?&to?&personId?` | | `StrengthLiftDto[]` |
| POST | `/api/strength` | `SaveStrengthSetRequest` | `StrengthSetDto` |
| DELETE | `/api/strength/{id}` | | 204 |
| GET | `/api/body/measurements?metricKey` | | `MeasurementDto[]`, the caller's own, newest first |
| POST | `/api/body/measurements` | `CreateMeasurementRequest` | `MeasurementDto` |
| DELETE | `/api/body/measurements/{metricKey}/{id}` | | 204 |
| GET | `/api/progress?personId?` | | `ProgressDto` |

Notes:

- A create answers 200 with the saved object, not 201.
- Errors are `application/problem+json` with a `traceId`.
- A read for someone who has not shared the category, or for a person who does not exist, is a
  404 with the title "Not found" and the detail "No such person, or they have not shared this
  with you." It is never a 403, so nothing leaks.
- A write or delete on an id that is not the caller's own is a 404 with the detail "No such
  goal.", "No such target.", "No such journal entry.", "No such item." (medicines), "No such set."
  or "No such measurement."
- A failed check in a service is a 400 with the title "Validation failed" and the message from
  the tables above as the detail.
- A request that fails the record's own attributes (a missing required field, a string over its
  length) is answered by the framework's 400 validation problem, with an `errors` object keyed by
  field, before the service runs.

## What this package does not do

- It does not count calories or weigh food. The day log holds words, tags and a coarse portion.
  The nutrition metrics are what another app logged on the phone, plus the numbers of a photo the
  person confirmed (`docs/photo-design.md`); nothing else writes them.
- It does not advise on medicines. The list is a record and a substance tag only explains a
  reading.
- It is not a gym tracker: one top set per lift per day, and the estimated best is only for
  comparing a person's own sets over time.
- It sets no medical thresholds of its own. The smoothing, the screen and the pace bands are
  engineering choices, and the two guidance ranges it quotes say where they come from.
- It stores nothing derived. The trend, the rates, the tally and the progress of a goal are
  worked out on read.
- It sends nothing off the host. Substance tagging is a keyword match on the server.
- It does not let one person write for another. Every write is the caller's own.
- It shows a suggestion only to the person themself and saves nothing until they choose it.
