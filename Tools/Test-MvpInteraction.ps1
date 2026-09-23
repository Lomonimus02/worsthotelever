$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Get-WheBuildDirectory.ps1')
$exePath = Join-Path (Get-WheBuildDirectory) 'WorstHotelEver.exe'
$resultsPath = Join-Path $projectRoot 'TestResults'
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null
if (!(Test-Path -LiteralPath $exePath)) { throw 'Build the MVP Windows player first.' }
$started = Get-Date
$logPath = Join-Path $resultsPath 'mvp-interaction-player.log'
$arguments = "-whe-host -whe-port 17787 -whe-session-tests -whe-mvp-fixture -whe-case mvp-interaction -screen-width 1280 -screen-height 800 -screen-fullscreen 0 -logFile `"$logPath`""
$run = Start-Process -FilePath $exePath -ArgumentList $arguments -WindowStyle Hidden -PassThru
try {
    $deadline = (Get-Date).AddSeconds(120)
    while (!$run.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
    if (!$run.HasExited) { throw 'MVP physical interaction test timed out.' }
    $reportPath = Join-Path $resultsPath 'mvp-interaction-session.txt'
    if (!(Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTime -lt $started) { throw 'Interaction report missing/stale.' }
    $report = Get-Content -LiteralPath $reportPath
    $report
    if ($run.ExitCode -ne 0 -or $report[0] -ne 'PASS') { throw 'MVP physical interaction test failed.' }
} finally {
    if (!$run.HasExited) { Stop-Process -Id $run.Id -ErrorAction SilentlyContinue }
}
