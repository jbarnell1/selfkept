# Smoke check against a running SelfKept API: version and health. Twin of scripts/smoke.sh.
#
#   scripts\smoke.ps1                           # the dev API on http://localhost:5000
#   scripts\smoke.ps1 http://127.0.0.1:8080     # a self-host stack on this machine
[CmdletBinding()]
param([string]$BaseUrl = 'http://localhost:5000')
$ErrorActionPreference = 'Continue'
$BaseUrl = $BaseUrl.TrimEnd('/')
$fail = $false

function Get-Json([string]$Path, [string]$What) {
  try {
    $r = Invoke-WebRequest -Uri "$BaseUrl$Path" -UseBasicParsing -TimeoutSec 10
    Write-Host "ok    $What (HTTP $($r.StatusCode))"
    Write-Host "      $($r.Content)"
    return ($r.Content | ConvertFrom-Json)
  } catch {
    Write-Host "FAIL  $What : $($_.Exception.Message)"
    $script:fail = $true
    return $null
  }
}

Write-Host "SelfKept smoke: $BaseUrl"
$version = Get-Json '/api/system/version' 'GET /api/system/version'
if ($version -and -not $version.version) { Write-Host 'FAIL  version body has no "version" field'; $fail = $true }

$health = Get-Json '/api/system/health' 'GET /api/system/health'
if ($health) {
  if ($health.status -ne 'ok') { Write-Host 'FAIL  health status is not ok'; $fail = $true }
  if ($health.database -ne 'ok') { Write-Host 'FAIL  database is not ok'; $fail = $true }
}

if ($fail) { Write-Host 'FAILED'; exit 1 } else { Write-Host 'PASS'; exit 0 }
