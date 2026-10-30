# Stop the SelfKept dev stack started by scripts\dev-up.ps1. Twin of scripts/dev-down.sh.
#
#   scripts\dev-down.ps1           # stop Flutter, the API and the Postgres container (data kept)
#   scripts\dev-down.ps1 -Prune    # also remove the Postgres container and its data volume
[CmdletBinding()]
param([switch]$Prune)
$ErrorActionPreference = 'Continue'

$Root = Split-Path -Parent $PSScriptRoot
$Dev = Join-Path $Root '.dev'
$ApiPort = if ($env:SELFKEPT_DEV_API_PORT) { [int]$env:SELFKEPT_DEV_API_PORT } else { 5000 }
$WebPort = if ($env:SELFKEPT_DEV_WEB_PORT) { [int]$env:SELFKEPT_DEV_WEB_PORT } else { 3000 }
$PgContainer = 'selfkept-pg'
$PgVolume = 'selfkept-pg-data'

function Stop-Tree([int]$ProcId) {
  if ($ProcId -gt 0) { taskkill /PID $ProcId /T /F *> $null }
}
function Stop-One([string]$Name, [string]$PidFile, [int]$Port) {
  if (Test-Path $PidFile) {
    Stop-Tree ([int](Get-Content $PidFile | Select-Object -First 1))
    Remove-Item $PidFile -Force
  }
  $conn = Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($conn) { Stop-Tree $conn.OwningProcess }
  Start-Sleep -Seconds 1
  if (Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue) {
    Write-Host "[warn]   $Name : something still listens on :$Port"
  } else {
    Write-Host "[ok]     $Name stopped (:$Port free)"
  }
}

Stop-One 'Flutter web' (Join-Path $Dev 'flutter.pid') $WebPort
Stop-One 'API' (Join-Path $Dev 'api.pid') $ApiPort

docker inspect $PgContainer *> $null
if ($LASTEXITCODE -eq 0) {
  if ($Prune) {
    docker rm -f $PgContainer | Out-Null
    Write-Host "[ok]     Removed container $PgContainer"
    docker volume rm $PgVolume *> $null
    if ($LASTEXITCODE -eq 0) { Write-Host "[ok]     Removed volume $PgVolume" } else { Write-Host "[warn]   Volume $PgVolume not found or in use" }
  } else {
    docker stop $PgContainer | Out-Null
    Write-Host "[ok]     Stopped container $PgContainer (data kept; -Prune removes it)"
  }
}
