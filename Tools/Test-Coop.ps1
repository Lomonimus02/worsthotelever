$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Get-WheBuildDirectory.ps1')
$exePath = Join-Path (Get-WheBuildDirectory) 'WorstHotelEver.exe'
$resultsPath = Join-Path $projectRoot 'TestResults'
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null
if (!(Test-Path -LiteralPath $exePath)) { throw 'Build the Windows player first.' }
$hostLog = Join-Path $resultsPath 'host-player.log'
$clientLog = Join-Path $resultsPath 'client-player.log'
$hostArgs = "-whe-host -whe-smoke -whe-legacy-fixture -whe-port 17777 -screen-width 1280 -screen-height 800 -screen-fullscreen 0 -logFile `"$hostLog`""
$clientArgs = "-whe-client 127.0.0.1 -whe-smoke -whe-legacy-fixture -whe-port 17777 -screen-width 1280 -screen-height 800 -screen-fullscreen 0 -logFile `"$clientLog`""
$testStarted = Get-Date
$hostRun = Start-Process -FilePath $exePath -ArgumentList $hostArgs -WindowStyle Hidden -PassThru
Start-Sleep -Seconds 3
$clientRun = Start-Process -FilePath $exePath -ArgumentList $clientArgs -WindowStyle Hidden -PassThru
$testDeadline = (Get-Date).AddSeconds(75)
while ((Get-Date) -lt $testDeadline) {
    $alive = @($hostRun.Id, $clientRun.Id) | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue }
    if (@($alive).Count -eq 0) { break }
    Start-Sleep -Seconds 1
}
$stillRunning = @($hostRun.Id, $clientRun.Id) | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue }
if ($stillRunning.Count -gt 0) {
    # Only terminate the two test processes that this script just created.
    $stillRunning | ForEach-Object { Stop-Process -Id $_ -ErrorAction SilentlyContinue }
    throw 'Co-op smoke test exceeded 75 seconds; inspect player logs.'
}
foreach ($role in @('host', 'client')) {
    $reportPath = Join-Path $resultsPath "$role-smoke.txt"
    if (!(Test-Path -LiteralPath $reportPath)) { throw "Missing report: $reportPath" }
    if ((Get-Item -LiteralPath $reportPath).LastWriteTime -lt $testStarted) { throw "Stale report: $reportPath" }
    $report = Get-Content -LiteralPath $reportPath
    Write-Output "$role result:"
    $report
    if ($report[0] -ne 'PASS') { throw "Co-op $role failed. See $reportPath" }
}
