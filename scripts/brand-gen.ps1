# Regenerates every brand-derived file from brand/brand.yaml.
# Same outputs and logic as brand-gen.sh, written for Windows PowerShell 5.1 and later.
#
# Outputs:
#   flutter/lib/brand/brand.dart                       const Brand class (committed)
#   backend/src/SelfKept.Api/appsettings.Brand.json    Brand configuration section
#   flutter platform identity (Android applicationId and label, iOS bundle id and
#   display name, web manifest and index title), patched in place when present.
$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Source = Join-Path $Root 'brand\brand.yaml'
$Utf8 = New-Object System.Text.UTF8Encoding($false)

function Strip-Comment([string]$line) {
    $out = New-Object System.Text.StringBuilder
    $quote = $null
    foreach ($ch in $line.ToCharArray()) {
        if ($quote) {
            [void]$out.Append($ch)
            if ($ch -eq $quote) { $quote = $null }
        } elseif ($ch -eq '"' -or $ch -eq "'") {
            $quote = $ch
            [void]$out.Append($ch)
        } elseif ($ch -eq '#') {
            break
        } else {
            [void]$out.Append($ch)
        }
    }
    return $out.ToString().TrimEnd()
}

function Unquote([string]$value) {
    $v = $value.Trim()
    if ($v.Length -ge 2 -and $v[0] -eq $v[$v.Length - 1] -and ($v[0] -eq '"' -or $v[0] -eq "'")) {
        return $v.Substring(1, $v.Length - 2)
    }
    return $v
}

function Parse-Brand([string]$text) {
    $data = [ordered]@{}
    $section = $null
    foreach ($raw in ($text -split "`r?`n")) {
        $line = Strip-Comment $raw
        if (-not $line.Trim()) { continue }
        $indent = $line.Length - $line.TrimStart(' ').Length
        $trimmed = $line.Trim()
        $idx = $trimmed.IndexOf(':')
        if ($idx -lt 0) { throw "brand-gen: cannot parse line: $raw" }
        $key = $trimmed.Substring(0, $idx).Trim()
        $value = $trimmed.Substring($idx + 1)
        if ($indent -eq 0) {
            if ($value.Trim() -eq '') {
                $section = [ordered]@{}
                $data[$key] = $section
            } else {
                $section = $null
                $data[$key] = Unquote $value
            }
        } else {
            if ($null -eq $section) { throw "brand-gen: unexpected nesting: $raw" }
            $section[$key] = Unquote $value
        }
    }
    return $data
}

$brand = Parse-Brand ([System.IO.File]::ReadAllText($Source, $Utf8))

$required = @(
    'product_name', 'short_name', 'tagline', 'codename', 'support_url', 'docs_url',
    'privacy_url', 'from_email', 'copyright_holder', 'ios_bundle_id',
    'android_application_id', 'deep_link_scheme', 'docker_image', 'env_prefix', 'release_url'
)
$missing = @($required | Where-Object { -not $brand.Contains($_) })
if ($missing.Count -gt 0) { throw "brand-gen: brand.yaml is missing $($missing -join ', ')" }
$colors = $brand['colors']
$typography = $brand['typography']
if ($null -eq $colors -or $colors.Count -eq 0) { throw 'brand-gen: brand.yaml needs a colors section' }
if ($null -eq $typography -or $typography.Count -eq 0) { throw 'brand-gen: brand.yaml needs a typography section' }
foreach ($name in $colors.Keys) {
    if ($colors[$name] -notmatch '^#[0-9A-Fa-f]{6}$') { throw "brand-gen: colour $name must be #RRGGBB, got '$($colors[$name])'" }
}

function To-Camel([string]$snake) {
    $parts = $snake -split '_'
    if ($parts.Length -lt 2) { return $snake }
    $rest = $parts[1..($parts.Length - 1)] | ForEach-Object { if ($_.Length -gt 0) { $_.Substring(0, 1).ToUpper() + $_.Substring(1) } }
    return $parts[0] + ($rest -join '')
}

function To-Pascal([string]$snake) {
    return (($snake -split '_') | ForEach-Object { if ($_.Length -gt 0) { $_.Substring(0, 1).ToUpper() + $_.Substring(1) } }) -join ''
}

function Dart-Str([string]$value) {
    return "'" + $value.Replace('\', '\\').Replace("'", "\'").Replace('$', '\$') + "'"
}

function Const-Line($name, $literal) {
    # A Dart const that stays within 80 columns, wrapped the way dart format wraps it.
    $one = "  static const String $name = $literal;"
    if ($one.Length -le 80) { return $one }
    return "  static const String $name =`n      $literal;"
}

$changed = New-Object System.Collections.ArrayList

function Write-IfChanged([string]$path, [string]$content) {
    $dir = Split-Path -Parent $path
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
    if ((Test-Path $path) -and ([System.IO.File]::ReadAllText($path, $Utf8) -eq $content)) { return }
    [System.IO.File]::WriteAllText($path, $content, $Utf8)
    [void]$changed.Add($path.Substring($Root.Length + 1).Replace('\', '/'))
}

function Patch-File([string]$path, [scriptblock]$transform) {
    if (-not (Test-Path $path)) { return }
    $before = [System.IO.File]::ReadAllText($path, $Utf8)
    $after = & $transform $before
    if ($after -ne $before) {
        [System.IO.File]::WriteAllText($path, $after, $Utf8)
        [void]$changed.Add($path.Substring($Root.Length + 1).Replace('\', '/'))
    }
}

# ---- flutter/lib/brand/brand.dart -------------------------------------------
$stringFields = @(
    @('productName', 'product_name'), @('shortName', 'short_name'), @('tagline', 'tagline'),
    @('codename', 'codename'), @('supportUrl', 'support_url'), @('docsUrl', 'docs_url'),
    @('privacyUrl', 'privacy_url'), @('copyrightHolder', 'copyright_holder'),
    @('deepLinkScheme', 'deep_link_scheme'), @('universalLinkHost', 'universal_link_host'),
    @('iosBundleId', 'ios_bundle_id'), @('androidApplicationId', 'android_application_id'),
    @('envPrefix', 'env_prefix'), @('releaseUrl', 'release_url'),
    @('androidAppUrl', 'android_app_url'), @('iosAppUrl', 'ios_app_url')
)
$lines = New-Object System.Collections.ArrayList
[void]$lines.Add('// GENERATED by scripts/brand-gen.sh from brand/brand.yaml. Do not edit by hand.')
[void]$lines.Add('// Every product string, colour token and identity value comes from here.')
[void]$lines.Add('')
[void]$lines.Add("import 'package:flutter/material.dart';")
[void]$lines.Add('')
[void]$lines.Add('/// Product identity. Screens, strings and themes must read from this class')
[void]$lines.Add('/// and never spell the product name themselves.')
[void]$lines.Add('abstract final class Brand {')
foreach ($pair in $stringFields) {
    $value = if ($brand.Contains($pair[1])) { $brand[$pair[1]] } else { '' }
    [void]$lines.Add((Const-Line $pair[0] (Dart-Str $value)))
}
[void]$lines.Add('')
[void]$lines.Add('  /// Name of the HttpOnly refresh cookie the server sets on web.')
[void]$lines.Add("  static const String refreshCookieName = $(Dart-Str ($brand['codename'] + '_refresh'));")
[void]$lines.Add('')
[void]$lines.Add('  // Palette tokens. Light: forest on cream. Dark: moss on charcoal.')
foreach ($name in $colors.Keys) {
    [void]$lines.Add("  static const Color $(To-Camel $name) = Color(0xFF$($colors[$name].Substring(1).ToUpper()));")
}
[void]$lines.Add('')
[void]$lines.Add('  // Typography family names, resolved through google_fonts.')
foreach ($name in $typography.Keys) {
    [void]$lines.Add("  static const String $(To-Camel $name)Family = $(Dart-Str $typography[$name]);")
}
[void]$lines.Add('}')
[void]$lines.Add('')
Write-IfChanged (Join-Path $Root 'flutter\lib\brand\brand.dart') ($lines -join "`n")

# ---- backend appsettings.Brand.json ------------------------------------------
# Hand-built JSON so the key order and 2-space indentation match brand-gen.sh exactly.
function Json-Str([string]$value) {
    return '"' + $value.Replace('\', '\\').Replace('"', '\"') + '"'
}
$j = New-Object System.Collections.ArrayList
[void]$j.Add('{')
[void]$j.Add('  "Brand": {')
foreach ($key in ($required + @('universal_link_host', 'android_app_url', 'ios_app_url'))) {
    $value = if ($brand.Contains($key)) { $brand[$key] } else { '' }
    [void]$j.Add("    $(Json-Str (To-Pascal $key)): $(Json-Str $value),")
}
[void]$j.Add('    "Colors": {')
$colorKeys = @($colors.Keys)
for ($i = 0; $i -lt $colorKeys.Count; $i++) {
    $comma = if ($i -lt $colorKeys.Count - 1) { ',' } else { '' }
    [void]$j.Add("      $(Json-Str (To-Pascal $colorKeys[$i])): $(Json-Str $colors[$colorKeys[$i]])$comma")
}
[void]$j.Add('    },')
[void]$j.Add('    "Typography": {')
$typeKeys = @($typography.Keys)
for ($i = 0; $i -lt $typeKeys.Count; $i++) {
    $comma = if ($i -lt $typeKeys.Count - 1) { ',' } else { '' }
    [void]$j.Add("      $(Json-Str (To-Pascal $typeKeys[$i])): $(Json-Str $typography[$typeKeys[$i]])$comma")
}
[void]$j.Add('    }')
[void]$j.Add('  }')
[void]$j.Add('}')
[void]$j.Add('')
Write-IfChanged (Join-Path $Root 'backend\src\SelfKept.Api\appsettings.Brand.json') ($j -join "`n")

# ---- flutter platform identity -----------------------------------------------
$flutter = Join-Path $Root 'flutter'
$product = $brand['product_name']
$short = $brand['short_name']
$iosId = $brand['ios_bundle_id']
$androidId = $brand['android_application_id']
$tagline = $brand['tagline']

Patch-File (Join-Path $flutter 'android\app\build.gradle.kts') {
    param($s)
    [regex]::Replace($s, 'applicationId\s*=\s*"[^"]*"', "applicationId = `"$androidId`"")
}
Patch-File (Join-Path $flutter 'android\app\src\main\AndroidManifest.xml') {
    param($s)
    ([regex]'android:label="[^"]*"').Replace($s, "android:label=`"$product`"", 1)
}
Patch-File (Join-Path $flutter 'ios\Runner\Info.plist') {
    param($s)
    $s = [regex]::Replace($s, '(<key>CFBundleDisplayName</key>\s*<string>)[^<]*(</string>)', { param($m) $m.Groups[1].Value + $product + $m.Groups[2].Value })
    $s = [regex]::Replace($s, '(<key>CFBundleName</key>\s*<string>)[^<]*(</string>)', { param($m) $m.Groups[1].Value + $short + $m.Groups[2].Value })
    $s
}
Patch-File (Join-Path $flutter 'ios\Runner.xcodeproj\project.pbxproj') {
    param($s)
    [regex]::Replace($s, 'PRODUCT_BUNDLE_IDENTIFIER = ([^;]+);', {
        param($m)
        $suffix = if ($m.Groups[1].Value.EndsWith('.RunnerTests')) { '.RunnerTests' } else { '' }
        "PRODUCT_BUNDLE_IDENTIFIER = $iosId$suffix;"
    })
}
Patch-File (Join-Path $flutter 'web\manifest.json') {
    param($s)
    $s = [regex]::Replace($s, '("name":\s*)"[^"]*"', { param($m) $m.Groups[1].Value + (Json-Str $product) })
    $s = [regex]::Replace($s, '("short_name":\s*)"[^"]*"', { param($m) $m.Groups[1].Value + (Json-Str $short) })
    $s = [regex]::Replace($s, '("description":\s*)"[^"]*"', { param($m) $m.Groups[1].Value + (Json-Str $tagline) })
    if ($colors.Contains('cream')) {
        $s = [regex]::Replace($s, '("background_color":\s*)"[^"]*"', { param($m) $m.Groups[1].Value + (Json-Str $colors['cream']) })
    }
    if ($colors.Contains('forest')) {
        $s = [regex]::Replace($s, '("theme_color":\s*)"[^"]*"', { param($m) $m.Groups[1].Value + (Json-Str $colors['forest']) })
    }
    $s
}
Patch-File (Join-Path $flutter 'web\index.html') {
    param($s)
    $s = [regex]::Replace($s, '<title>[^<]*</title>', "<title>$product</title>")
    $s = [regex]::Replace($s, '(<meta name="apple-mobile-web-app-title" content=")[^"]*(")', { param($m) $m.Groups[1].Value + $product + $m.Groups[2].Value })
    $s = [regex]::Replace($s, '(<meta name="description" content=")[^"]*(")', { param($m) $m.Groups[1].Value + $tagline + $m.Groups[2].Value })
    $s
}

# ---- self-host installer ------------------------------------------------------
$installerBlock = @(
    '# >>> brand (scripts/brand-gen) >>>',
    "PRODUCT_NAME=`"$product`"",
    "CODENAME=`"$($brand['codename'])`"",
    "ENV_PREFIX=`"$($brand['env_prefix'])`"",
    "IMAGE=`"$($brand['docker_image'])`"",
    "RELEASE_URL=`"$($brand['release_url'])`"",
    '# <<< brand <<<'
) -join "`n"
Patch-File (Join-Path $Root 'selfhost\install.sh') {
    param($s)
    [regex]::Replace($s, '(?s)# >>> brand \(scripts/brand-gen\) >>>.*?# <<< brand <<<', { param($m) $installerBlock })
}

if ($changed.Count -gt 0) {
    Write-Output ("brand-gen: updated " + ($changed -join ', '))
} else {
    Write-Output 'brand-gen: everything already up to date'
}

