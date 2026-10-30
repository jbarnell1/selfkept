# Downloads the brand typefaces (Fraunces, Inter, JetBrains Mono: all SIL Open
# Font License) from the google/fonts repository into assets/fonts and enables
# the fonts block in pubspec.yaml. Same behaviour as fetch-fonts.sh.
#
# Run once: powershell -ExecutionPolicy Bypass -File flutter\tool\fetch-fonts.ps1
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$FlutterDir = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$FontDir = Join-Path $FlutterDir 'assets\fonts'
$Base = 'https://raw.githubusercontent.com/google/fonts/main/ofl'
New-Item -ItemType Directory -Force $FontDir | Out-Null

$Files = @(
    @('Fraunces.ttf', 'fraunces/Fraunces%5BSOFT,WONK,opsz,wght%5D.ttf'),
    @('Fraunces-Italic.ttf', 'fraunces/Fraunces-Italic%5BSOFT,WONK,opsz,wght%5D.ttf'),
    @('OFL-Fraunces.txt', 'fraunces/OFL.txt'),
    @('Inter.ttf', 'inter/Inter%5Bopsz,wght%5D.ttf'),
    @('Inter-Italic.ttf', 'inter/Inter-Italic%5Bopsz,wght%5D.ttf'),
    @('OFL-Inter.txt', 'inter/OFL.txt'),
    @('JetBrainsMono.ttf', 'jetbrainsmono/JetBrainsMono%5Bwght%5D.ttf'),
    @('JetBrainsMono-Italic.ttf', 'jetbrainsmono/JetBrainsMono-Italic%5Bwght%5D.ttf'),
    @('OFL-JetBrainsMono.txt', 'jetbrainsmono/OFL.txt')
)

foreach ($pair in $Files) {
    $name = $pair[0]
    $path = $pair[1]
    $target = Join-Path $FontDir $name
    if ((Test-Path $target) -and ((Get-Item $target).Length -gt 0)) {
        Write-Output "fetch-fonts: $name already present"
        continue
    }
    Write-Output "fetch-fonts: downloading $name"
    Invoke-WebRequest -Uri "$Base/$path" -OutFile "$target.part" -UseBasicParsing
    Move-Item -Force "$target.part" $target
}

$Pubspec = Join-Path $FlutterDir 'pubspec.yaml'
$content = [System.IO.File]::ReadAllText($Pubspec)
if ($content -match '(?m)^  #FONTS# ') {
    $content = [regex]::Replace($content, '(?m)^  #FONTS# ', '  ')
    [System.IO.File]::WriteAllText($Pubspec, $content, (New-Object System.Text.UTF8Encoding($false)))
    Write-Output 'fetch-fonts: enabled the fonts block in pubspec.yaml'
}

Write-Output "fetch-fonts: done. Run 'flutter pub get' and rebuild."
