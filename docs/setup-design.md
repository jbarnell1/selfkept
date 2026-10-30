# First-run setup

What a person sees the first time they open the app, and how an install gets to a working state
without editing files. The guiding rule: ask what they use, then set up only that, with each
step able to stand on its own.

## The questions

After the first account is created (or anyone joins with an invite), the client sends a signed-in
person whose `setupCompletedAt` is null to `/welcome`:

- What do you wear or use? iPhone or Apple Watch, Android phone or Pixel Watch or Fitbit, Oura
  ring, blood test results.
- Anything else? Someone else in the household (admin only), a private assistant.

On a phone the matching health store is ticked already. "Continue" saves the answers
(`PUT /api/me/setup`, `completed: false`) and opens `/welcome/steps`; nothing ticked, or "Skip for
now", finishes setup at once. The answers live on `user_account.setup_plan`, so they follow the
person to every device.

## The steps

`/welcome/steps` shows one card per answer. Each card reads live state and ticks itself:

| Answer | The card | Done when |
|---|---|---|
| Apple Health / Health Connect | On the matching phone: connect here. Anywhere else: the server address to sign in to, and a link to the phone app. | This phone's collector is connected, or a source of that kind exists. |
| Oura | Connect, or for the admin the one-time application form, or for a member who to ask. | The person's Oura link exists. |
| Blood tests | Add results (the scan and review flow). | Any marker has a value. |
| Household | Create an invite; who joined. | Another member exists. |
| Assistant | The Open WebUI steps and the MCP address. | An assistant or token is connected. |

"Go to Today" finishes setup (`completed: true`). The page stays reachable from You, Setup steps,
for anything left. The router only sends people to `/welcome` when they arrive (sign-in, register,
the splash); inside the app every link works, so a step can open the lab scanner or Assistants.
Oura's round trip comes back to `/welcome/steps` (`returnTo`), and that address survives the
splash and a sign-in.

## The Oura application, from the app

Oura's API needs one application per server. The admin creates it on Oura's developer page and
pastes the client id and secret into the Oura card, which shows the exact redirect address to
register. They are stored in `server_setting`, every value encrypted with the server's key; the
secret is never sent back. `Oura:ClientId` and `Oura:ClientSecret` in the environment still win,
and the app then shows them read-only. Only the household admin may read or change it
(`/api/integrations/oura`), and every change is audited without its values.

## The install

`selfhost/install.sh` is the one command (see `docs/self-host.md`). It is written to be piped from
curl: everything runs from `main` on the last line and nothing reads stdin, so a piped script is
never cut short. Running it again is an upgrade. `selfhost/backup.sh` ships with it.
