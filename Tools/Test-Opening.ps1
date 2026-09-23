param([switch]$Mvp)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$resultsPath = Join-Path $projectRoot 'TestResults'
. (Join-Path $PSScriptRoot 'Get-WheBuildDirectory.ps1')
$exePath = Join-Path (Get-WheBuildDirectory) 'WorstHotelEver.exe'
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null
$started = Get-Date
$logPath = Join-Path $resultsPath 'opening-player.log'
$fixture = if ($Mvp) { '-whe-mvp-fixture' } else { '-whe-legacy-fixture' }
$arguments = "-whe-host -whe-port 17782 -whe-session-tests $fixture -whe-case opening -screen-width 640 -screen-height 480 -screen-fullscreen 0 -logFile `"$logPath`""
$run = Start-Process -FilePath $exePath -ArgumentList $arguments -WindowStyle Hidden -PassThru
try {
    if (!$run.WaitForExit(45000)) { throw 'Standalone two-day opening test timed out.' }
    $reportPath = Join-Path $resultsPath 'opening-session.txt'
    if (!(Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTime -lt $started) { throw 'Opening report missing or stale.' }
    $report = Get-Content -LiteralPath $reportPath
    $report
    if ($report[0] -ne 'PASS' -or $run.ExitCode -ne 0) { throw 'Standalone opening test failed.' }
} finally {
    if (!$run.HasExited) { Stop-Process -Id $run.Id -ErrorAction SilentlyContinue }
}
