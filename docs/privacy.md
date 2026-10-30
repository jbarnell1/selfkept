# Privacy

SelfKept runs on a computer you own. This page says exactly what it stores, where, and what leaves
that computer. The short version: everything stays on your machine unless you export it, and the
software phones home to no one.

## What the app stores

On your server, in the PostgreSQL database (the `pgdata` volume):

- Accounts: email, display name, a password hash (never the password), role, and refresh-token hashes.
- Household: its name, members, pending invite codes (hashed), and the sharing grants each
  person has given to each other member, per category.
- A person profile per account: display name, sex at birth, birth date, units, time zone.
- Health samples the collectors or you add: activity, sleep, vitals, body measurements, lab
  results (including scanned blood panels), cycle data, check-ins, and the insights computed from
  them. Each sample records which source it came from and when.
- Later packages: embeddings of your own data for search and for the MCP server, also in this
  database.

In the `secrets` volume: the database password, the JWT signing key and the encryption key, all
generated on your machine at first run.

In the `appdata` volume: database dumps taken before an upgrade.

On the phones: the access token in secure storage, the server address, and the device's own
health store (Apple Health, Health Connect), which the app reads with your permission and only
uploads to your server.

## Who can see what

Each person sees their own data. Another household member sees a category of yours only after you
grant it, and the check happens in the data layer on every query, not in the screens. The admin
manages the household (name, invites) and does not thereby gain access to anyone's health data.

## What leaves the host

Nothing, unless you cause it:

- **No telemetry, no analytics, no outside crash reporting.** The server and the apps include
  no third-party analytics or tracking SDK and send no usage data anywhere. When the app hits an
  error it reports it to your own server (the error text, a stack trace, the app version, the
  platform and the screen it was on; no health values), where it goes into the server log and
  the admin's Recent problems list. Nothing else receives it.
- **No cloud AI.** Insights are computed on your server. The MCP server is for an assistant you
  run yourself, such as Open WebUI; it is a separate authenticated endpoint and it serves your
  server's data to a client each person allows, nothing more.
- **No update checks.** The server does not contact the project to ask about new versions. You
  upgrade by running the installer again, or with `docker compose pull`.
- **Exports are yours.** When you build an export (a later package: a file download, or a share
  with a clinician), the file goes where you send it, and the export is recorded in your
  account's audit log.
- **Network.** With the recommended setup the app listens on loopback and Tailscale carries the
  traffic inside your tailnet, encrypted end to end. Tailscale's coordination servers see device
  keys and addresses, not your traffic or your data. The web app loads no fonts, scripts or
  images from any other origin.
- **Docker image pulls** reach GitHub Container Registry when you run `docker compose pull`. That
  is a request for the image, not a report about you.

## Deleting

- One account: the admin removes the member; their person, samples and grants are deleted.
- Everything: `docker compose down -v` removes the database, the keys and the backups.

## Questions

Open an issue at <https://github.com/jbarnell1/selfkept>. For a security problem, see
[SECURITY.md](../SECURITY.md).
