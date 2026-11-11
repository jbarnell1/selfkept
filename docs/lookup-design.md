# Cloud lookup

> Read this when working on: the opt-in lookup that sends a question from the assistant to a cloud
> model, its consent and warning screens, the scrubber, or anything under `Lookup`.

The assistant runs on a small local model, or on templates when there is none. Neither can read
the medical literature. A cloud lookup lets a person send one question to a large cloud model with
web search, read the answer with its sources, and have the local side apply it to their record.

It is the one place where text a person wrote can leave the server for a model. It exists because
the owner of the project asked for it, and it is built so that it cannot happen by accident.

## The rule this changes

The project rule was "no cloud model calls". It now reads: no cloud model calls, except a cloud
lookup that is off by default, that the person turned on for themselves, and whose exact text the
person saw and sent. Nothing else changes:

- The state brief, the check-in, the morning brief, insights and the day log never go to a cloud
  model on their own. Only the text on the lookup screen goes, and only when the person presses
  send. Facts from the record are on that screen only when the person chose "With my details".
- The server never sends a lookup on its own. No schedule, no retry in the background, no
  "improve this answer" call.
- The only host contacted is the provider's API (`api.anthropic.com`). No telemetry, no analytics.
- A server without a provider key has no lookup: the feature flag is absent and the routes
  answer 404.

## Three switches, all off by default

| Switch | Who | Where | What it does |
| --- | --- | --- | --- |
| Server | whoever runs the box | `SELFKEPT_LOOKUP_ENABLED=true` and a provider key | Makes the feature exist. Adds `assistant.lookup` to `/api/system/version` |
| Person | each person, for themselves | You, Assistant, Cloud lookup | Lets the assistant offer a lookup in that person's chats. Turning it on needs a ticked acknowledgement |
| Send | the person, every time | the lookup screen | Sends the text shown on that screen, once |

Nobody can turn the person switch on for someone else. An admin has no route to it.

## What the person sees

1. **The offer.** After a reply to a question, the assistant shows a quiet line under it:
   "Look this up with a cloud model". Nothing is sent by tapping it. The offer appears only when
   the server switch and the person switch are on, the message reads as a question, and the
   message is not a red flag. A red flag is answered by code as before and is never offered a
   lookup.
2. **The lookup screen.** A full-height sheet with a warning band at the top that cannot be
   scrolled away:
   "This leaves your server. The text below goes to {provider} over the internet. Once it is sent
   it cannot be taken back."
   Under it:
   - A choice of two modes, "Anonymized" (chosen first) and "With my details".
   - What was removed or changed to build the text ("your name", "2 dates", "3 numbers rounded").
   - The exact text that will be sent, in a box the person can edit.
   - A line that is always there: "{provider} can tell the request came from this server's
     account, whatever the text says."
   - "Send to {provider}" in the warning colour, never the app's primary colour, and "Cancel".
3. **The answer.** A message marked "Cloud model" with the provider's name, set apart from local
   replies by its outline and label, with its sources as links and a "What was sent" row that
   opens the exact outgoing text. Under it the fixed line: "General research, not advice about
   you."
4. **Applied locally.** When a local model is configured, the server then asks it, on the box,
   for two or three sentences on what the answer means for this person, using the state brief.
   That text passes the reply guard like any other local reply and is its own message, marked as
   local. With no local model this step is skipped. The brief never goes to the cloud.
5. **Sent to the cloud.** A page under You that lists every lookup the person sent: when, the
   mode, the provider and model, and the outgoing text. Each can be deleted.

## The two modes

The draft is built by code (`LookupScrubber`). A local model, when there is one, may reword the
anonymized draft into a general research question; the code scrub then runs again on what the
model wrote, and the model's version is dropped if it contains a name or a number that was not in
the scrubbed draft. The scrub is the guarantee; the rewording is polish.

**Always, in both modes** (hard identifiers):

- The person's display name and the names of everyone in the household, whole words, any case.
- Email addresses, phone numbers, web addresses, and the server's own host name.

The server refuses to send text that still holds a hard identifier (422 with what it found), so an
edit cannot put one back.

**Anonymized** adds:

- Calendar dates are removed ("on March 3" becomes "recently").
- A stated age becomes a decade ("I'm 43" becomes "in my 40s").
- Other numbers are rounded to two significant figures (187.4 becomes 190).
- Nothing from the record is added. The text is the person's question and no more.

**With my details** keeps the question as typed (minus hard identifiers) and adds an "About me"
block from the state brief: age as a decade, sex when set, and the brief's fact lines (recent
readings, goals, medicines and supplements). The birth date is never included. The warning band
gains a second line: "This includes readings and medicines from your record."

What the scrubber cannot do: it does not understand prose. A street address, an employer, a rare
condition or a story that identifies someone will pass. The screen says so in one line ("Read it
before you send it. Only names, contact details, dates and numbers are caught for you."), which is
why the text is always shown and never sent unseen.

## The cloud call

- Provider: Anthropic, through the official C# SDK (`Anthropic` NuGet package). The SDK is
  referenced by one class, `AnthropicLookupClient`, behind `ICloudLookupClient`. Tests use a fake.
- Model: `Lookup:Model`, default `claude-opus-5-5` (an empty value means the default). The request
  opts in to the provider's server-side fallback (`fallbacks: "default"`, beta
  `server-side-fallback-2026-07-01`): when a safety classifier declines the question, the provider
  re-runs it on the model it recommends inside the same call. The model that answered is what is
  stored and shown.
- Tools: the provider's server-side web search (`web_search_20260209`), at most
  `Lookup:MaxSearches` (5) searches. No other tool.
- System prompt (fixed, in code): answer as a short research summary for an adult reader; cite
  sources; say how strong the evidence is and say plainly when it is weak or mixed; no diagnosis,
  no dose instructions beyond what a cited source states; under 300 words.
- The request holds the system prompt and the text from the lookup screen. No history, no brief,
  no person id, no metadata field.
- Stop reasons are checked before the text is read: a refusal, a cut-off answer and a failed
  search are each a failed lookup with a plain message, never a partial answer shown as whole.
  `max_tokens`, an empty answer and a search loop still paused after three continuations are
  `cut_off`; a search counts as failed when every search the provider ran came back as an error.
  A `pause_turn` (the provider paused its search loop) is resumed by sending the paused content
  back, at most three times, inside the one send.
- Sources are the pages the answer cites; when it cites none, the pages its searches found (at
  most 10), de-duplicated by address.
- HTTP failures: 401 and 403 are `key_rejected`, 429 is `rate_limited`, everything else (a 400,
  a 5xx, the network, the timeout) is `unreachable`.
- Timeout `Lookup:TimeoutSeconds` (180). One attempt. A failure is shown; the person may send
  again, which is a new send with a new acknowledgement.

## What is stored

Migration `0021_lookup`:

- `lookup_setting (person_id pk, enabled, enabled_at, updated_at)`.
- `assistant_lookup (id, person_id, conversation_id, question_message_id, answer_message_id, mode,
  status, question_cipher, sources_cipher, provider, model, input_tokens, output_tokens,
  created_at, answered_at)`. `mode` is `anonymized` or `detailed`. `status` is `sent`,
  `answered` or `failed`. Deleting the conversation deletes its lookups.
- `ck_assistant_message_origin` gains `cloud`.

The outgoing text and the sources are encrypted with `IEncryptionService`, like message content.
The answer is an `assistant_message` with origin `cloud`; its `meta` holds the lookup id and
nothing else. Lookups are own-only: repository methods take the caller's person id and are named
`...Own...`, and there is no `personId` on any route.

Every send writes an `ai_call` row with purpose `assistant_lookup` (prompt hash, tokens, latency,
ok or the failure reason; never the text) and, when the local model words the "applied" message,
one with purpose `assistant_lookup_applied`. A draft the local model rewords writes one with
purpose `assistant_lookup_draft`.

Audit actions, which say that something happened and never what was said:

- `assistant.lookup_enabled`, `assistant.lookup_disabled`
- `assistant.lookup_sent` (mode, provider, model, character count)
- `assistant.lookup_deleted`

## Settings

| `.env` | Setting | Default | |
| --- | --- | --- | --- |
| `SELFKEPT_LOOKUP_ENABLED` | `Lookup:Enabled` | `false` | The server switch |
| `SELFKEPT_LOOKUP_API_KEY` | `Lookup:ApiKey` | none | The provider key. A file named `Lookup__ApiKey` in the secrets folder works too |
| `SELFKEPT_LOOKUP_MODEL` | `Lookup:Model` | `claude-opus-5-5` | |

`Lookup:MaxSearches` (5) and `Lookup:TimeoutSeconds` (180) have no `.env` name. The lookup is
usable when it is enabled and a key is set. The key is never logged, never returned by any route
and never shown in the app.

## API

All routes need a signed-in user, act on the caller's own data and answer 404 when the server
switch is off.

### GET /api/assistant/lookup

```json
{ "available": true, "enabled": false, "provider": "Anthropic", "model": "claude-opus-5-5", "localModel": false }
```

`localModel` tells the app whether the "applied locally" step will run.

### PUT /api/assistant/lookup

```json
{ "enabled": true, "acknowledged": true }
```

Turning it on without `acknowledged: true` is a 422. Turning it off needs no acknowledgement.
Returns the same shape as the GET.

### POST /api/assistant/lookups/draft

```json
{ "messageId": "…", "mode": "anonymized" }
```

`messageId` is one of the caller's own user messages. Nothing is sent and nothing is stored.

```json
{
  "text": "Does magnesium glycinate help with sleep in adults in their 40s?",
  "mode": "anonymized",
  "changes": [ { "kind": "name", "count": 1 }, { "kind": "number", "count": 2 } ],
  "reworded": false,
  "provider": "Anthropic",
  "model": "claude-opus-5-5"
}
```

`kind` is one of `name`, `email`, `phone`, `link`, `date`, `age`, `number`, `record` (the "About
me" block was added). 409 when the person switch is off. 422 when the message is a red flag.

### POST /api/assistant/lookups

```json
{ "messageId": "…", "mode": "anonymized", "text": "…", "acknowledged": true }
```

`text` is what the person saw, 1 to 4000 characters. 422 without `acknowledged: true`, 422 when
the message or the text is a red flag, and 422 with `findings` (same shape as `changes`) when the
text holds a hard identifier. 409 when the person switch is off. The route allows 5 sends a
minute per person (429 beyond that).

The answer is a `text/event-stream`, like a chat reply:

| Event | Data | When |
| --- | --- | --- |
| `sent` | `{ "lookupId": "…" }` | The request has gone to the provider |
| `waiting` | `{}` | Every 10 seconds while the provider works, to keep the stream open |
| `done` | `{ "lookupId": "…", "message": AssistantMessage, "applied": AssistantMessage or null }` | The answer is stored |
| `failed` | `{ "lookupId": "…", "reason": "refused" or "cut_off" or "search_failed" or "unreachable" or "rate_limited" or "key_rejected", "text": "…" }` | Nothing was stored as an answer |

### GET /api/assistant/lookups

The caller's own lookups, newest first, up to 100.

```json
[ { "id": "…", "conversationId": "…", "mode": "anonymized", "status": "answered", "provider": "Anthropic", "model": "claude-opus-5-5", "text": "…", "sentAt": "2026-11-11T03:10:00Z" } ]
```

### DELETE /api/assistant/lookups/{id}

Deletes the lookup row and its outgoing text. The answer stays in the conversation as a message
with no "What was sent" row. The sources are stored on the lookup row, so the kept answer has no
sources either (`sources` empty, `lookupId` null). 204, or 404 when it is not the caller's.

### Changes to existing shapes

- `AssistantMessage` gains `sources` (`[{ "title": "…", "url": "…" }]`, empty unless origin is
  `cloud`) and `lookupId` (null unless origin is `cloud` and the lookup still exists).
  `origin` gains the value `cloud`.
- The chat `done` event gains `lookup`: `{ "offered": true, "reason": "question" }` or null.
  `reason` is `question`, `refused` (the guard replaced the reply) or `model_off`.

## Left out on purpose

- Lookup answers do not enter the playbook. The playbook stays a reviewed list in code.
- No lookup from the check-in, the morning brief, labs or insights.
- No conversation with the cloud model. One question, one answer.
- No second provider and no OpenAI-compatible endpoint.
- No admin view of what others sent, and no household-wide switch that turns it on for people.
