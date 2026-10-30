# Start the SelfKept dev stack from PowerShell: Postgres container, API on :5000, Flutter web on :3000.
# Twin of scripts/dev-up.sh. Logs and pids go to .dev\ (ignores itself).
#
#   scripts\dev-up.ps1              # everything
#   scripts\dev-up.ps1 -ApiOnly     # no Flutter
#   scripts\dev-up.ps1 -NoPg        # a Postgres is already listening on $env:SELFKEPT_DEV_PG_PORT (default 5432)
[CmdletBinding()]
param(
  [switch]$ApiOnly,
  [switch]$NoPg
)
$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $PSScriptRoot
$Dev = Join-Path $Root '.dev'
New-Item -ItemType Directory -Force $Dev | Out-Null
$gi = Join-Path $Dev '.gitignore'
if (-not (Test-Path $gi)) { Set-Content -Path $gi -Value '*' -Encoding ascii }

$ApiPort = if ($env:SELFKEPT_DEV_API_PORT) { [int]$env:SELFKEPT_DEV_API_PORT } else { 5000 }
$WebPort = if ($env:SELFKEPT_DEV_WEB_PORT) { [int]$env:SELFKEPT_DEV_WEB_PORT } else { 3000 }
$ApiUrl = "http://localhost:$ApiPort"
$WebUrl = "http://localhost:$WebPort"
$HealthUrl = "$ApiUrl/api/system/health"
$PgContainer = 'selfkept-pg'
$PgVolume = 'selfkept-pg-data'
$PgImage = 'pgvector/pgvector:0.8.0-pg16'

function Test-Port([int]$Port) {
  return [bool](Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue)
}
function Test-Http([string]$Url) {
  try { $r = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 3; return ($r.StatusCode -ge 200 -and $r.StatusCode -lt 400) } catch { return $false }
}
function Wait-Http([string]$Url, [int]$Tries) {
  for ($i = 0; $i -lt $Tries; $i++) { if (Test-Http $Url) { return $true }; Start-Sleep -Seconds 2 }
  return $false
}
function New-RandomKey([int]$Length) {
  $bytes = New-Object byte[] 96
  [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
  return ([Convert]::ToBase64String($bytes) -replace '[/+=]', '').Substring(0, $Length)
}

# 1. Postgres
if ($NoPg) {
  $PgPort = if ($env:SELFKEPT_DEV_PG_PORT) { [int]$env:SELFKEPT_DEV_PG_PORT } else { 5432 }
  Write-Host "[info]   Using the Postgres already on localhost:$PgPort (-NoPg)"
} else {
  docker info *> $null
  if (-not $?) { throw 'Docker is not running.' }
  $exists = docker inspect $PgContainer 2>$null
  if ($LASTEXITCODE -eq 0) {
    $running = docker inspect -f '{{.State.Running}}' $PgContainer
    if ($running -ne 'true') { docker start $PgContainer | Out-Null }
    $PgPort = [int]((docker port $PgContainer 5432/tcp | Select-Object -First 1) -replace '.*:', '')
  } else {
    $PgPort = 5432
    if (Test-Port $PgPort) { $PgPort = 5433 }
    if (Test-Port $PgPort) { throw 'Ports 5432 and 5433 are both busy. Set SELFKEPT_DEV_PG_PORT and use -NoPg, or free one.' }
    Write-Host "[info]   Creating $PgContainer ($PgImage) on 127.0.0.1:$PgPort"
    docker run -d --name $PgContainer -e POSTGRES_USER=selfkept -e POSTGRES_PASSWORD=selfkept -e POSTGRES_DB=selfkept `
      -p "127.0.0.1:${PgPort}:5432" -v "${PgVolume}:/var/lib/postgresql/data" $PgImage | Out-Null
  }
  $ready = $false
  for ($i = 0; $i -lt 30; $i++) {
    docker exec $PgContainer pg_isready -U selfkept -d selfkept *> $null
    if ($LASTEXITCODE -eq 0) { $ready = $true; break }
    Start-Sleep -Seconds 1
  }
  if (-not $ready) { throw "Postgres did not become ready. docker logs $PgContainer" }
  Write-Host "[ok]     Postgres ready on localhost:$PgPort (db/user/password: selfkept)"
}

# 2. Dev keys
$devEnv = Join-Path $Dev 'dev.env'
if (-not (Test-Path $devEnv)) {
  @("Jwt__Key=$(New-RandomKey 64)", "Encryption__Key=$(New-RandomKey 40)") | Set-Content -Path $devEnv -Encoding ascii
  Write-Host '[info]   Generated dev keys in .dev\dev.env'
}
foreach ($line in Get-Content $devEnv) {
  if ($line -match '^([^=]+)=(.*)$') { Set-Item -Path "env:$($Matches[1])" -Value $Matches[2] }
}
$env:ConnectionStrings__SelfKept = "Host=localhost;Port=$PgPort;Database=selfkept;Username=selfkept;Password=selfkept"
$env:SELFKEPT_TEST_CONNECTION = "Host=localhost;Port=$PgPort;Database=postgres;Username=selfkept;Password=selfkept"
$env:ASPNETCORE_URLS = $ApiUrl
$env:App__BaseUrl = $WebUrl
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'

# 3. API
if (Test-Http $HealthUrl) {
  Write-Host "[ok]     API already healthy at $ApiUrl"
} else {
  if (Test-Port $ApiPort) { throw "Port $ApiPort is in use but $HealthUrl does not answer. Run scripts\dev-down.ps1 and retry." }
  Write-Host "[info]   Starting API on $ApiUrl"
  $p = Start-Process -FilePath 'dotnet' -WorkingDirectory $Root -PassThru -WindowStyle Hidden `
    -ArgumentList 'run', '--project', 'backend/src/SelfKept.Api', '--environment', 'Development', '--no-launch-profile' `
    -RedirectStandardOutput (Join-Path $Dev 'api.log') -RedirectStandardError (Join-Path $Dev 'api.err.log')
  Set-Content -Path (Join-Path $Dev 'api.pid') -Value $p.Id
  if (Wait-Http $HealthUrl 90) {
    Write-Host "[ok]     API healthy at $ApiUrl"
  } else {
    Write-Host '[error]  API did not become healthy. Last lines of .dev\api.log:'
    Get-Content (Join-Path $Dev 'api.log') -Tail 30
    Get-Content (Join-Path $Dev 'api.err.log') -Tail 30
    exit 1
  }
}

# 4. Flutter web
if ($ApiOnly) {
  Write-Host '[info]   Skipping Flutter (-ApiOnly)'
} elseif (Test-Port $WebPort) {
  Write-Host "[warn]   Port $WebPort is already in use; assuming Flutter web is running there."
} else {
  Write-Host "[info]   Starting Flutter web on $WebUrl (API_BASE_URL=$ApiUrl); first compile takes a while"
  $f = Start-Process -FilePath 'flutter' -WorkingDirectory (Join-Path $Root 'flutter') -PassThru -WindowStyle Hidden `
    -ArgumentList 'run', '-d', 'web-server', '--web-port', "$WebPort", '--web-hostname', 'localhost', "--dart-define=API_BASE_URL=$ApiUrl" `
    -RedirectStandardOutput (Join-Path $Dev 'flutter.log') -RedirectStandardError (Join-Path $Dev 'flutter.err.log')
  Set-Content -Path (Join-Path $Dev 'flutter.pid') -Value $f.Id
  if (Wait-Http $WebUrl 150) { Write-Host "[ok]     Flutter web serving at $WebUrl" }
  else { Write-Host '[warn]   Flutter web not answering yet; it may still be compiling. See .dev\flutter.log' }
}

Write-Host ''
Write-Host '  SelfKept dev stack'
Write-Host "  API       $ApiUrl      (health: $HealthUrl)"
if (-not $ApiOnly) { Write-Host "  Web       $WebUrl" }
Write-Host "  Postgres  localhost:$PgPort   db/user/password selfkept"
Write-Host '  Logs      .dev\api.log  .dev\flutter.log'
Write-Host '  Check     scripts\smoke.ps1     Stop: scripts\dev-down.ps1'
Write-Host ''
