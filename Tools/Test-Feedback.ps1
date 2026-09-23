$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$resultsPath = Join-Path $projectRoot 'TestResults'
. (Join-Path $PSScriptRoot 'Get-WheBuildDirectory.ps1')
$exePath = Join-Path (Get-WheBuildDirectory) 'WorstHotelEver.exe'
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null
$started = Get-Date
$logPath = Join-Path $resultsPath 'feedback-player.log'
$arguments = "-whe-host -whe-port 17783 -whe-session-tests -whe-legacy-fixture -whe-case feedback -screen-width 640 -screen-height 480 -screen-fullscreen 0 -logFile `"$logPath`""
$run = Start-Process -FilePath $exePath -ArgumentList $arguments -WindowStyle Hidden -PassThru
try {
    if (!$run.WaitForExit(45000)) { throw 'Standalone feedback test timed out.' }
    $reportPath = Join-Path $resultsPath 'feedback-session.txt'
    if (!(Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTime -lt $started) { throw 'Feedback report missing or stale.' }
    $report = Get-Content -LiteralPath $reportPath
    $report
    if ($report[0] -ne 'PASS' -or $run.ExitCode -ne 0) { throw 'Standalone feedback test failed.' }
} finally {
    if (!$run.HasExited) { Stop-Process -Id $run.Id -ErrorAction SilentlyContinue }
}
