$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$resultsPath = Join-Path $projectRoot 'TestResults'
$exePath = Join-Path $projectRoot 'Builds\Windows\WorstHotelEver.exe'
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null
$started = Get-Date
$logPath = Join-Path $resultsPath 'legacy-oversize-player.log'
$arguments = "-whe-host -whe-port 17788 -whe-session-tests -whe-legacy-fixture -whe-case legacy-oversize -screen-width 640 -screen-height 480 -screen-fullscreen 0 -logFile `"$logPath`""
$run = Start-Process -FilePath $exePath -ArgumentList $arguments -WindowStyle Hidden -PassThru
try {
    if (!$run.WaitForExit(40000)) { throw 'Legacy migration test timed out.' }
    $path = Join-Path $resultsPath 'legacy-oversize-session.txt'
    if (!(Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).LastWriteTime -lt $started) { throw 'Legacy migration report missing/stale.' }
    $report = Get-Content -LiteralPath $path
    $report
    if ($run.ExitCode -ne 0 -or $report[0] -ne 'PASS') { throw 'Legacy migration player test failed.' }
} finally { if (!$run.HasExited) { Stop-Process -Id $run.Id -ErrorAction SilentlyContinue } }
