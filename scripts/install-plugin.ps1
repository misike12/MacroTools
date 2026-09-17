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
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0, ValueFromRemainingArguments)]
    [string[]]$Artifact
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$portFile = Join-Path $env:TEMP 'macro-deck-host.port'
if (-not (Test-Path -LiteralPath $portFile)) {
    Write-Error "Host port file not found at '$portFile'. Is the Macro Deck desktop app running?"
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
            -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 600
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
        if ($_.ErrorDetails -and $_.ErrorDetails.Message) {
            Write-Warning ($_.ErrorDetails.Message | ConvertFrom-Json | Select-Object -ExpandProperty error -ErrorAction SilentlyContinue)
        }
    }
}

if ($failures -gt 0) {
    exit 1
}
