---
description: Check /api/system/version and /api/system/health against the dev API (or a given base URL, for example a self-host install).
argument-hint: "[base-url]"
---

# Smoke check

1. Run `scripts/smoke.sh $ARGUMENTS` (default base URL `http://localhost:5000`; pass
   `http://127.0.0.1:8080` for a local self-host stack, or a Tailscale address).
2. Report the version, the brand block (`productName` should match `brand/brand.yaml`), and the
   health status. `database` must be `ok`.
3. On failure show the HTTP status and body, and the last lines of `.dev/api.log` if the dev
   stack is the target. Never report success when a check failed.
