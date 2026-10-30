---
description: Start the local stack (Postgres container, API on :5000, Flutter web on :3000) and report the URLs.
argument-hint: "[--api-only] [--no-pg]"
---

# Bring up the dev stack

1. From the repo root run `scripts/dev-up.sh $ARGUMENTS`.
   - Default: Postgres container `selfkept-pg`, the API with `--environment Development` on
     `http://localhost:5000`, Flutter web on `http://localhost:3000` pointed at the API.
   - `--api-only` skips Flutter. `--no-pg` assumes a Postgres is already listening.
   - First run pulls the pgvector image, restores NuGet and pub packages and runs migrations.
     A minute or two is normal, not a hang.
2. Report the printed card: API URL, web URL, database port, where the logs are (`.dev/`).
3. If the API health check times out, show the last 30 lines of `.dev/api.log`, name the real
   error and stop. Do not retry blindly.
4. Open `http://localhost:3000` in the browser pane with the `web` launch configuration when the
   user wants to see it.
