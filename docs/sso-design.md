# Single sign-on

Signing in through the household's own OpenID Connect provider: Pocket ID, Authelia, Authentik or
any other that publishes a discovery document. It sits beside passwords, not in place of them, and
is off until the owner sets it up.

## What it is for

A household that already runs a provider signs in to everything with one passkey. Nobody has to
remember (or lose) a separate password for this server.

## The rules

- **A provider login reaches only the account it is tied to.** `user_identity` holds one
  `(issuer, subject)` per account. The ID token's `sub` is the provider's stable id for the
  person; the email is kept for display only.
- **Tying a login to an account** happens in one of two ways:
  - The person, signed in, chooses Connect under You, Account and signs in at the provider.
  - On a first provider sign-in, when the provider marks the email verified (`email_verified`)
    and exactly that address has an account here with no login from this provider yet.
- **An unverified email never finds an account.** In many providers people can edit their own
  address, so a matching email alone would let one household member claim another's account.
- **No account is ever created here.** Accounts still come from first-run setup or an invite. A
  provider login with no account lands on the sign-in page with a sentence saying how to connect.
- **Moving a login needs Disconnect first.** A login tied to one account cannot be connected to
  another, and an account already tied to a login at a provider is never silently re-tied.

## The trip

Authorization code flow with PKCE (S256) and a nonce, client authentication
`client_secret_basic`. The server is the client; the provider never talks to the apps.

1. The sign-in button navigates to `GET /api/auth/sso/start?client=web|mobile`. The server stores
   a state (hashed), the nonce and the PKCE verifier (encrypted) for ten minutes and redirects to
   the provider. Connect does the same through `POST /api/auth/sso/link`, which returns the URL
   so the signed-in app can open it.
2. The provider sends the browser to `GET /api/auth/sso/callback`. The state is single use. The
   server trades the code for tokens and validates the ID token: RS256 signature against the
   provider's keys, issuer, audience (the client id), lifetime with two minutes of skew, nonce.
   A token naming a key the cache does not hold makes the server fetch the keys once more (a
   rotation).
3. Where the answer goes:
   - **Web sign-in:** the usual refresh cookie is set and the browser lands on `/`; the app's
     start-up refresh signs it in.
   - **Phone sign-in:** the browser lands on the app link `<scheme>://sso?code=...` (the scheme is
     `deep_link_scheme` in `brand/brand.yaml`). The code is single use, lives two minutes and is
     traded at `POST /api/auth/sso/exchange` for the tokens, so no token is ever in a URL.
   - **Connect:** `/you?sso=linked` on the web, `<scheme>://sso?linked=1` on a phone.
   - **Failure:** `/login?sso=<reason>` (or `/you?sso=<reason>` for Connect), or
     `<scheme>://sso?error=<reason>`. Reasons: `disabled`, `expired`, `cancelled`, `provider`,
     `not_linked`, `linked_elsewhere`.

The discovery document must name the configured authority as its issuer (OpenID Connect Discovery
1.0 section 4.3). It and the keys are cached for an hour.

## Settings

| Setting | `.env` | What |
| --- | --- | --- |
| `Sso:Authority` | `SELFKEPT_SSO_AUTHORITY` | The provider's issuer URL, as browsers reach it. |
| `Sso:ClientId` | `SELFKEPT_SSO_CLIENT_ID` | From registering this server at the provider. |
| `Sso:ClientSecret` | `SELFKEPT_SSO_CLIENT_SECRET` | Or a file `Sso__ClientSecret` in the secrets volume. |
| `Sso:Name` | `SELFKEPT_SSO_NAME` | What the button says ("Sign in with Pocket ID"). |
| `Sso:InternalUrl` | `SELFKEPT_SSO_INTERNAL_URL` | Optional. Where the server itself reaches the provider. |

`InternalUrl` is for a provider this server cannot reach at its public address, for example one on
the same machine behind Tailscale Serve: the discovery document is read from it, and the token and
key addresses it lists under the public address are reached through it. Browsers always use the
public address. The callback to register at the provider is `<App:BaseUrl>/api/auth/sso/callback`.

## Data

`0011_sso`: `user_identity`, `sso_auth_state` (trips in flight) and `sso_handoff` (phone codes
waiting to be collected). Housekeeping removes expired states and handoffs. Linking and
unlinking are audited (`auth.sso.linked`, `auth.sso.unlinked`), and `auth.login` records whether a
session came from a password or the provider.
