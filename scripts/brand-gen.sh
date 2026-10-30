#!/usr/bin/env bash
# Regenerates every brand-derived file from brand/brand.yaml.
#
# Outputs:
#   flutter/lib/brand/brand.dart                       const Brand class (committed)
#   backend/src/SelfKept.Api/appsettings.Brand.json    Brand configuration section
#   flutter platform identity (Android applicationId and label, iOS bundle id and
#   display name, web manifest and index title), patched in place when present.
#
# The YAML is flat with one level of nesting, so it is parsed here without
# PyYAML; the only requirement is a Python 3 interpreter. brand-gen.ps1 holds the
# same logic for PowerShell.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if command -v python3 >/dev/null 2>&1; then
  PY=python3
elif command -v python >/dev/null 2>&1; then
  PY=python
else
  echo "brand-gen: python3 is required" >&2
  exit 1
fi

"$PY" -I - "$ROOT" <<'PYEOF'
import json
import re
import sys
from pathlib import Path

root = Path(sys.argv[1])
source = root / "brand" / "brand.yaml"


def strip_comment(line):
    out = []
    quote = None
    for ch in line:
        if quote:
            out.append(ch)
            if ch == quote:
                quote = None
        elif ch in "\"'":
            quote = ch
            out.append(ch)
        elif ch == "#":
            break
        else:
            out.append(ch)
    return "".join(out).rstrip()


def unquote(value):
    value = value.strip()
    if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
        return value[1:-1]
    return value


def parse(text):
    data = {}
    section = None
    for raw in text.splitlines():
        line = strip_comment(raw)
        if not line.strip():
            continue
        indent = len(line) - len(line.lstrip(" "))
        key, sep, value = line.strip().partition(":")
        if not sep:
            raise SystemExit(f"brand-gen: cannot parse line: {raw}")
        key = key.strip()
        if indent == 0:
            if value.strip() == "":
                section = {}
                data[key] = section
            else:
                section = None
                data[key] = unquote(value)
        else:
            if section is None:
                raise SystemExit(f"brand-gen: unexpected nesting: {raw}")
            section[key] = unquote(value)
    return data


brand = parse(source.read_text(encoding="utf-8"))

required = [
    "product_name", "short_name", "tagline", "codename", "support_url", "docs_url",
    "privacy_url", "from_email", "copyright_holder", "ios_bundle_id",
    "android_application_id", "deep_link_scheme", "docker_image", "env_prefix", "release_url",
]
# May be empty: no public host, no store listing yet.
optional = ["universal_link_host", "android_app_url", "ios_app_url"]
missing = [k for k in required if k not in brand]
if missing:
    raise SystemExit(f"brand-gen: brand.yaml is missing {', '.join(missing)}")
colors = brand.get("colors")
typography = brand.get("typography")
if not isinstance(colors, dict) or not colors:
    raise SystemExit("brand-gen: brand.yaml needs a colors section")
if not isinstance(typography, dict) or not typography:
    raise SystemExit("brand-gen: brand.yaml needs a typography section")
for name, value in colors.items():
    if not re.fullmatch(r"#[0-9A-Fa-f]{6}", value):
        raise SystemExit(f"brand-gen: colour {name} must be #RRGGBB, got {value!r}")


def camel(snake):
    parts = snake.split("_")
    return parts[0] + "".join(p[:1].upper() + p[1:] for p in parts[1:])


def pascal(snake):
    return "".join(p[:1].upper() + p[1:] for p in snake.split("_"))


def dart_str(value):
    return "'" + value.replace("\\", "\\\\").replace("'", "\\'").replace("$", "\\$") + "'"

def const_line(name, literal):
    """A Dart const that stays within 80 columns, wrapped the way dart format wraps it."""
    one = f"  static const String {name} = {literal};"
    if len(one) <= 80:
        return one
    return f"  static const String {name} =\n      {literal};"



changed = []


def write(path, content):
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists() and path.read_text(encoding="utf-8") == content:
        return
    path.write_text(content, encoding="utf-8", newline="\n")
    changed.append(path.relative_to(root).as_posix())


def patch(path, transform):
    if not path.exists():
        return
    before = path.read_text(encoding="utf-8")
    after = transform(before)
    if after != before:
        path.write_text(after, encoding="utf-8", newline="\n")
        changed.append(path.relative_to(root).as_posix())


# ---- flutter/lib/brand/brand.dart -------------------------------------------
string_fields = [
    ("productName", "product_name"),
    ("shortName", "short_name"),
    ("tagline", "tagline"),
    ("codename", "codename"),
    ("supportUrl", "support_url"),
    ("docsUrl", "docs_url"),
    ("privacyUrl", "privacy_url"),
    ("copyrightHolder", "copyright_holder"),
    ("deepLinkScheme", "deep_link_scheme"),
    ("universalLinkHost", "universal_link_host"),
    ("iosBundleId", "ios_bundle_id"),
    ("androidApplicationId", "android_application_id"),
    ("envPrefix", "env_prefix"),
    ("releaseUrl", "release_url"),
    ("androidAppUrl", "android_app_url"),
    ("iosAppUrl", "ios_app_url"),
]
lines = [
    "// GENERATED by scripts/brand-gen.sh from brand/brand.yaml. Do not edit by hand.",
    "// Every product string, colour token and identity value comes from here.",
    "",
    "import 'package:flutter/material.dart';",
    "",
    "/// Product identity. Screens, strings and themes must read from this class",
    "/// and never spell the product name themselves.",
    "abstract final class Brand {",
]
for field, key in string_fields:
    lines.append(const_line(field, dart_str(brand.get(key, ''))))
lines.append("")
lines.append("  /// Name of the HttpOnly refresh cookie the server sets on web.")
lines.append(f"  static const String refreshCookieName = {dart_str(brand['codename'] + '_refresh')};")
lines.append("")
lines.append("  // Palette tokens. Light: forest on cream. Dark: moss on charcoal.")
for name, value in colors.items():
    lines.append(f"  static const Color {camel(name)} = Color(0xFF{value[1:].upper()});")
lines.append("")
lines.append("  // Typography family names, resolved through google_fonts.")
for name, value in typography.items():
    lines.append(f"  static const String {camel(name)}Family = {dart_str(value)};")
lines.append("}")
lines.append("")
write(root / "flutter" / "lib" / "brand" / "brand.dart", "\n".join(lines))

# ---- backend appsettings.Brand.json ------------------------------------------
settings = {"Brand": {}}
for key in required + optional:
    settings["Brand"][pascal(key)] = brand.get(key, "")
settings["Brand"]["Colors"] = {pascal(k): v for k, v in colors.items()}
settings["Brand"]["Typography"] = {pascal(k): v for k, v in typography.items()}
write(
    root / "backend" / "src" / "SelfKept.Api" / "appsettings.Brand.json",
    json.dumps(settings, indent=2) + "\n",
)

# ---- flutter platform identity -----------------------------------------------
flutter = root / "flutter"
product = brand["product_name"]
short = brand["short_name"]
ios_id = brand["ios_bundle_id"]
android_id = brand["android_application_id"]

patch(
    flutter / "android" / "app" / "build.gradle.kts",
    lambda s: re.sub(r'applicationId\s*=\s*"[^"]*"', f'applicationId = "{android_id}"', s),
)
patch(
    flutter / "android" / "app" / "src" / "main" / "AndroidManifest.xml",
    lambda s: re.sub(r'android:label="[^"]*"', f'android:label="{product}"', s, count=1),
)


def patch_plist(s):
    s = re.sub(
        r"(<key>CFBundleDisplayName</key>\s*<string>)[^<]*(</string>)",
        lambda m: m.group(1) + product + m.group(2), s,
    )
    s = re.sub(
        r"(<key>CFBundleName</key>\s*<string>)[^<]*(</string>)",
        lambda m: m.group(1) + short + m.group(2), s,
    )
    return s


patch(flutter / "ios" / "Runner" / "Info.plist", patch_plist)


def patch_pbxproj(s):
    def repl(m):
        suffix = ".RunnerTests" if m.group(1).endswith(".RunnerTests") else ""
        return f"PRODUCT_BUNDLE_IDENTIFIER = {ios_id}{suffix};"

    return re.sub(r"PRODUCT_BUNDLE_IDENTIFIER = ([^;]+);", repl, s)


patch(flutter / "ios" / "Runner.xcodeproj" / "project.pbxproj", patch_pbxproj)


def patch_manifest(s):
    manifest = json.loads(s)
    manifest["name"] = product
    manifest["short_name"] = short
    manifest["description"] = brand["tagline"]
    manifest["background_color"] = colors.get("cream", manifest.get("background_color"))
    manifest["theme_color"] = colors.get("forest", manifest.get("theme_color"))
    return json.dumps(manifest, indent=4) + "\n"


patch(flutter / "web" / "manifest.json", patch_manifest)


def patch_index(s):
    s = re.sub(r"<title>[^<]*</title>", f"<title>{product}</title>", s)
    s = re.sub(
        r'(<meta name="apple-mobile-web-app-title" content=")[^"]*(")',
        lambda m: m.group(1) + product + m.group(2), s,
    )
    s = re.sub(
        r'(<meta name="description" content=")[^"]*(")',
        lambda m: m.group(1) + brand["tagline"] + m.group(2), s,
    )
    return s


patch(flutter / "web" / "index.html", patch_index)

# ---- self-host installer ------------------------------------------------------
installer_block = "\n".join([
    "# >>> brand (scripts/brand-gen) >>>",
    f'PRODUCT_NAME="{product}"',
    f'CODENAME="{brand["codename"]}"',
    f'ENV_PREFIX="{brand["env_prefix"]}"',
    f'IMAGE="{brand["docker_image"]}"',
    f'RELEASE_URL="{brand["release_url"]}"',
    "# <<< brand <<<",
])


def patch_installer(s):
    return re.sub(r"# >>> brand \(scripts/brand-gen\) >>>.*?# <<< brand <<<", lambda m: installer_block, s, flags=re.S)


patch(root / "selfhost" / "install.sh", patch_installer)

if changed:
    print("brand-gen: updated " + ", ".join(changed))
else:
    print("brand-gen: everything already up to date")
PYEOF

