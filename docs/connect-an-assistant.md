# Connecting an assistant

Assistants that speak MCP (Open WebUI, Claude, ChatGPT and others) can read your health data on your server and answer questions about it. Each person connects their own account and sees only their own data, plus whatever a household member has chosen to share with them. Assistants can read, and they can keep notes you ask them to remember. They can never change your health data.

The server must know the address people use to reach it. Set `App:BaseUrl` (in the self-host `.env`, `SELFKEPT_ORIGIN`) to your Tailscale address, for example `https://box.tail1234.ts.net`. Without it, assistant connections stay off and the server log says so.

The MCP address is that origin plus `/mcp`. The app shows it under You, Assistants, with a copy button.

## Open WebUI (recommended)

Open WebUI 0.6.31 or later talks MCP natively; these steps were checked against 0.12.

1. Set `WEBUI_SECRET_KEY` on Open WebUI, and keep it. Sign-ins are stored encrypted with it, so
   a new key on every start drops them.
2. Make sure Open WebUI can reach your server at the address the app shows (it must resolve your
   Tailscale name; with Docker on Linux, host networking or a container with Tailscale does it).
3. As the Open WebUI admin: Settings, Admin, Integrations, External Tool Servers, Add Connection.
   - Type: MCP (Streamable HTTP)
   - URL: your MCP address
   - Authentication: OAuth 2.1
   - Click Register Client, then Save. Open WebUI registers itself with the server.
4. Give the model room for the tools. Tool descriptions take about 2,500 tokens, and Ollama's
   default 4K context cuts the answer off after its first word. Set the model's context length
   to 16K or more (Admin, Models, the model, Advanced Params), or create a variant in Ollama:
   `FROM qwen3.5:9b` and `PARAMETER num_ctx 16384` in a Modelfile, then
   `ollama create qwen3.5-9b-tools -f Modelfile`. A 9B model with tool calling is enough.
5. Each person, signed in to Open WebUI as themselves, turns the tool on in a chat (the
   Integrations button, Tools) and follows the sign-in prompt. A page from your server asks them
   to allow the connection; it shows which app is asking and where it will send them back.
6. Ask something like "How did I sleep last night compared with my usual, and is anything in my
   latest blood test worth a look?"

Do not make the tool a default tool for everyone: Open WebUI cannot start a sign-in in the middle
of a reply.

## Claude and ChatGPT

Add a custom connector with your MCP address. They identify themselves with a published metadata document, so there is nothing to register. The same consent page appears. Your server must be reachable from them, which with Tailscale only means the Claude or ChatGPT app on a device that is on your tailnet; their cloud services cannot reach a tailnet-only server.

## Access tokens

For an assistant that only takes a bearer token: You, Assistants, Create token. The token is shown once. Paste it as the bearer token in the assistant's MCP settings. Turn off "Let it remember notes" for a read-only token. Revoke it from the same screen.

In Open WebUI, Bearer authentication applies to everyone who uses that tool server, so a token there would let every Open WebUI user read the token owner's data. Use OAuth 2.1 in Open WebUI; keep tokens for single-person setups.

## What an assistant can do

| Tool | What it reads |
| --- | --- |
| `get_today` | Today's sentence, readiness against your own range, up to two insight cards, last check-in |
| `list_metrics` | Which metrics have data, with their keys |
| `get_daily_summary` | One value per day for a metric, up to 90 days |
| `get_trend` | 28 or 90 days with your own usual band |
| `compare_periods` | One metric across two windows (for "since my last blood test") |
| `get_insights` | Insight cards with severity and your reactions |
| `get_checkins` | Mood check-ins and notes, with text search |
| `get_sources` | Your devices and when they last synced |
| `get_lab_markers` | Latest blood results with ranges and flags, and the last panel date |
| `get_lab_history` | Every result for one marker, with what it is and what moves it |
| `recall_memory` | Notes assistants were asked to keep |
| `remember` | Keeps one note (needs the memory permission) |

Most tools take an optional `person`: a household member's name. That only works for categories they have shared with you under You, Sharing.

Every call is written to the audit log with the tool, the assistant and how many rows it returned, never the values. You, Assistants lists connected assistants with Disconnect, your tokens with Revoke, and everything assistants remember with Forget.

## If you need a stronger model

Everything above keeps your data on your server and in the model you run. When a question needs a model you do not host, use a privacy bundle (planned): pick categories and dates, choose how much to redact, preview exactly what leaves, and paste the file into the model yourself.
