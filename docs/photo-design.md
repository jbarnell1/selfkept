# Photo design (photos read by the home model)

A person picks or takes a photo in the assistant. The server hands it to the home model once,
turns the answer into a proposal, and forgets the photo. Nothing is saved until the person has
looked at the proposal, changed what is off and pressed save. Version 0.1.10.

What a photo can be:

| Kind | What comes back | Where a confirmed result goes |
| --- | --- | --- |
| `meal` | what is on the plate, day log tags, a portion, and a rough guess at energy, protein, carbohydrate and fat | a day log food entry with its numbers |
| `nutrition_label` | the product, the serving size and the printed numbers per serving | a day log food entry with the label numbers times the servings eaten |
| `medicine_label` | kind, brand, product, dose, ingredients | the medicines form, filled in (`POST /api/medications` after the person saves) |
| `device_display` | the numbers on a scale, cuff, thermometer, meter or oximeter | typed readings (`POST /api/body/measurements`) |
| `lab_report` | the lines of the report, matched against the lab catalogue | the existing lab review sheet (`docs/labs-design.md`) |
| `other` | one or two plain sentences saying what is visible | nothing is saved; the description goes with a chat message so the assistant can answer |

## Privacy

- The photo travels from the phone to the home server and from there to the model the owner set
  under `Ai:` (the same host the assistant already uses). It never goes to the cloud lookup or to
  any other host. A cloud lookup cannot carry a photo.
- The photo is held in memory for the length of the request and is never written to disk, to the
  database, to a log or to `ai_call`. There is no photo table. The app keeps its own copy only
  while the proposal is on screen.
- The description of an `other` photo is stored only when the person sends it with a chat
  message, and then inside that message's encrypted content.
- Photos are own-only, like the rest of the assistant: no `personId` on the route, no share
  category.
- `ai_call` gets one row per model call (purposes `assistant_photo_sort` and
  `assistant_photo_read`) with latency and ok or the failure, never text. The audit action is
  `assistant.photo_analyzed` with the kind and nothing else.

## Settings

Photos are off until the owner turns them on, because not every home model can see.

| Setting | `.env` | Default | Meaning |
| --- | --- | --- | --- |
| `Ai:Vision` | `SELFKEPT_AI_VISION` | `false` | Photos are offered. Needs `Ai:Enabled` and a model that takes images. |
| `Ai:VisionModel` | `SELFKEPT_AI_VISION_MODEL` | empty | A different model for photos. Empty means `Ai:Model`. |
| `Ai:VisionTimeoutSeconds` | `SELFKEPT_AI_VISION_TIMEOUT_SECONDS` | `90` | The longest one photo call may take. A cold model loads first. |

`GET /api/assistant/status` gains `photos: bool`: true when `Ai:Vision` is on and the model
settings are usable. The app shows the photo button only then.

## The model calls

The adapter (`OpenAiCompatibleChatModel`) sends an image as an OpenAI content part:
`{"type":"image_url","image_url":{"url":"data:<media type>;base64,<bytes>"}}` next to a text
part. Photo calls set `response_format: {"type":"json_object"}`, temperature 0.1, the
interactive header, the reasoning effort setting, and are never streamed.

1. Sort (skipped when the request names the kind). The model answers
   `{"kind": ..., "description": ...}`. An unknown kind becomes `other`.
2. Read (skipped for `other`). One prompt per kind asks for that kind's JSON and nothing else.
   For a lab report the model writes out the lines it can read (`{"lines": [string]}`) and the
   existing lab parser matches them; the model never names a marker itself.

Code checks every answer before it becomes a proposal:

- The JSON is taken from the answer even when it is wrapped in a code fence. An answer that is
  not usable JSON is asked for once more; a second failure is the 422 below.
- A number outside its range is left out (null), never clamped. Text is trimmed and cut to its
  limit. Tags must be in the food tags of `JournalTags`; others are dropped.
- The description is one or two sentences, at most 300 characters, and must pass the reply
  guard's rules against diagnosis, detection and cause claims. If it does not, it is replaced by
  "A photo." and the kind stays.
- The model never decides what is saved. It only fills a form the person then reads.

Ranges:

| Field | Accepted |
| --- | --- |
| energy (kcal) | 0 to 5000 |
| protein (g) | 0 to 400 |
| carbohydrate (g) | 0 to 1000 |
| fat (g) | 0 to 500 |
| servings per container | 0.25 to 200 |

Readings on a display map to metrics. The value is converted when the display shows another
unit; a reading with a unit the table does not list is dropped.

| What the model saw | Metric | Units read | Accepted (in the metric's unit) |
| --- | --- | --- | --- |
| weight | `weight_kg` | kg, lb | 20 to 400 |
| body fat | `body_fat_pct` | % | 2 to 75 |
| systolic pressure | `blood_pressure_sys` | mmHg | 50 to 300 |
| diastolic pressure | `blood_pressure_dia` | mmHg | 20 to 200 |
| pulse | `heart_rate_bpm` | bpm | 20 to 250 |
| temperature | `body_temp_c` | C, F | 30 to 45 |
| blood glucose | `blood_glucose_mmol` | mmol/L (mg/dL only through the conversion the lab code already has) | 1 to 40 |
| oxygen | `spo2_pct` | % | 50 to 100 |

Typed measurements (`docs/body-design.md`) accept these metrics as well as the three they
already took, with the same ranges, so a confirmed reading is an ordinary sample under the
person's "Typed in" source.

## Numbers for food

Until this version the app held no amounts of food. A photo is the one place they come from:

- `label`: the numbers printed on a nutrition label, per serving, times the servings the person
  says they ate. These are read, not guessed.
- `estimate`: the model's guess from a picture of a meal. It can be well off. The app says so
  where the numbers are shown and where they are saved: "A guess from the picture. It can be
  well off, so change anything that looks wrong."

The person can change or clear every number before saving. A confirmed entry is a day log food
(or drink) entry with source `photo` and its numbers, and each number is also written as a
sample (`energy_intake_kcal`, `protein_g`, `carbs_g`, `fat_g`) under a per-person source named
"Photo", so the day's totals, the Protein target and Progress count it. Deleting the entry
removes its samples and recomputes the day. If another app also sends the same meal through
Health Connect, the day counts it twice; the day log shows where each number came from.

The assistant's own words still never carry calories or macros: the state brief leaves the
numbers out and the reply guard is unchanged.

## Migration 0022_photo

- `journal_entry.source` also accepts `photo` (the check constraint is widened).
- `journal_entry` gains `nutrition_basis varchar(16) null` (check: `estimate` or `label`),
  `energy_kcal`, `protein_g`, `carbs_g` and `fat_g` (`numeric(7,1) null`). A basis without any
  number, or a number without a basis, is refused by the service.

## API

### POST /api/assistant/photos

`multipart/form-data`:

| Part | |
| --- | --- |
| `image` | required. JPEG, PNG or WebP, told by its first bytes, at most 6 MB |
| `kind` | optional. One of the six kinds; skips the sort |
| `note` | optional, at most 300 characters, the person's own words about the photo; given to the model with the photo |

```
200 PhotoAnalysisDto = {
  kind: "meal" | "nutrition_label" | "medicine_label" | "device_display" | "lab_report" | "other",
  description: string,
  meal?: {
    text: string,                      // what is on the plate, at most 200 characters
    tags: string[], portion?: "small" | "usual" | "large",
    energyKcal?: number, proteinG?: number, carbsG?: number, fatG?: number   // the guess, whole meal
  },
  label?: {
    product?: string, servingSize?: string, servingsPerContainer?: number,
    energyKcal?: number, proteinG?: number, carbsG?: number, fatG?: number   // per serving, as printed
  },
  medicine?: {
    kind: "medication" | "supplement", brand?: string, product: string, dose?: string,
    ingredients: [{ name, amount?, unit? }]
  },
  readings?: [{ metricKey, value, unit, label }],   // value already in the metric's unit
  lab?: ParseResult                                   // as POST /api/labs/parse
}
```

Exactly one of `meal`, `label`, `medicine`, `readings` and `lab` is present, matching the kind;
none for `other`. A kind whose read came back empty (no product on a medicine label, no reading
on a display, no line on a lab report) is answered as `other` with the description.

```
400 "Attach a photo."
400 "That file is not a JPEG, PNG or WebP photo."
400 "The photo is larger than 6 MB."
400 "kind must be one of: meal, nutrition_label, medicine_label, device_display, lab_report, other."
409 "Photos are not turned on for this server."
422 "The photo could not be read. Try a closer, sharper picture."
503 "The home model is not answering, so the photo could not be read. Nothing was saved."
```

### POST /api/journal

`CreateJournalEntryRequest` gains `nutrition?: { basis: "estimate" | "label", energyKcal?,
proteinG?, carbsG?, fatG? }`. With it the entry's source is `photo`. It is allowed on food and
drink only ("Only food and drink have numbers."), needs at least one number ("Give at least one
number, or leave nutrition out."), and each number must be inside the ranges above
("{Name} must be between {min} and {max} {unit}.").

`JournalEntryDto` gains `nutrition?` in the same shape. Entries without numbers are unchanged.

### POST /api/assistant/conversations/{id}/messages

The body gains `photo?: { kind, description }` (description at most 300 characters). `text` may
then be empty and stands as "What is in this photo?". The stored message is the person's text
followed by a line "Photo: {description}", and its meta holds `photo: kind`. The description
counts as the person's own words for the guard, so a number in it may be repeated. Capture runs
on the text only, never on the description. The model is told that it cannot see the photo, that
the line is what the photo shows, and to repeat its numbers as written without converting them.

A small model still converts units now and then, and the guard refuses a number that is neither
in the photo line nor in the brief. When a reply to a photo is refused, or the model cannot be
reached, the fallback says what the photo shows instead of summarising the day's record:
"I held that answer back because it did not pass my checks. Here is what the photo shows."
followed by the description.

### POST /api/body/measurements

Also takes `blood_pressure_sys`, `blood_pressure_dia`, `heart_rate_bpm`, `body_temp_c`,
`blood_glucose_mmol` and `spo2_pct`, with the ranges above.

## In the app

- The assistant's message box has a photo button when `status.photos` is true. It opens a sheet:
  "Take a photo" (not on the web) and "Choose a photo" (the phone's own photo picker). Under
  them: "The photo goes to your home server and its model, is read once and is not kept."
- The picker returns a JPEG no larger than 1600 pixels on its long side.
- While the server reads it the thread shows the picture with "Looking at the photo". Then a
  card for the kind:
  - meal and label: the text, tags and portion, the four numbers (for a label, servings eaten
    and the totals), the line about guesses for a meal, Save and Discard.
  - medicine: what was read and "Add to medicines", which opens the medicines form filled in.
  - display: one row per reading with the value in the person's units, Save and Discard.
  - lab report: how many markers were found and "Review the panel", which opens the lab review.
  - other: the description, sent with the person's question so the assistant answers.
- Every card has "Not right?" with the other kinds; choosing one reads the photo again as that
  kind. The picture leaves the app's memory when the card is saved or discarded.
- When the read fails the card says why (the messages above) and offers "Try again".
- The day log shows an entry's numbers with where they came from: "About 620 kcal, 35 g protein
  (guessed from a photo)" or "230 kcal, 3 g protein (from the label)".

## Left out on purpose

- Keeping photos. The owner chose to read and discard.
- Photos in the evening check-in, and more than one photo per message.
- Barcode lookups and any food database. A label is read as printed.
- Sending a photo to the cloud lookup.
- HEIC and other formats. The picker hands over a JPEG.
