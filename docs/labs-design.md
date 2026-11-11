# Labs design (Package 4)

A monthly panel arrives as a photo or screenshot of the Rythm results screen. The phone reads it, the person confirms each value against the cropped image, and only confirmed values are stored. Manual entry is the same review sheet with empty fields. The web app offers manual entry only.

## Tables

| Table | Columns |
| --- | --- |
| `lab_report` | id, person_id, provider (rythm, manual, other), collected_on date, received_on date null, source_image_cipher bytea null, image_sha256 null, notes_cipher null, created_at, confirmed_at null; draw conditions (0017): fasting (yes, no, unknown) null, fasting_hours smallint null, draw_time time null, draw_period (morning, afternoon, evening, unknown) null, hard_training bool null, unwell bool null, conditions_defaulted bool |
| `lab_result` | id, report_id, person_id, metric_key (lab.*), value, unit, flag (low, high, normal, none) as reported, raw_text (what the scanner read), confirmed bool, reference_low null, reference_high null (the lab's own range when visible), created_at; unique (report_id, metric_key) |
| `lab_marker_alias` | metric_key, alias text; seeded from the Rythm display names and common synonyms (ApoB, Apolipoprotein B; hs-CRP, hsCRP, High sensitivity CRP; Vitamin D, 25-OH Vitamin D; eGFR, Estimated GFR; and so on) |
| `lab_reference` | metric_key, sex (both, female, male), low, high, optimal_low null, optimal_high null, tier (watch, clinician), source_url, age_split_at smallint null, older_low null, older_high null (0017; cystatin C only); seeded from `docs/lab-ranges.md` |
| `trial` | id, person_id, title_cipher, change_cipher null, started_on, read_on (on or after started_on), metric_keys text[], status (active, done, dropped), outcome_cipher null, created_at, updated_at, ended_at null (0017) |

Images and notes are encrypted with the existing `IEncryptionService`. A result row also writes a `lab.*` sample (start = collected_on at noon in the person's zone) so labs appear on Trends and are reachable by the baseline engine later.

## Endpoints

| Method | Path | Body or query | Returns |
| --- | --- | --- | --- |
| POST | `/api/labs/parse` | multipart image, or `{ text }` of OCR lines from the phone | `ParseResult = { candidates: [{ metricKey, displayName, value, unit, flag, rawText, confidence, line }], unmatchedLines: string[] }` server-side matching only, nothing stored |
| POST | `/api/labs/reports` | `{ provider, collectedOn, results: [{ metricKey, value, unit, flag?, rawText?, referenceLow?, referenceHigh? }], image?: base64 }` | `LabReportDto` confirmed in one call |
| GET | `/api/labs/reports?personId?` | | `LabReportDto[]` newest first |
| GET | `/api/labs/reports/{id}` | | report with results |
| DELETE | `/api/labs/reports/{id}` | | 204, own reports only; its samples and every undismissed card whose evidence names it (`reportId` or `previousReportId`) go too, then today is interpreted again |
| GET | `/api/labs/markers?personId?` | | per marker: display name, unit, group, sex applicability, latest value and date, previous value, population range for the person's sex, optimal range, tier, direction of good, last three values |
| GET | `/api/labs/markers/{metricKey}?personId?` | | full history for one marker plus the blurb and "what moves it" text |
| GET | `/api/labs/summary.pdf?from&to&personId?` | | clinician PDF (QuestPDF, two pages: trends table and lab table with flags) |
| PUT | `/api/labs/reports/{id}/conditions` | `DrawConditionsInput` | `LabReportDto`, own reports only (see "API") |
| GET | `/api/labs/reports/{id}/review` | | `LabReviewDto` (see "API") |
| GET | `/api/labs/timeline?personId?` | | `LabTimelineDto` |
| GET | `/api/labs/links` | | `LabLinkDto[]` |
| GET | `/api/labs/markers/{metricKey}/related?personId?` | | `RelatedReadingsDto` |
| GET, POST, PATCH, DELETE | `/api/trials`, `/api/trials/{id}` | | trials, body grant |
| GET | `/api/household/overview` | | `HouseholdOverviewDto` |

Category for access checks is `labs`. Trials, and the weight beside a draw, are `body`; a related reading follows its own category.

## Parsing

OCR runs on the phone with `google_mlkit_text_recognition` (on-device model, no network). The phone sends the recognised lines to `/api/labs/parse`, which does the matching so the rules live in one place and the web app can reuse them for pasted text:

1. Normalise each line (collapse spaces, unify decimal separators, strip thousands separators).
2. Match a marker by alias with case-insensitive prefix and token matching; prefer the longest alias.
3. Find the value as the first number after the alias; accept the next token as unit when it matches the catalogue unit or a convertible one; capture a flag token (L, H, Low, High, arrow glyphs) if present.
4. Confidence: 1.0 when alias, value and unit all match; 0.7 without unit; 0.4 when the value sits on the next line.
5. Ratios (LDL-C/ApoB, TC/HDL, TG/HDL) are computed server-side from the confirmed components when not read, and marked `derived`.

Unit conversion reuses `UnitConversion` from Package 2, extended for lab units (mmol/L to mg/dL for cholesterol, nmol/L to ng/dL for testosterone, pmol/L to pg/mL for free T3 and estradiol).

## Review sheet (client)

One row per catalogue marker applicable to the person's sex, in panel order: name, the read value in an editable field, the unit, last month's value in quiet text, and a thumbnail crop of the source line when the OCR gave bounding boxes. Rows with confidence under 0.7 are highlighted amber. The sheet cannot be saved while any required marker is empty unless the person taps "Not on this panel" for it. Confirm stores the report, the results and the encrypted image.

## Insights

Package 3's stubs become real: `lab_out_of_range` compares each confirmed value with `lab_reference` for the person's sex, tier from the table; `lab_trend` fires on a 15 percent move in the direction of worse across two consecutive panels for ApoB, LDL, hsCRP, fructosamine, triglycerides, ferritin, TSH and GGT. Both cards mention the two dates and values and say what to bring to a clinician. Lab cards speak about the newest panel by collection date and the one before it, so the order panels are entered in does not change which cards end up open.

## Labs screen

Small multiples: one tile per marker with the last six values as a sparkline, the person's own range shaded, the population range as a hairline, and the latest value large. Groups follow the Rythm panel. Tap a tile for the marker page. A single "Add results" button opens the scan flow on phones and the manual sheet on web.

## Draw conditions

When a panel is confirmed the client asks two questions, "were you fasting?" and "what time was the blood drawn?", both skippable, plus two optional yes or no taps (hard training in the days before, unwell around the draw). A Rythm panel arrives pre-filled as a morning draw after a fast of 8 or more hours (`fasting: "yes"`, `fastingHours: 8`, `drawPeriod: "morning"`, `defaulted: true`), because Rythm asks for morning testing after an 8+ hour fast; the person can change any of it. Other providers start with nothing known. When a draw time is given the period is worked out from it: morning before 12:00 local time, afternoon 12:00 to 17:59, evening from 18:00. Substances are not asked: they come from the medicines list, the tags of the items active on the draw date. How the conditions are used is in `docs/cross-reference.md`.

## Wider catalogue

Twelve markers join the 30 of the Rythm panel so a fuller panel is read in full: ALT, AST, platelets, white cell count, red cell count, MCV, HbA1c, fasting glucose, cystatin C, transferrin saturation, serum iron and TIBC (`docs/lab-ranges.md`, "Wider catalogue"). Migration 0019 reseeds the metric catalogue and 0020 the reference rows and aliases, the way 0014 delivered the body metrics, so an install that ran 0004, 0007 and 0014 gains them. Cystatin C carries an age split at 50 years; the Labs screen, the PDF and the rules read the interval for the person's age, or the wider of the two without a birth date. `GET /api/labs/markers` now lists 42 markers in panel order: metabolic gains HbA1c, fasting glucose, iron, TIBC and transferrin saturation; blood gains red cells, MCV, white cells and platelets; kidney gains cystatin C; liver gains ALT and AST.

## Lab review

`GET /api/labs/reports/{id}/review` sets one panel against the one before it (by collected date). A marker the panel before did not include is set against the latest earlier panel that did, judged with that panel's own draw conditions, and `previousOn` names its day; `no_previous` is kept for a marker never measured before. Each marker gets a verdict judged against how much that test varies by itself: `working`, `right_direction`, `no_clear_change`, `not_working` or `cannot_tell`, with the change, the reference change value, the reasons, the draw-condition and substance links that apply, preparation notes and, where a verified source gives one, a retest line. The definitions, the variation figures and their sources are in `docs/cross-reference.md`. Trials whose start falls between the two panels are listed with the verdicts of their markers.

## Trials

A trial is a single change with a planned check: a title, what changed, a start date, a planned read date (usually the next panel), and the readings expected to move (lab markers or wearable metrics). The title, what changed and the outcome are encrypted; the rest is plain so the rules can see when it is ready. Trials are read under the `body` grant. `trial_ready` fires when a panel collected after the start comes in or the read date arrives; `retest_due` when a blood trial's read date has come and no panel has been taken since it started.

## Household overview

`GET /api/household/overview` lists every member of the caller's household, the caller first, each with a short summary per category the caller may see. Each summary comes from a repository read that checks that category's grant, so a category without a grant is absent from the JSON, while a granted category with no data is present with nulls or zeros. The caller always sees their own. Cycle is not summarised.

## API

Every route needs a bearer token and is rate limited like the rest of `/api`. `personId` defaults to the caller. Dates are `yyyy-MM-dd`, times of day `HH:mm` local, timestamps ISO 8601 UTC, JSON camelCase. A read for someone who has not shared the category, or for a person or report that does not exist, is a 404 problem with the title "Not found" and the detail "No such person, or they have not shared this with you." (the older lab routes keep "No such person, or they have not shared labs with you." and "No such report."). A write on another person's report or trial is a 404 with "No such report." or "No such trial.". A failed check is a 400 with the title "Validation failed" and the message as the detail. Values are in the catalogue unit.

### Shapes

```
DrawConditionsInput = {                       // every field optional
  fasting?: "yes" | "no" | "unknown",
  fastingHours?: int 0..72,                  // not allowed with fasting "no"
  drawTime?: "HH:mm",                        // local time of day; sets drawPeriod
  drawPeriod?: "morning" | "afternoon" | "evening" | "unknown",
  hardTraining?: bool,
  unwell?: bool
}

DrawConditionsDto = {
  fasting: "yes" | "no" | "unknown",         // "unknown" when never answered
  fastingHours: int | null,                  // 8 on a Rythm default means "8 or more"
  drawTime: "HH:mm" | null,
  drawPeriod: "morning" | "afternoon" | "evening" | "unknown",
  hardTraining: bool | null,
  unwell: bool | null,
  defaulted: bool                            // true while these are the Rythm pre-fill, untouched
}

CreateLabReportRequest = {                   // unchanged fields as before, plus conditions
  provider: "rythm" | "manual" | "other",
  collectedOn: date,
  receivedOn?: date,
  results: [{ metricKey, value, unit?, flag?, rawText?, referenceLow?, referenceHigh? }],
  image?: base64,
  notes?: string,
  conditions?: DrawConditionsInput
}

LabReportDto = {
  id, personId, provider, collectedOn, receivedOn: date | null, hasImage: bool, notes?: string,
  createdAt, confirmedAt: timestamp | null,
  results: [{ id, metricKey, displayName, value, unit, flag, rawText?, derived: bool, referenceLow?, referenceHigh? }],
  conditions: DrawConditionsDto              // new
}

ConditionDifferenceDto = {
  condition: "fasting" | "drawPeriod" | "hardTraining" | "unwell" | "substance:<tag>",
  now: string | null,                        // fasting and drawPeriod: the stored answer; the others: "yes" | "no"
  then: string | null
}

ReadWithCareDto = { linkId, from, explanation, source }      // from: "condition:not_fasting" | "condition:not_morning" | "substance:<tag>"
VariationSourceDto = { cviPercent: number, source, sourceUrl, caveat: string | null }

LabReviewMarkerDto = {
  metricKey, displayName, unit,
  value: number,
  previousValue: number | null,
  previousOn: date | null,                   // the panel before, or the latest earlier panel with this marker
  change: number | null,                     // value minus previousValue, catalogue unit
  changePercent: number | null,              // against previousValue, one decimal
  rcvPercent: number | null,                 // null without a verified variation figure
  beyondRcv: bool | null,
  moved: "up" | "down" | "none" | null,      // null without a previous value
  wanted: "up" | "down" | "none",
  verdict: "working" | "right_direction" | "no_clear_change" | "not_working" | "cannot_tell",
  reasons: ("no_previous" | "no_variation_figure" | "draw_conditions_differ" | "substance_at_draw" | "no_wanted_direction")[],
  readWithCare: ReadWithCareDto[],
  variation: VariationSourceDto | null,
  prep: string[],                            // notes for the next draw
  retest: string | null                      // only where a verified source gives one
}

TakenItemDto = { id, kind: "medication" | "supplement", brand: string | null, product, substances: string[] }
WeightAtDrawDto = { day: date, kg: number }
LabReviewWeightDto = { now: WeightAtDrawDto | null, then: WeightAtDrawDto | null, changeKg: number | null }

TrialDto = {
  id, title, change?: string, startedOn: date, readOn: date, metricKeys: string[],
  status: "active" | "done" | "dropped", outcome?: string,
  createdAt, updatedAt, endedAt?: timestamp
}

LabReviewTrialDto = {
  trial: TrialDto,
  markers: [{ metricKey, verdict }],         // the trial's lab markers that are on this panel
  sharedCredit: bool                         // another trial in the same window links one of the same markers
}

LabReviewDto = {
  reportId, personId, collectedOn,
  previousReportId: uuid | null, previousCollectedOn: date | null,
  conditions: DrawConditionsDto,
  previousConditions: DrawConditionsDto | null,
  conditionsDiffer: ConditionDifferenceDto[],     // empty without a previous panel
  markers: LabReviewMarkerDto[],                  // every confirmed result on the report, panel order
  takingNow: TakenItemDto[],                      // items active on collectedOn
  takingThen: TakenItemDto[],                     // items active on previousCollectedOn
  substancesNow: string[], substancesThen: string[],
  weight?: LabReviewWeightDto,                    // absent without the body grant; the weigh-in nearest each draw within 7 days
  trials?: LabReviewTrialDto[],                   // absent without the body grant; trials started after the previous draw and on or before this one
  verdictCounts: { working: int, right_direction: int, no_clear_change: int, not_working: int, cannot_tell: int }
}

LabTimelinePanelDto = {
  reportId, collectedOn, provider,
  conditions: DrawConditionsDto,
  markerCount: int,
  weight: WeightAtDrawDto | null,            // null without the body grant or without a weigh-in within 7 days
  substances: string[]                       // tags active on collectedOn
}
LabTimelineDto = { personId, weightShared: bool, panels: LabTimelinePanelDto[] }    // oldest first

CreateTrialRequest = {
  title: string 1..80,
  change?: string up to 200,
  startedOn?: date,                          // default today in the person's zone; at most a month ahead
  readOn: date,                              // on or after startedOn, at most two years after
  metricKeys?: string[] up to 8              // catalogue keys: lab.* or wearable metrics
}
UpdateTrialRequest = {                       // only fields sent change
  title?, change?, startedOn?, readOn?, metricKeys?,
  status?: "active" | "done" | "dropped",    // done or dropped sets endedAt; active clears it
  outcome?: string up to 500                 // an empty string clears change or outcome
}

LabLinkDto = { id, from, to, direction: "same" | "opposite" | "modifies" | "none", explanation, source, sourceUrl, evidence }

RelatedReadingDto = {
  key,                                       // a metric key, "substance:<tag>" or "condition:<name>"
  kind: "lab" | "metric" | "substance" | "condition",
  displayName,
  unit?: string,
  category: string,                          // the share category of the reading ("labs" for substances and conditions)
  shared: bool,                              // false when the viewer may not see that category; no value then
  latestValue?: number, latestDate?: date,   // latest panel value, or latest daily value in the last 90 days
  links: LabLinkDto[]                        // every link between the marker and this reading
}
RelatedReadingsDto = { metricKey, displayName, related: RelatedReadingDto[] }

OverviewLabsDto = { latestCollectedOn: date | null, panels: int, outOfRange: int }   // outOfRange: flagged markers on the latest panel
OverviewBodyDto = { weightKg: number | null, weighedOn: date | null, mainGoal: string | null, activeTrials: int }   // weight from the last 28 days
OverviewValueDto = { metric, day: date | null, value: number | null, unit }          // latest daily value in the last 14 days
OverviewInsightsDto = { cards: int, topSeverity: "notice" | "watch" | "clinician" | null }   // undismissed cards of the last 7 days, learning left out
OverviewCheckinsDto = { lastAt: timestamp | null, mood: int | null }                 // last check-in of the last 30 days

HouseholdMemberOverviewDto = {
  personId, displayName, isSelf: bool,
  sharedCategories: string[],                // what the caller may see: activity, sleep, vitals, body, labs, cycle, checkins, insights
  labs?: OverviewLabsDto,                    // labs grant
  body?: OverviewBodyDto,                    // body grant
  sleep?: OverviewValueDto,                  // sleep grant; metric sleep_stage, minutes asleep
  activity?: OverviewValueDto,               // activity grant; metric steps
  vitals?: OverviewValueDto,                 // vitals grant; metric resting_hr_bpm
  insights?: OverviewInsightsDto,            // insights grant
  checkins?: OverviewCheckinsDto             // checkins grant
}
HouseholdOverviewDto = { householdId, day: date, members: HouseholdMemberOverviewDto[] }  // caller first, then by join date
```

A key with `?` is absent from the JSON when it does not apply. A key typed `X | null` is always present.

### Routes

| Method | Path | Body | Returns |
| --- | --- | --- | --- |
| POST | `/api/labs/reports` | `CreateLabReportRequest` (now with `conditions?`) | `LabReportDto` with `conditions` |
| GET | `/api/labs/reports?personId?`, `/api/labs/reports/{id}` | | `LabReportDto[]`, `LabReportDto` with `conditions` |
| PUT | `/api/labs/reports/{id}/conditions` | `DrawConditionsInput` (replaces every condition; omitted fields become unknown; `defaulted` becomes false) | `LabReportDto`; own reports only |
| GET | `/api/labs/reports/{id}/review` | | `LabReviewDto`; labs grant of the report's owner |
| GET | `/api/labs/timeline?personId?` | | `LabTimelineDto`; labs grant, weight needs body too |
| GET | `/api/labs/links` | | `LabLinkDto[]`, the catalogue (not person data) |
| GET | `/api/labs/markers/{metricKey}/related?personId?` | | `RelatedReadingsDto`; labs grant; each value gated by its own category. The key may omit the `lab.` prefix |
| GET | `/api/trials?personId?&includeEnded?` | | `TrialDto[]` newest start first; active only unless `includeEnded=true`; body grant |
| POST | `/api/trials` | `CreateTrialRequest` | `TrialDto` (200) |
| PATCH | `/api/trials/{id}` | `UpdateTrialRequest` | `TrialDto`; own only |
| DELETE | `/api/trials/{id}` | | 204; own only |
| GET | `/api/household/overview` | | `HouseholdOverviewDto`; 404 "You are not in a household." without one |

Trial checks (400, "Validation failed"): "Say what the trial is.", "title is at most 80 characters.", "change is at most 200 characters.", "outcome is at most 500 characters.", "readOn is required: the date to read the result.", "readOn cannot be before startedOn.", "readOn is at most two years after startedOn.", "startedOn is more than a month away.", "'{key}' is not a reading in the catalogue.", "At most 8 readings per trial.", "At most 20 active trials. End one first.", "status must be one of: active, done, dropped.". Draw condition checks: "fasting must be one of yes, no, unknown.", "fastingHours must be between 0 and 72.", "fastingHours goes with fasting \"yes\" or \"unknown\", not \"no\".", "drawTime must be a local time of day as HH:mm.", "drawPeriod must be one of morning, afternoon, evening, unknown.".

Audit: `lab.report.conditions` (collected date only), `trial.saved` and `trial.deleted` (status and the number of readings, never the words).

### Example: a review

```json
{
  "reportId": "0193...", "personId": "0192...", "collectedOn": "2026-12-12",
  "previousReportId": "0191...", "previousCollectedOn": "2026-08-14",
  "conditions": { "fasting": "no", "fastingHours": null, "drawTime": "15:40", "drawPeriod": "afternoon", "hardTraining": null, "unwell": null, "defaulted": false },
  "previousConditions": { "fasting": "yes", "fastingHours": 8, "drawTime": null, "drawPeriod": "morning", "hardTraining": null, "unwell": null, "defaulted": true },
  "conditionsDiffer": [
    { "condition": "fasting", "now": "no", "then": "yes" },
    { "condition": "drawPeriod", "now": "afternoon", "then": "morning" }
  ],
  "markers": [
    {
      "metricKey": "lab.ggt", "displayName": "GGT", "unit": "U/L", "value": 28, "previousValue": 40, "previousOn": "2026-08-14",
      "change": -12, "changePercent": -30, "rcvPercent": 24.7, "beyondRcv": true, "moved": "down", "wanted": "down",
      "verdict": "working", "reasons": [], "readWithCare": [],
      "variation": { "cviPercent": 8.9, "source": "Carobene 2017, Clin Chem 63(6):1141-1150 (EuBIVAS)", "sourceUrl": "https://pubmed.ncbi.nlm.nih.gov/28428356/", "caveat": null },
      "prep": [], "retest": null
    },
    {
      "metricKey": "lab.triglycerides", "displayName": "Triglycerides", "unit": "mg/dL", "value": 168, "previousValue": 120, "previousOn": "2026-08-14",
      "change": 48, "changePercent": 40, "rcvPercent": null, "beyondRcv": null, "moved": "up", "wanted": "down",
      "verdict": "cannot_tell", "reasons": ["draw_conditions_differ", "no_variation_figure"],
      "readWithCare": [{ "linkId": "not_fasting_triglycerides", "from": "condition:not_fasting", "explanation": "One to six hours after a usual meal, ...", "source": "Nordestgaard 2016, ..." }],
      "variation": null,
      "prep": ["Draw the same way each time, fasting or not, so this compares with the last panel."], "retest": null
    }
  ],
  "takingNow": [{ "id": "0190...", "kind": "supplement", "brand": null, "product": "Creatine monohydrate", "substances": ["creatine"] }],
  "takingThen": [{ "id": "0190...", "kind": "supplement", "brand": null, "product": "Creatine monohydrate", "substances": ["creatine"] }],
  "substancesNow": ["creatine"], "substancesThen": ["creatine"],
  "weight": { "now": { "day": "2026-12-13", "kg": 80.5 }, "then": { "day": "2026-08-12", "kg": 84 }, "changeKg": -3.5 },
  "trials": [{ "trial": { "id": "0192...", "title": "Oats instead of a bagel", "startedOn": "2026-08-16", "readOn": "2026-12-12", "metricKeys": ["lab.ldl", "lab.ggt"], "status": "active", "createdAt": "...", "updatedAt": "..." }, "markers": [{ "metricKey": "lab.ldl", "verdict": "cannot_tell" }, { "metricKey": "lab.ggt", "verdict": "working" }], "sharedCredit": false }],
  "verdictCounts": { "working": 1, "right_direction": 0, "no_clear_change": 0, "not_working": 0, "cannot_tell": 1 }
}
```

### Insight kinds

The rules in `docs/cross-reference.md`, "Rules", add the kinds `trial_ready`, `retest_due`, `draw_conditions_differ`, `apob_ldl_disagree`, `calculated_ldl_unreliable`, `ferritin_inflammation`, `high_ferritin_context`, `thyroid_cholesterol`, `free_testosterone_shbg`, `creatinine_muscle`, `hematocrit_testosterone`, `expected_swing` and `lab_review_ready` to `InsightDto.kind`. Their evidence carries `reportId` (and `previousReportId` where two panels are compared) or `trialId`, so a card can open the review or the trial.
