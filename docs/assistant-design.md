# Assistant design (in-app assistant)

The assistant lives in the app and talks only to the local model the owner configured under `Ai:`
(Ollama, LM Studio, vLLM, llama.cpp; the OpenAI-compatible `IChatModel`). Code decides what is
said and what is saved; the model only words it, and a guard checks every sentence before it
reaches the screen. The Open WebUI connection through `/mcp` is a separate door and is unchanged.

What it does:

- Chat, with replies streamed as server-sent events.
- Saves what the person says to the day log at once, with Undo.
- An evening check-in that asks only about what today's record is missing.
- A morning brief written from the record.
- A reviewed playbook of suggestions, chosen by code.
- An evening reminder through the person's own ntfy server.
- A model check an admin runs to see whether their local model is good enough.

What it never does:

- Calls a cloud model, or any host except the configured model URL and the person's ntfy URL.
- Estimates calories or macros, or stores amounts of food.
- Lets the model decide the check-in flow or call tools.
- Shares anything with another household member.

## Privacy and access

Conversations, messages, the morning brief, remembered facts and nudge settings are own-only.
There is no share category for them and no `personId` parameter on any assistant route.
Repository methods take the caller's own person id and are named `...Own...`
(`IAssistantRepository`, `INudgeRepository`). A household member with every grant there is still
gets a 404 on someone else's conversation or message, and an empty list for memory.

When the assistant reads the record (goals, targets, readings, insights, the day log, medicines,
labs), it uses the server-internal own reads (`ListOwnAsync`, `TallyOwnAsync`,
`GetLatestOwnPerMetricAsync` and the like). The brief therefore only ever holds the caller's own
data, and it only ever goes to the local model.

The following are encrypted with `IEncryptionService`:

- message content
- conversation titles
- the cached morning brief
- the ntfy address
- the ntfy token

`state` and `meta` are jsonb that hold keys, ids and tags only. The ntfy address and token are
never logged and never returned. The API returns a masked address (`https://host/***`, because
the topic in the path is the secret part) and a `tokenSet` flag. Audit rows say that something
happened, never what was said.

Every model call is written to `ai_call` (purpose, prompt hash, tokens, latency, ok or the
refusal reason). The text is never written there. The purposes are:

- `assistant_chat`
- `assistant_checkin`
- `assistant_brief`
- `assistant_model_check`

## Tables (migration 0016_assistant)

Every table cascades from `person`.

`assistant_conversation`

| Column | Notes |
| --- | --- |
| id, person_id | |
| kind | `chat` or `checkin` (check) |
| title_cipher | text null, encrypted. A chat takes it from the first message: the first sentence when it fits in 60 characters, else the first 60 cut at a word |
| day | date. The local day of a check-in (required for `checkin`), null for a chat |
| state | jsonb. The check-in state machine (topic keys and flags only) |
| created_at, updated_at, ended_at | |

Indexes: `(person_id, updated_at DESC)`, and a unique `(person_id, day) WHERE kind = 'checkin'`
(one check-in per local day).

`assistant_message`

| Column | Notes |
| --- | --- |
| id, conversation_id, person_id | conversation_id cascades |
| role | `user` or `assistant` |
| content_cipher | text, encrypted |
| origin | `model`, `template`, `guard` or `code` |
| meta | jsonb: `captures` (type, id, kind, tags), `undone`, `topic`, `reason` |
| created_at | |

Origins:

- `model`: worded by the local model and passed by the guard.
- `template`: worded from a template (the model is off or unreachable, or the line was not reworded).
- `guard`: the guard replaced the model's answer, or a red flag was answered by code.
- `code`: typed by the person.

`assistant_brief`: `(person_id, day)` primary key, `facts_hash` (SHA-256 of the facts),
`text_cipher`, `generated`, `computed_at`.

`nudge_setting`:

- `person_id` (the primary key)
- `enabled`
- `ntfy_url_cipher`
- `ntfy_token_cipher`
- `evening_time` (local time, default 20:00)
- `updated_at`

`nudge_log`: primary key `(person_id, kind, day)`, `sent_at`. The only kind is `evening`. The
key allows one nudge per person and day.

Remembered facts reuse the existing `ai_memory` table. The value is encrypted and
`display_text` is left empty, because that column is plain.

## State brief

`IStateBriefBuilder` builds a plain-text brief for every chat reply. Code builds it and the model
calls no tools. The sections come in priority order, each as a heading with `- ` lines:

1. Today: the date, the person's name, and their unit system.
2. Goals: the slot, the title, the metric now and its target, the pace and the status (from
   `IBodyContextLoader`).
3. Targets: for each active target, today's value and verdict, and the days kept out of the days
   with an answer (`ITargetService.TallyOwnAsync`).
4. Readings against their usual range: sleep, HRV, resting heart rate, breathing rate, wrist
   temperature, SpO2, steps, exercise minutes, weight and check-in mood. Each line has the value,
   when it is from, the `p25` to `p75` band of the latest baseline, and below, inside or above.
   Overnight metrics read today's row (last night); the others read yesterday's full day.
5. Insights from the rules: up to 4 cards from yesterday and today, by severity.
6. Playbook entries that apply today: these are the only suggestions the model may make.
7. Medicines and supplements they take: product, brand, dose, schedule, and ingredients with
   amounts.
8. Latest blood panel: the collection date, and the markers outside the usual adult range
   (`LabFlagging`) or flagged by the lab.
9. Things they told you before: up to 30 `ai_memory` values.
10. Day log, last 7 days, newest first: kind, the person's words (up to 160 characters), tags
    and portion.

The cap is 6000 characters. Sections are added in order. The first line that would pass the cap
stops the brief, so the older day log is what gets cut. The builder also returns every number in
the brief (canonical digits plus number words), and the guard checks against that set.

## Capture

`IJournalCapture` is a lexicon in code. It splits a message into sentences and turns statements
into day log drafts. The drafts are saved through `IJournalService.SaveDraftAsync` with source
`assistant` (or `checkin`) and the user message id as `MessageId`, so that Undo can delete them.

These sentences are never captured:

- Questions: a sentence that ends in `?`, opens with a question word followed by a verb ("how
  was"), or opens with a verb followed by a subject ("should I", "did I").
- Plans, wishes and conditionals: if, would, should, could, going to, will, tomorrow, want to,
  need to, thinking about, and similar.
- Sentences about someone else: "my wife has", "he was".

An item that is negated in its clause ("no beer", "didn't have any coffee") is not logged. A later
"had" in the same clause cancels the negation.

Each sentence gives at most one entry per kind:

- The first kind found is the primary one. It keeps the person's words as the text, with calorie
  and gram counts taken out.
- The other kinds carry tags only.

Each kind needs a cue before it is logged:

| Kind | Cue | Tags (from `JournalTags`) |
| --- | --- | --- |
| food | an eating verb (had, ate, grabbed) or a meal phrase (for lunch). The check-in topic `eating` also counts | high_protein, vegetables, fruit, whole_grains, sweets, fried_food, takeaway, home_cooked, late_meal, swap, skipped_meal, or no tag for meal words |
| drink | a drinking verb, a quantity before the drink (two, a pint of, a cup of), or a sentence of 4 words or fewer | alcohol, caffeine, late_caffeine, sugary_drink, water |
| training | a past-tense verb (did, went, lifted, ran), or today or this morning | lifted, cardio, walk, rest_day, hard_session |
| symptom | none needed | headache, sore_throat, congestion, cough, fever, fatigue, nausea, stomach_upset, soreness, pain, stress, low_mood |
| sleep | none (the phrases are specific) | poor_sleep, good_sleep, nap, late_night |

A few rules apply to particular tags:

- `hard_session` needs a training word beside it. "A brutal day at work" is not a session.
- `late_caffeine` is added for a caffeine drink at or after 15:00 local. The time comes from a
  clock time ("at 4pm"), a part of the day ("this afternoon"), or the time the message was sent.

Portion (food and drink only) is set only when the person says it:

- large: big, large, huge, loads of, seconds
- small: small, light, a bit of, just a
- usual: the usual, normal portion

Days:

- "yesterday" moves an entry to the day before. Without a time it is placed at 12:00 local.
- "last night" also moves an entry to the day before, at 21:00.
- Sleep entries are the exception: a night is credited to the morning it ended, like the sleep
  readings (whose sleep day runs from 18:00 the evening before to 18:00), so "slept badly last
  night" stays on today.
- No time is ever later than now.

No calorie or macro estimate is ever made. Phrases such as "600 calorie" and "40g of protein" are
taken out of the stored words, and no tag carries an amount.

Durable facts go to `ai_memory` with source "In-app assistant". The list is short on purpose,
and a fact is only taken from a statement:

| Pattern | Type | Key | Value |
| --- | --- | --- | --- |
| I'm vegetarian, vegan or pescatarian | preference | diet | Eats a vegetarian diet |
| I don't drink (alcohol) | preference | alcohol | Does not drink alcohol |
| I work nights, I'm on night shifts | life_event | work_pattern | Works night shifts |
| I work shifts | life_event | work_pattern | Works shifts |
| I'm lactose intolerant, gluten free, coeliac | condition | lactose_intolerant, gluten_free, coeliac | |
| I'm allergic to X | condition | allergy_x | Allergic to X |

## Guard

`IReplyGuard` handles red flags first. If the person's message names a red-flag symptom, the
reply is a fixed template and the model is never called:

- Emergency template, for chest pain or pressure, trouble breathing, fainting or collapse, stroke
  signs (a drooping face, slurred speech, one-sided weakness or numbness, sudden confusion or loss
  of vision, the worst headache), coughing or vomiting blood, heavy bleeding, a seizure, swelling
  of the throat or face, or an overdose. It tells the person to call their local emergency number
  or go to an emergency department.
- Crisis template, for suicidal thoughts, wanting to die, or self-harm. It points to emergency
  services and crisis lines (988 in the US and Canada, Samaritans on 116 123 in the UK and
  Ireland).

A plain negation right before the phrase ("no chest pain") does not trigger a template. Each red
flag is audited as `assistant.red_flag_answered`.

A model reply, and each streamed sentence, must pass every rule below. "The allowed text" means
the brief (with its playbook entries) plus the person's own message.

1. Numbers: every number in the reply is in the allowed set. Digits are compared in canonical
   form ("1,200" is "1200"). The words two to twenty, the tens, hundred, thousand, dozen, twice,
   double and triple count as numbers; "one" does not.
2. Dose units: a number followed by mg, mcg, µg, ug, g, IU, ml, units, tablets, capsules, pills,
   drops, milligrams, micrograms or grams is allowed only when that unit appears with a number in
   the allowed text.
3. No diagnosis: no "diagnos...", "you (may) have a cold, infection, ...", "you are anaemic,
   diabetic, infected, ...", "this means a cold", or "sign of a disease".
4. No detection claims: no "infection detected".
5. No cause claims: no "caused by", "is causing", "is damaging", "that's why your", "because of
   the alcohol" and the like.
6. Nothing from the proposal's "What we will not show" list:
   - metabolic age, visceral fat or muscle quality
   - calories, kcal or macros
   - a stress score
   - FFMI, a "natural limit", steroids or PEDs
   - reassurance from oxygen ("oxygen is fine") or apnea scores
   - predicted lab values ("your LDL should now be")
   - streaks, badges or scores out of 100
   - nasal zinc
7. Remedies: a remedy name (zinc, vitamin C, vitamin D, melatonin, magnesium, echinacea, ibuprofen,
   paracetamol, antihistamine, iron, creatine, fish oil and others) must appear in the allowed
   text. In practice that means a playbook entry that applies today, the person's medicine list,
   or the person's own question.
8. Length: a whole reply is at most 1200 characters. An encouragement line is at most 240 and
   holds no question.
9. Encouragement and brief lines also may not promise: no "will improve", "will show",
   "definitely", "guarantee" or "for sure".

Em and en dashes from the model are rewritten to commas and hyphens, not refused. When a reply
fails, a template replaces it:

- "I held that answer back because it did not pass my checks. Here is what your record says." It
  is followed by the brief's facts: sleep, up to two standout readings, today's targets so far,
  and the top insight.
- With the model off or unreachable: "The model is not reachable right now, so here is a plain
  summary from your record." followed by the same facts.
- Both start with "Saved to today's log." when the message saved something.

## Streaming

The model is asked with `stream: true`. The adapter skips `<think>` blocks, including tags split
across pieces. Inside the reply:

1. `SentenceBuffer` gathers pieces into sentences. A sentence ends at `.`, `!` or `?` followed by
   white space, or at a line break. Decimals such as "7.5" and abbreviations such as "e.g." do not
   end a sentence.
2. Each sentence is checked before it is sent as a `delta`.
3. The first sentence that fails stops the stream. If nothing was shown yet, the template is sent
   as a `delta`; if something was, a `replace` swaps all of it.
4. When the stream ends, the whole reply is checked once more. If that check fails, a `replace`
   is sent.
5. If the model cannot be reached part way, a `replace` sends the unavailable template.
6. The stored reply is always the final text, with its origin.

## Evening check-in

The check-in is a state machine in code (`CheckinMachine`), kept as JSON on the check-in
conversation of the day. The topics come in this order:

1. eating
2. training
3. drinks (alcohol, caffeine)
4. symptoms
5. mood

The check-in asks only about the topics today's own record is missing:

| Topic | Missing when today has no |
| --- | --- |
| eating | day log entry of kind food |
| training | training entry, strength set or workout |
| drinks | drink entry |
| symptoms | symptom entry |
| mood | `checkin_mood` daily summary (the existing mood check-in) |

The flow:

1. Start. `POST /api/assistant/checkin` with no answer creates the day's conversation, computes
   the missing topics, and asks the first one with "Let's close the day." in front. When nothing
   is missing it says so and the day is done.
2. Answer. The answer is saved as a user message and harvested at once by `CheckinHarvest`. It
   goes through the capture lexicon with the topic as a hint, so fragments such as "salmon, rice
   and greens" count, and it is saved with source `checkin`. If the lexicon finds nothing to tag,
   the answer is saved in the person's own words as an entry of the topic's kind.
3. None. An answer of "none today" saves a marker entry, so a target can count the day as known
   (`TargetTally` treats a journal day as unknown when it has no entry at all):
   - training: `rest_day` (built-in)
   - eating: `skipped_meal` (built-in), with the words
   - drinks: `no_alcohol` and `no_caffeine` (`AssistantTags`), each only when that drink was not
     mentioned ("two coffees, no alcohol" saves caffeine plus `no_alcohol`)
   - symptoms: `no_symptoms` (`AssistantTags`)

   These are ordinary well-shaped tags on ordinary entries, so no schema change was needed. The
   two new tags are not built-in tags, so they cannot be the subject of a target, and an
   `at_most` alcohol target reads the marker day as zero.
4. Mood. Mood words go into the existing mood check-in (`ICheckinService`) on its scale of 1 to
   6: very low, terrible = 1; low, bad, rough = 2; meh = 3; okay, fine, not bad = 4; good = 5;
   great = 6; or a digit from 1 to 6. If no mood word is found, the question is asked once more
   and then the topic is skipped.

   Off the topic. The conversation can be left at any point and keeps what it could harvest, so
   an answer that does not fit the question (the mood question answered with "I had salmon and
   rice for dinner") still goes through the same capture as chat, is saved with source `checkin`
   on the user message (so undo works the same way) and is not saved twice. The reply says what
   was kept ("Thanks, I have put the food on today's log.") and asks the question again, once;
   with nothing to keep it is the plain retry ("Sorry, I did not catch that. ..."). Any answer
   that puts another topic's kind on the log (a glass of wine named with dinner) marks that topic
   done, so it is not asked.
5. Skip. "skip", "pass" or "rather not say" saves nothing and moves on.
6. Encouragement. Before the next question comes one line from the bank, keyed by topic and by
   what the answer said:

   | Topic | Tones |
   | --- | --- |
   | eating | balanced, treat, none, noted |
   | training | hard, done, none, noted |
   | drinks | alcohol, late_caffeine, none, noted |
   | symptoms | unwell, none |
   | mood | high, middle, low |

   For example, training answered hard gives: "That was great work today. It will be
   interesting to see if it is reflected in tomorrow's recovery readings." The model may reword
   the line. The reworded line must pass the line check (no numbers, no promise, no question,
   240 characters at most); otherwise the bank line is used.
7. Leave. `leave: true` keeps everything harvested so far, ends the turn and marks the day
   partial. A later empty POST resumes with whatever is still missing and was neither answered
   nor skipped.
8. Red flag. An answer that names a red flag gets the red-flag template and the check-in stops
   (partial).

Statuses:

- `not_started`: no check-in conversation today.
- `partial`: started and not finished, or left.
- `done`: every missing topic was answered or skipped.

## Morning brief

`GET /api/assistant/brief`. Code chooses up to five items, each worded by a template:

| Kind | From |
| --- | --- |
| `sleep` | today's `sleep_stage` row (last night) and its band |
| `reading` | resting heart rate, HRV, breathing rate and wrist temperature last night, when outside their `p10` to `p90` band; at most two |
| `targets` | yesterday's day targets: kept out of those with an answer |
| `insight` | the leading insight of yesterday or today, by severity |
| `evening` | tags from yesterday's `checkin` and `assistant` entries, led by "Last night's check-in noted" when that check-in is done, otherwise by "Yesterday's log noted" |

The text is "Good morning, {name}." followed by the items. With no items it says "Nothing new in
your record since yesterday." The model may reword the text; every number must be one of the
template's, and the whole-reply guard applies. The text is cached per person and local day in
`assistant_brief` and written again only when the facts hash changes.

## Playbook

The playbook is a set of reviewed entries in code (`Playbook`). Code checks which entries apply
against the record and the medicine and supplement substance tags; the model may only word them.
Every figure comes from a VERIFIED row of the research notes (B1, B2, B3a with the B3b wording
rules, B4, E1).

| Key | Applies when | Wording | Evidence | Source |
| --- | --- | --- | --- | --- |
| `sleep_seven_hours` | last night under 7 h, or a `sleep_debt` insight, or poor_sleep or late_night in the last 3 days | "Sleep researchers' consensus for adults is 7 or more hours a night on a regular basis. A steady bedtime that leaves room for that is the simplest place to start." | consensus | Watson 2015, Sleep 38(6):843-844 (E1) |
| `caffeine_cutoff` | caffeine or late_caffeine in the last 7 days, or a caffeine substance on the list | "In a pooled analysis of caffeine studies, caffeine cut total sleep by about 45 minutes and sleep efficiency by about 7 %. The authors suggest a coffee at least 8.8 hours before bed, and a standard pre-workout serve at least 13.2 hours before." | meta-analysis | Gardiner 2023, Sleep Med Rev 69:101764 (B2) |
| `alcohol_nights` | alcohol in the last 7 days | "Nights after drinking are worth setting beside your own dry nights. The morning brief puts resting heart rate and sleep side by side, so your own pattern shows over time." No outside figure is quoted: none is verified | personal pattern | the person's own record |
| `training_recovery` | hard_session in the last 2 days, or a `training_spike` insight | "Heart rate variability usually takes up to 24 hours to recover after an easy session, 24 to 48 hours after a threshold session, and at least 48 hours after a high-intensity one. An easier day after a hard one gives the readings room to come back." | review | Stanley 2013, Sports Med 43(12):1259-77 (B1) |
| `early_cold_rest` | a cold-like symptom (sore throat, congestion, cough, fever) in the last 3 days, or an `illness_pattern` insight | "Rest and an easy day come first. Readings alone can only say that something looks unusual for you; a symptom you log is what makes a cold the likelier story. Anything beyond self-care is for a clinician." | self-care | none quoted |
| `early_cold_zinc` | a cold-like symptom in the last 3 days and no zinc on the list. Readings alone never qualify | "A 2024 Cochrane review found that zinc taken once a cold has started may shorten it by about 2.37 days on average, with low certainty. Side effects were more common with zinc: an unpleasant taste, loss of smell, vomiting, stomach cramps and diarrhoea. Taken to prevent colds, it may make little or no difference." | low certainty | Nault 2024, Cochrane CD014914 (B3a, B3b) |
| `early_cold_vitamin_c` | a cold-like symptom in the last 3 days | "A Cochrane review found that vitamin C taken regularly, before any cold, shortened colds by about 8 % in adults. Started after symptoms begin, it showed no consistent effect on how long or how bad a cold was." | Cochrane review | Hemila 2013, Cochrane CD000980 (B4) |

The zinc entry follows the B3b note:

- It covers all zinc formulations; the word "lozenge" is not used.
- It lists only the adverse events the review names.
- It says nothing about nasal zinc.
- It makes no claim about interactions with other medicines.

Instead of an interaction claim, both supplement entries carry a caution whenever the person has
an active medicine (kind `medication`) on their list: "You have medicines on your list. Ask a
pharmacist before adding a supplement." The brief includes the entries that apply, and the guard
lets the model name a remedy only when an applicable entry (or the person) names it.

## Nudges

There is one evening nudge, sent to the person's own ntfy topic. Settings:

- The address must be http or https, have no user info, include a topic path, and be at most 500
  characters.
- The token is optional, at most 200 characters, with no spaces.
- `eveningTime` is `HH:mm` local and defaults to 20:00.
- Turning reminders on needs an address.

`NudgeJob` is a job-locked hosted service, like `InterpretNightlyJob`, and runs every 5 minutes.
It sends to a person when all of these hold:

- Their local time is between the evening time and three hours after it.
- Today's check-in is not done.
- No `evening` row is in `nudge_log` for the day.

It POSTs the fixed text and logs the day, so a person gets at most one nudge a day.

| | Title | Message |
| --- | --- | --- |
| Evening nudge | Evening check-in | Ready for a quick end of day chat? A minute or two closes today's log. |
| Test (`POST /api/assistant/nudges/test`) | Test reminder | This is a test. Evening reminders will arrive here. |

Neither text holds a number or a health value. The request sends the title in a `Title` header,
`Priority: default`, and the token as a bearer token, and follows no redirects. A failure is
logged with the person id and a short reason ("The server answered 403."), never the address,
and is retried on the next tick inside the window.

## Model check

`POST /api/assistant/model-check` is admin only and is limited to 5 calls a minute. It runs five
fixed conversations over a made-up brief, plus one encouragement rewording, through the
configured model and the guard. No one's record is used.

| Case | Checks |
| --- | --- |
| `restates_a_number` | uses a number from the brief (62) and nothing else |
| `refuses_new_numbers` | does not invent a step count |
| `no_dose` | gives no zinc dose |
| `no_diagnosis` | does not say "you have covid" |
| `playbook_only` | suggests only playbook remedies |
| `encouragement_line` | rewords a check-in line with no number, promise or question |

## API

Every route needs a bearer token and is rate limited like the rest of `/api`. There is no
`personId`: everything is the caller's own.

Conventions:

- JSON is camelCase.
- Times are ISO 8601 UTC. Days are `yyyy-MM-dd` in the person's time zone.
- Every field shown is always present; `?` marks one that may be `null`.
- Errors are `application/problem+json` (RFC 7807) with `title`, `status`, `detail` and
  `traceId`:
  - A failed service check is a 400 with the title "Validation failed" and the message as the
    `detail`.
  - A request that breaks a field's attributes (a missing `text`, a string over its length) gets
    the framework's 400 with an `errors` object.
  - An id that is not the caller's is a 404, with the detail given per route.

### Shared shapes

```
JournalEntryDto = {
  id: uuid, at: datetime, day: date,
  kind: "food" | "drink" | "symptom" | "training" | "sleep" | "note",
  text?: string, tags: string[], portion?: "small" | "usual" | "large",
  source: "chip" | "typed" | "assistant" | "checkin", messageId?: uuid
}

AiMemoryDto = {
  id: uuid,
  type: "goal" | "condition" | "medication" | "preference" | "life_event" | "household",
  key: string, value: string, displayText?: string, source?: string,
  createdAt: datetime, updatedAt: datetime
}

CaptureRefDto = {
  type: "journal" | "memory" | "mood",
  id: uuid,              // journal entry id, ai_memory id or check-in id
  kind?: string,         // journal kind, memory type, or "mood"
  tags: string[]         // journal tags, or [memory key]
}
```

### GET /api/assistant/status

```
200 { "enabled": true, "model": "qwen3:8b", "reachable": true }
```

- `enabled` is false when `Ai:Enabled`, `Ai:BaseUrl` or `Ai:Model` is not set. Then `model` is
  null and `reachable` is false.
- `reachable` comes from a `GET {Ai:BaseUrl}/models` with a 5-second timeout; nothing is
  generated.

### GET /api/assistant/conversations

Returns up to 50 conversations, most recently updated first:

```
200 [ ConversationSummaryDto ]
ConversationSummaryDto = {
  id: uuid, kind: "chat" | "checkin",
  title: string,    // the first message's first sentence when it fits in 60 characters,
                    // else its first 60 cut at a word without trailing punctuation, the title given,
                    // "New conversation", or "Evening check-in, Nov 10"
  day?: date,       // check-ins only
  createdAt: datetime, updatedAt: datetime
}
```

### POST /api/assistant/conversations

```
body { "title"?: string (max 80) }       // the body may be empty or {}
200 ConversationSummaryDto
400 "A title is at most 80 characters."
```

### GET /api/assistant/conversations/{id}

Returns the conversation with every message, oldest first:

```
200 {
  id, kind, title, day?, createdAt, updatedAt,
  messages: [ AssistantMessageDto ]
}
AssistantMessageDto = {
  id: uuid, role: "user" | "assistant", content: string,
  origin: "model" | "template" | "guard" | "code",
  createdAt: datetime,
  captures: CaptureRefDto[],    // what this user message saved; [] for replies
  capturesUndone: bool,         // true once Undo ran for this message
  redFlag: bool                 // true on a reply code wrote for a red flag; shown prominently and in full
}
404 "No such conversation."
```

### DELETE /api/assistant/conversations/{id}

```
204
404 "No such conversation."
```

Deleting a conversation removes its messages. Day log entries it saved stay.

### POST /api/assistant/conversations/{id}/messages

```
body { "text": string (required, 1 to 2000 characters) }
```

The errors come back before any stream starts:

```
400 "Say something first."                                   // blank text
400 "The evening check-in is answered through /api/assistant/checkin."  // a check-in conversation
404 "No such conversation."
```

On success the answer is `200` with `Content-Type: text/event-stream; charset=utf-8`. Each event
is a frame of the form:

```
event: <name>
data: <one line of JSON>
<blank line>
```

The events come in this order: `capture` exactly once, then `delta` zero or more times, then
`replace` at most once, then `done` exactly once. The stream closes after `done`.

```
event: capture
data: {
  "conversationId": uuid,
  "messageId": uuid,                 // the saved user message; use it for Undo
  "entries": [ JournalEntryDto ],    // day log entries saved from it (source "assistant")
  "remembered": [ AiMemoryDto ]      // facts saved to memory
}

event: delta
data: { "text": string }

event: replace
data: { "text": string, "reason": "guard" | "unreachable" }

event: done
data: {
  "conversationId": uuid,
  "messageId": uuid,                 // the stored reply
  "userMessageId": uuid,
  "origin": "model" | "template" | "guard",
  "text": string,                    // the final reply, the same as what is stored
  "redFlag": bool                    // true when code answered a red flag
}
```

- `delta.text` is one checked sentence. The deltas after the first start with a space, so the
  client appends them as they come.
- On `replace`, the client throws away everything shown for this reply and shows
  `replace.text` instead.
- `capture.entries` may be empty. When it is not, the client shows the saved items with an Undo
  action that calls `DELETE /api/assistant/messages/{messageId}/captures`.
- `done.origin`:
  - `template` when the model is off, or could not be reached;
  - `guard` when a red flag was answered by code (`redFlag` is true), or the model's answer was
    refused (`redFlag` is false);
  - `model` otherwise.
- When the model is off, a single `delta` carries the template. With a red flag the model is
  never called.

### DELETE /api/assistant/messages/{messageId}/captures

Undo. It removes the day log entries, the remembered facts and the mood check-in that one user
message saved. It is idempotent: the entries go every time, and memory and mood only on the
first call.

```
200 { "messageId": uuid, "removedEntries": int, "removedMemories": int, "removedCheckins": int }
404 "No such message."    // not the caller's, or an assistant reply
```

### POST /api/assistant/checkin

Starts, continues, resumes or leaves today's evening check-in.

```
body {
  "answer"?: string (max 2000),   // the answer to the topic being asked
  "leave"?: bool                  // true: stop now, keep everything, mark the day partial
}
```

The body may be `{}` or empty.

- An empty body starts the check-in, or resumes it after leaving.
- `answer` and `leave` may be sent together: the answer is harvested, then the check-in ends.

```
200 CheckinTurnDto
CheckinTurnDto = {
  conversationId: uuid,
  day: date,
  status: "partial" | "done",
  topic?: "eating" | "training" | "drinks" | "symptoms" | "mood",   // asked now; null when done or left
  encouragement?: string,   // the line about the answer just given
  question?: string,        // the next question; null when done or left
  reply: string,            // what to show: the encouragement and then the question, or the closing line
  captured: JournalEntryDto[],   // day log entries saved from this answer (source "checkin")
  mood?: int,               // 1 to 6 when this answer set the mood check-in
  userMessageId?: uuid,     // the stored answer (null when there was none); Undo works on it too
  assistantMessageId: uuid,
  done: string[],           // topics answered, in order
  skipped: string[],
  remaining: string[],      // the topic asked now first, then the rest; [] when done or left
  redFlag: bool             // true when the answer named a red flag: reply is the get-care template, the check-in stops
}
```

Closing lines:

| Line | When |
| --- | --- |
| "That's the day. Everything you told me is saved." | the last topic was answered |
| "Your day already has food, training, drinks, symptoms and mood on record, so there is nothing to ask tonight. Thank you." | nothing was missing at the start |
| "No problem. I have kept everything you told me, and today is marked partial." | `leave` |
| "Today's check-in is done. Anything you add in chat still goes on today's log." | an answer sent after the check-in was done (it is still captured) |

Questions:

| Topic | Question |
| --- | --- |
| eating | "How was your eating today?" |
| training | "Did you lift or train today? How did it go?" |
| drinks | "Any alcohol or caffeine today?" |
| symptoms | "How are you feeling physically? Any symptoms today, like a sore throat, a headache or tiredness?" |
| mood | "And how was your mood today: great, good, okay, meh, low or very low?" |

The first question of a new or resumed check-in starts with "Let's close the day.". The client
may offer quick replies for mood (great, good, okay, meh, low, very low) and a "Skip" chip (send
"skip").

### GET /api/assistant/checkin/today

```
200 {
  day: date,
  status: "not_started" | "partial" | "done",
  conversationId?: uuid,
  topic?: string,      // the topic waiting for an answer; null when not started, left or done
  question?: string,
  missing: string[],   // topics today's record has nothing for right now, in asking order
  done: string[],
  skipped: string[]
}
```

### GET /api/assistant/brief

```
200 {
  day: date,
  text: string,           // "Good morning, Ada. You slept 6 h 5 min last night, below your usual range. ..."
  generated: bool,        // true when the model's rewording passed the guard
  items: [ { kind: "sleep" | "reading" | "targets" | "insight" | "evening", text: string } ],
  computedAt: datetime
}
404 "No person profile for this account."
```

### GET /api/assistant/memory

Returns every remembered fact, newest first. This includes facts written over `/mcp` through the
remember tool.

```
200 [ AiMemoryDto ]
```

### DELETE /api/assistant/memory/{id}

```
204
404 "No such memory."
```

Audited as `memory.deleted`.

### GET /api/assistant/playbook

Returns the entries that apply to the caller today:

```
200 [ PlaybookEntryDto ]
PlaybookEntryDto = {
  key: string,           // see the playbook table
  topic: "sleep" | "caffeine" | "alcohol" | "training" | "early_cold",
  title: string,
  text: string,          // the reviewed wording
  evidenceGrade: "consensus" | "meta-analysis" | "review" | "low certainty" | "Cochrane review" | "personal pattern" | "self-care",
  source: string,        // citation
  sourceUrl: string,     // "" when no outside source is quoted
  why: string,           // why it applies today, from the record
  cautions: string[]
}
404 "No person profile for this account."
```

### GET /api/assistant/nudges

```
200 NudgeSettingsDto
NudgeSettingsDto = {
  enabled: bool,
  urlSet: bool,
  ntfyUrlMasked?: string,   // "https://ntfy.example.net/***"; the topic is never returned
  tokenSet: bool,           // the token itself is never returned
  eveningTime: "HH:mm",     // local, default "20:00"
  timeZone: string,         // the person's IANA zone the time is read in
  lastSentOn?: date
}
```

### PUT /api/assistant/nudges

```
body {
  "enabled": bool,
  "ntfyUrl"?: string (max 500),     // null keeps the stored one, "" clears it
  "ntfyToken"?: string (max 200),   // null keeps the stored one, "" clears it
  "eveningTime"?: "HH:mm"           // null keeps the stored time
}
200 NudgeSettingsDto
```

The 400 details:

- "Enter the address of your ntfy topic."
- "The address must start with http:// or https://."
- "Put the token in the token field, not in the address."
- "The address must include the topic, for example /evening."
- "The address is at most 500 characters."
- "The token is at most 200 characters."
- "The token cannot contain spaces or line breaks."
- "eveningTime must be HH:mm, for example 20:30."
- "Add the address of your ntfy topic before turning reminders on."

Audited as `assistant.nudges_changed`, with the enabled, urlSet and tokenSet flags only.

### POST /api/assistant/nudges/test

No body. It sends the test text to the stored topic. It is limited to 5 calls a minute.

```
200 { "sent": bool, "error"?: string }
```

When sending fails, `error` is one of:

- "The server answered 403."
- "The server could not be reached."
- "The server did not answer in time."

The `error` never holds the address. A missing address is a 400 with the detail "Add the address
of your ntfy topic first."

### POST /api/assistant/model-check

Admin only (403 for anyone else). No body. It is limited to 5 calls a minute.

```
200 {
  enabled: bool,           // false: model not configured; cases is [] and passed is false
  model?: string,
  passed: bool,            // every case passed
  passedCount: int,
  total: int,
  cases: [ { name: string, checks: string, passed: bool, detail: string, latencyMs: int } ],
  checkedAt: datetime
}
```

`detail` says why a case passed or failed, for example "refused by the guard: number 10000 not in
the record" or "the model did not answer: timeout".

### Audit actions

- `assistant.conversation_created`
- `assistant.conversation_deleted`
- `assistant.captures_undone` (with counts)
- `assistant.checkin_started` (with the count of missing topics)
- `assistant.red_flag_answered`
- `assistant.nudges_changed`
- `assistant.nudge_tested`
- `assistant.model_checked` (with the pass count)

Day log saves and deletes are also audited as `journal.created` and `journal.deleted`, with source
`assistant` or `checkin`.

## Left out on purpose

- No tool calls by the model. The proposal sketches tools ("save a note, set a target, start a
  trial"). A small local model reasons better over a prepared brief, and every write here is made
  by code from the capture.
- The proposal keeps conversations for 30 days. That retention sweep is not built yet: messages
  stay until the conversation is deleted.
- Model-written suggestions outside the playbook, and any figure from a row that is not marked
  VERIFIED (alcohol sizes, Mason 2022 detection rates, vitamin D in colds).
- An "expect tonight" line in the check-in that quotes the person's own pattern (for example
  "about 20 min less sleep"). That needs the personal pattern engine first.
