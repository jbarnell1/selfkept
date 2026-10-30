---
paths:
  - flutter/**/*.dart
  - flutter/pubspec.yaml
---
# Flutter rules

Flutter 3.44 stable, one app for iOS, Android and web. `flutter/build/web` is what the self-host
image serves from the API's `wwwroot`.

## Stack
- State: Provider + ChangeNotifier only. No Riverpod, GetX, Bloc.
- HTTP: Dio. The parameter is `queryParameters:`, not `queryParams`.
- Routing: GoRouter with an auth redirect guard.
- Platform checks: `kIsWeb` and `defaultTargetPlatform` from `package:flutter/foundation.dart`.
  Never import `dart:io` in a file that can load on web.
- Tokens: `flutter_secure_storage`. Never `SharedPreferences` for auth tokens. On web the refresh
  token is an HttpOnly cookie set by the server; the client keeps only the access token in memory.

## Server address
There is no fixed server. `API_BASE_URL` is a `--dart-define`:
- `same-origin` (the self-host web build): call `/api` on whatever origin served the page.
- a URL (dev: `http://localhost:5000`): call that.
- absent on mobile: the user types their server address (their Tailscale name, for example
  `https://box.tailnet.ts.net`) in the app's server screen; it is stored in secure storage and
  checked with `GET /api/system/version` before sign-in. A wrong address is a clear error, not a hang.

## Design language
Colours and fonts come from `brand/brand.yaml` through the generated theme file
(`scripts/brand-gen.*` writes it; do not edit the output by hand, do not type hex values in
widgets). The tokens:

| Token | Light | Dark | Use |
|---|---|---|---|
| forest | `#1F4D3A` | primary deep green | primary |
| moss | `#2E6B4F` | dark-mode primary | hover, dark primary |
| sage | `#8FB39A` | | soft accents, bands |
| cream / cream_2 | `#F7F3EA` / `#EFE9DB` | | light background, light surface 2 |
| ink / ink_soft | `#1C2321` / `#5C6660` | | light text, secondary text |
| amber | `#C8912B` | | attention ("watch") |
| terracotta | `#B4654A` | | clinician tier; never red for this |
| charcoal / charcoal_2 | | `#161A18` / `#1F2522` | dark background, dark surface 2 |
| linen | | `#ECE6D8` | dark text |

Type: Fraunces for display, Inter for UI, JetBrains Mono for numbers and data. Both light and dark
themes ship; every new widget is checked in both. No red for health states: amber is "watch",
terracotta is the clinician tier.

## Copy and brand
No product name in string literals; use the brand constants. No em dashes anywhere in copy. Plain
sentences, no exclamation marks, no marketing voice.

## Models and JSON
`fromJson` accepts both camelCase and PascalCase keys. Enums get `fromString()`, `displayName`
and a `toJson` value; never compare `.index`. Keep client-side computation small: if a page
does multi-step data work, ask whether the API should return it instead.

## Layout
`flutter/lib/`: `config/`, `models/`, `pages/` (`pages/public/` for unauthenticated pages),
`providers/`, `router/`, `services/`, `theme/` (generated tokens plus the ThemeData builders),
`utils/`, `widgets/`.

## Health data on device
Apple Health and Health Connect collectors read on the device and send to the user's own server
through `POST /api/ingest/batch` with a stable `sourceUid` per sample so a retry never duplicates.
Nothing is sent anywhere else. No analytics or crash SDKs.

## Before submitting
`dart format .`, `flutter analyze`, `flutter test`: zero warnings, zero failures. The post-edit
hook formats on save; CI fails on unformatted files.
