# Contributing to SelfKept

Bug reports, ideas and pull requests are welcome. The full guide is
[docs/contributing.md](docs/contributing.md); this page is the short version.

- **Bugs and ideas:** open an issue with the templates. Remove names, emails, tokens and real
  health values from logs and screenshots first.
- **Security problems:** not in a public issue. See [SECURITY.md](SECURITY.md).
- **Set up:** the .NET 10 SDK, Flutter 3.44 (stable) and Docker, then `scripts/dev-up.sh`.
- **Branches:** pull requests target the open `release/x.y.z` branch, not `main`.
- **Checked on every pull request:** tests for what you changed, `dotnet test`,
  `flutter analyze`, `flutter test` and a web build ("CI Complete" must be green).
- **House rules:** no em dashes, no hard-coded brand strings, access control in the repository
  layer, additive SQL migrations, and nothing that sends data off the host.
- **Conduct:** the [Code of Conduct](CODE_OF_CONDUCT.md) applies everywhere in the project.

By contributing you agree that your contribution is licensed under AGPL-3.0-only, the same as the
project.
