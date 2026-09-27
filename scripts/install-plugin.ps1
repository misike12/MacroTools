<#Requires -Version 7.0>
<#
.SYNOPSIS
    Installs packed plugin artifacts into the local Macro Deck host.

.DESCRIPTION
    Posts each artifact to the host's install-path endpoint over loopback
    (unsigned local builds need allowUnsigned) and reports per-artifact
    timing. Artifacts install strictly one at a time: the host serializes
    installs per plugin and a second concurrent install wedges behind the
    first, so this script never parallelizes.

    Since host beta.14 the loopback API needs the per-launch secret in the
    X-MacroDeck-Loopback-Secret header. The script reads it from
    %APPDATA%\MacroDeck\config\loopback-secret when that file exists (hosts
    that generate one), or takes it via -LoopbackSecret. Without a secret
    the host answers 401 and the only path is installing by hand: double-click
    the artifact (or use install-from-file in the desktop app) and confirm
    the unsigned-artifact prompt.

    Build the artifacts first, e.g.:
        macrodeck-plugin build --source src/Timers --output ./artifacts

    Then:
        ./scripts/install-plugin.ps1 ./artifacts/com.misu.timers-1.1.3.macroDeckPlugin

    If installs take minutes instead of seconds, the host is probably taking
    a full pre-update backup for each one. Either prune old plugin versions
    or turn the backup off (Settings, or PATCH /api/backups/settings with
    {"beforePluginUpdate":false}) and re-enable it before risky updates.

.PARAMETER Artifact
    One or more artifact paths, installed sequentially in the given order.
    Relative paths resolve against the repository root.
.PARAMETER LoopbackSecret
    Per-launch loopback secret for hosts that require one (beta.14+).
    Falls back to %APPDATA%\MacroDeck\config\loopback-secret when present.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0, ValueFromRemainingArguments)]
    [string[]]$Artifact,
    [Parameter()]
    [string]$LoopbackSecret = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$portFile = Join-Path $env:TEMP 'macro-deck-host.port'
if (-not (Test-Path -LiteralPath $portFile)) {
    Write-Error "Host port file not found at '$portFile'. Is the Macro Deck desktop app running?"
}

$secretFile = Join-Path $env:APPDATA 'MacroDeck/config/loopback-secret'
if ([string]::IsNullOrWhiteSpace($LoopbackSecret) -and (Test-Path -LiteralPath $secretFile)) {
    $LoopbackSecret = (Get-Content -LiteralPath $secretFile -TotalCount 1).Trim()
}

$headers = @{}
if (-not [string]::IsNullOrWhiteSpace($LoopbackSecret)) {
    $headers['X-MacroDeck-Loopback-Secret'] = $LoopbackSecret
}

$port = (Get-Content -LiteralPath $portFile -TotalCount 1).Trim()
$failures = 0

foreach ($entry in $Artifact) {
    $full = if ([IO.Path]::IsPathRooted($entry)) { $entry } else { Join-Path $repoRoot $entry }
    if (-not (Test-Path -LiteralPath $full)) {
        Write-Warning "Skipping missing artifact: $entry"
        $failures++
        continue
    }

    $body = @{ path = $full; force = $false; allowUnsigned = $true } | ConvertTo-Json -Compress
    $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
        $response = Invoke-WebRequest `
            -Uri "http://127.0.0.1:$port/api/plugin-installation/install-path" `
            -Method Post -ContentType 'application/json' -Headers $headers -Body $body -TimeoutSec 600
        $watch.Stop()
        $result = $response.Content | ConvertFrom-Json
        Write-Host "OK $($response.StatusCode) in $([math]::Round($watch.Elapsed.TotalSeconds, 1))s: $($result.pluginId) $($result.version) (was $($result.previousVersion))"
        foreach ($warning in @($result.warnings)) {
            Write-Warning "$($warning.code): $($warning.message)"
        }
    }
    catch {
        $watch.Stop()
        $failures++
        Write-Warning "FAILED after $([math]::Round($watch.Elapsed.TotalSeconds, 1))s: $entry : $($_.Exception.Message)"
        if ($_.Exception.Response -and $_.Exception.Response.StatusCode.value__ -eq 401) {
            Write-Warning "The host refused the loopback call (401). On beta.14+ pass -LoopbackSecret or install by hand via double-click."
        }

        if ($_.ErrorDetails -and $_.ErrorDetails.Message) {
            Write-Warning ($_.ErrorDetails.Message | ConvertFrom-Json | Select-Object -ExpandProperty error -ErrorAction SilentlyContinue)
        }
    }
}

if ($failures -gt 0) {
    exit 1
}
