param([switch]$Mvp)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Get-WheBuildDirectory.ps1')
$exePath = Join-Path (Get-WheBuildDirectory) 'WorstHotelEver.exe'
$resultsPath = Join-Path $projectRoot 'TestResults'
if (!(Test-Path -LiteralPath $exePath)) { throw 'Build the Windows player first.' }
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null
function Start-Scenario([string]$Scenario, [string]$Connection) {
    $logPath = Join-Path $resultsPath "$Scenario-player.log"
    $fixture = if ($Mvp) { '-whe-mvp-fixture' } else { '-whe-legacy-fixture' }
    $arguments = "-whe-session-tests $fixture -whe-case $Scenario $Connection -screen-width 1280 -screen-height 800 -screen-fullscreen 0 -logFile `"$logPath`""
    Start-Process -FilePath $exePath -ArgumentList $arguments -WindowStyle Hidden -PassThru
}
function Complete-Scenarios($Runs, $Names, $Started) {
    $deadline = (Get-Date).AddSeconds(45)
    try {
        while ((Get-Date) -lt $deadline) {
            $alive = @($Runs | Where-Object { !$_.HasExited })
            if ($alive.Count -eq 0) { break }
            Start-Sleep -Milliseconds 300
        }
        foreach ($run in $Runs) { if (!$run.HasExited) { throw 'Session test timed out.' } }
        foreach ($name in $Names) {
            $path = Join-Path $resultsPath "$name-session.txt"
            if (!(Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).LastWriteTime -lt $Started) { throw "Missing/stale report: $name" }
            $report = Get-Content -LiteralPath $path
            if ($Mvp) { Copy-Item -LiteralPath $path -Destination (Join-Path $resultsPath "mvp-$name-session.txt") -Force }
            Write-Output "$name result:"
            $report
            if ($report[0] -ne 'PASS') { throw "Failed scenario: $name" }
        }
        foreach ($run in $Runs) { if ($run.ExitCode -ne 0) { throw "Test process exited with code $($run.ExitCode)." } }
    } finally {
        # Only the process objects started by this script are eligible for cleanup.
        foreach ($run in $Runs) { if (!$run.HasExited) { Stop-Process -Id $run.Id -ErrorAction SilentlyContinue } }
    }
}
$started = Get-Date
$lifecycle = Start-Scenario 'lifecycle' '-whe-host -whe-port 17779'
Complete-Scenarios @($lifecycle) @('lifecycle') $started
$started = Get-Date
$capacityHost = Start-Scenario 'capacity-host' '-whe-host -whe-port 17780'
Start-Sleep -Seconds 2
$clientA = Start-Scenario 'capacity-client-a' '-whe-client 127.0.0.1 -whe-port 17780'
$clientB = Start-Scenario 'capacity-client-b' '-whe-client 127.0.0.1 -whe-port 17780'
Complete-Scenarios @($capacityHost,$clientA,$clientB) @('capacity-host','capacity-client-a','capacity-client-b') $started
$reports = (Get-Content (Join-Path $resultsPath 'capacity-client-a-session.txt')) + (Get-Content (Join-Path $resultsPath 'capacity-client-b-session.txt'))
if (@($reports | Where-Object { $_ -eq 'CAPACITY_ACCEPTED_WITH_PLAYER' }).Count -ne 1 -or @($reports | Where-Object { $_ -eq 'CAPACITY_REJECTED_WITH_REASON' }).Count -ne 1) {
    throw 'Expected exactly one accepted and one capacity-rejected client.'
}
