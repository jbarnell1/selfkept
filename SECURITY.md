# Security policy

SelfKept holds a household's health data on a machine the household owns. A security problem in
it is a problem in someone's home, so reports get answered first.

## Reporting a problem

Please report security problems privately, not in a public issue. Use this repository's
**Security** tab, then **Report a vulnerability** (GitHub private vulnerability reporting).

Include what you found, how to reproduce it, and which version you tested
(`GET /api/system/version` on your server shows it). You will get a reply within 3 days, updates
while it is fixed, and credit in the release notes if you want it.

## Scope

- This repository: the server, the web and mobile app, the self-host image and compose files,
  and the GitHub Actions workflows.
- There is no hosted service to test against. Test on your own install.

## Out of scope

- Problems that need physical access to the host or a compromised Tailscale account.
- Running the app on the open internet with `SELFKEPT_BIND=0.0.0.0` and no HTTPS. The
  documented setup is Tailscale (or Caddy with a certificate).

## Supported versions

Security fixes go into the latest release. Update with
`docker compose pull && docker compose up -d`. Each published image carries a signed build
provenance record: `gh attestation verify oci://ghcr.io/jbarnell1/selfkept:<version> --owner jbarnell1`.
