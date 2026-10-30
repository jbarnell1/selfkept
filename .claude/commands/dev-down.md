---
description: Stop the local stack (Flutter, API, and stop the Postgres container; --prune removes it and its data).
argument-hint: "[--prune]"
---

# Tear down the dev stack

Run `scripts/dev-down.sh $ARGUMENTS` from the repo root and confirm ports 3000 and 5000 are free.

- Without flags the `selfkept-pg` container is stopped, not removed, so the data is still there
  next time.
- `--prune` removes the container and its volume. Only when the user asked for a fresh database.
