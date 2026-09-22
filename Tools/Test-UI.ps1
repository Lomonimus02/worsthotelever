param([switch]$Mvp)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$resultsPath = Join-Path $projectRoot 'TestResults'
$exePath = Join-Path $projectRoot 'Builds\Windows\WorstHotelEver.exe'
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null
$started = Get-Date
$scenario = if ($Mvp) { 'mvp-ui' } else { 'ui' }
$logPath = Join-Path $resultsPath "$scenario-player.log"
# The fixture resizes the initial window to both target sizes before capturing its backbuffer.
# Requires an unlocked Windows desktop / graphics device; this is not a headless render test.
$fixture = if ($Mvp) { '' } else { '-whe-legacy-fixture' }
$arguments = "-whe-host -whe-port 17781 -whe-session-tests $fixture -whe-case $scenario -screen-width 640 -screen-height 480 -screen-fullscreen 0 -logFile `"$logPath`""
$run = Start-Process -FilePath $exePath -ArgumentList $arguments -WindowStyle Hidden -PassThru
try {
    if (!$run.WaitForExit(55000)) { throw 'UI render fixture timed out.' }
    $reportPath = Join-Path $resultsPath "$scenario-session.txt"
    if (!(Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTime -lt $started) { throw 'UI report missing or stale.' }
    $report = Get-Content -LiteralPath $reportPath
    $report
    if ($report[0] -ne 'PASS' -or $run.ExitCode -ne 0) { throw 'UI render fixture failed.' }
    $panels = if ($Mvp) { @('hud','rooms','guests','guest','operations','schedule','management','briefing','finish-confirm','pause','settings','day2-briefing','day2-management','day2-schedule','day2-hud') } else { @('hud','reception','guest','tasks','management','briefing','pace-confirm','finish-confirm','pause','settings','day2-briefing','day2-hud') }
    $prefix = if ($Mvp) { 'mvp-' } else { '' }
    foreach ($width in @(1280,960)) {
        foreach ($panel in $panels) {
            $png = Join-Path $resultsPath "ui-$width-$prefix$panel.png"
            if (!(Test-Path -LiteralPath $png) -or (Get-Item -LiteralPath $png).LastWriteTime -lt $started) { throw "Missing or stale screenshot: $png" }
        }
    }
    $extraCount = 0
    if ($Mvp) {
        foreach ($width in @(1280,960)) {
            foreach ($panel in @('management-middle','management-bottom','rooms-bottom','schedule-bottom')) {
                $png = Join-Path $resultsPath "ui-$width-mvp-scroll-$panel.png"
                if (!(Test-Path -LiteralPath $png) -or (Get-Item -LiteralPath $png).LastWriteTime -lt $started) { throw "Missing or stale scrolled screenshot: $png" }
                $extraCount++
            }
        }
    }
    Write-Output "$($panels.Count * 2 + $extraCount) current UI screenshots captured. Inspect them visually; PASS only asserts rendering, not layout quality."
} finally {
    if (!$run.HasExited) { Stop-Process -Id $run.Id -ErrorAction SilentlyContinue }
}
