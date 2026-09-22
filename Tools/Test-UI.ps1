$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$resultsPath = Join-Path $projectRoot 'TestResults'
$exePath = Join-Path $projectRoot 'Builds\Windows\WorstHotelEver.exe'
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null
$started = Get-Date
$logPath = Join-Path $resultsPath 'ui-player.log'
# The fixture resizes the initial window to both target sizes before capturing its backbuffer.
# Requires an unlocked Windows desktop / graphics device; this is not a headless render test.
$arguments = "-whe-host -whe-port 17781 -whe-session-tests -whe-case ui -screen-width 640 -screen-height 480 -screen-fullscreen 0 -logFile `"$logPath`""
$run = Start-Process -FilePath $exePath -ArgumentList $arguments -WindowStyle Hidden -PassThru
try {
    if (!$run.WaitForExit(45000)) { throw 'UI render fixture timed out.' }
    $reportPath = Join-Path $resultsPath 'ui-session.txt'
    if (!(Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTime -lt $started) { throw 'UI report missing or stale.' }
    $report = Get-Content -LiteralPath $reportPath
    $report
    if ($report[0] -ne 'PASS' -or $run.ExitCode -ne 0) { throw 'UI render fixture failed.' }
    foreach ($width in @(1280,960)) {
        foreach ($panel in @('hud','reception','guest','tasks','management','finish-confirm','pause')) {
            $png = Join-Path $resultsPath "ui-$width-$panel.png"
            if (!(Test-Path -LiteralPath $png) -or (Get-Item -LiteralPath $png).LastWriteTime -lt $started) { throw "Missing or stale screenshot: $png" }
        }
    }
    Write-Output '14 current UI screenshots captured. Inspect them visually; PASS only asserts rendering, not layout quality.'
} finally {
    if (!$run.HasExited) { Stop-Process -Id $run.Id -ErrorAction SilentlyContinue }
}
