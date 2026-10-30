# Labs design (Package 4)

A monthly panel arrives as a photo or screenshot of the Rythm results screen. The phone reads it, the person confirms each value against the cropped image, and only confirmed values are stored. Manual entry is the same review sheet with empty fields. The web app offers manual entry only.

## Tables

| Table | Columns |
| --- | --- |
| `lab_report` | id, person_id, provider (rythm, manual, other), collected_on date, received_on date null, source_image_cipher bytea null, image_sha256 null, notes_cipher null, created_at, confirmed_at null |
| `lab_result` | id, report_id, person_id, metric_key (lab.*), value, unit, flag (low, high, normal, none) as reported, raw_text (what the scanner read), confirmed bool, reference_low null, reference_high null (the lab's own range when visible), created_at; unique (report_id, metric_key) |
| `lab_marker_alias` | metric_key, alias text; seeded from the Rythm display names and common synonyms (ApoB, Apolipoprotein B; hs-CRP, hsCRP, High sensitivity CRP; Vitamin D, 25-OH Vitamin D; eGFR, Estimated GFR; and so on) |
| `lab_reference` | metric_key, sex (both, female, male), low, high, optimal_low null, optimal_high null, tier (watch, clinician), source_url; seeded from `docs/lab-ranges.md` |

Images and notes are encrypted with the existing `IEncryptionService`. A result row also writes a `lab.*` sample (start = collected_on at noon in the person's zone) so labs appear on Trends and are reachable by the baseline engine later.

## Endpoints

| Method | Path | Body or query | Returns |
| --- | --- | --- | --- |
| POST | `/api/labs/parse` | multipart image, or `{ text }` of OCR lines from the phone | `ParseResult = { candidates: [{ metricKey, displayName, value, unit, flag, rawText, confidence, line }], unmatchedLines: string[] }` server-side matching only, nothing stored |
| POST | `/api/labs/reports` | `{ provider, collectedOn, results: [{ metricKey, value, unit, flag?, rawText?, referenceLow?, referenceHigh? }], image?: base64 }` | `LabReportDto` confirmed in one call |
| GET | `/api/labs/reports?personId?` | | `LabReportDto[]` newest first |
| GET | `/api/labs/reports/{id}` | | report with results |
| DELETE | `/api/labs/reports/{id}` | | 204, own reports only |
| GET | `/api/labs/markers?personId?` | | per marker: display name, unit, group, sex applicability, latest value and date, previous value, population range for the person's sex, optimal range, tier, direction of good, last three values |
| GET | `/api/labs/markers/{metricKey}?personId?` | | full history for one marker plus the blurb and "what moves it" text |
| GET | `/api/labs/summary.pdf?from&to&personId?` | | clinician PDF (QuestPDF, two pages: trends table and lab table with flags) |

Category for access checks is `labs`.

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

Package 3's stubs become real: `lab_out_of_range` compares each confirmed value with `lab_reference` for the person's sex, tier from the table; `lab_trend` fires on a 15 percent move in the direction of worse across two consecutive panels for ApoB, LDL, hsCRP, fructosamine, triglycerides, ferritin, TSH and GGT. Both cards mention the two dates and values and say what to bring to a clinician.

## Labs screen

Small multiples: one tile per marker with the last six values as a sparkline, the person's own range shaded, the population range as a hairline, and the latest value large. Groups follow the Rythm panel. Tap a tile for the marker page. A single "Add results" button opens the scan flow on phones and the manual sheet on web.
