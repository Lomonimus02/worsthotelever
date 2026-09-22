$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path $projectRoot 'Builds\Windows\WorstHotelEver.exe'
$resultsPath = Join-Path $projectRoot 'TestResults'
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null
$previousWorld = ''
$expectedDay = 1
$parts = @(3,3,4)
for ($part = 0; $part -lt $parts.Count; $part++) {
    $count = $parts[$part]
    $load = if ($part -gt 0) { '-whe-load' } else { '' }
    $logPath = Join-Path $resultsPath "mvp-soak-part-$part.log"
    $arguments = "-whe-host $load -whe-port 17786 -whe-session-tests -whe-mvp-fixture -whe-soak -whe-case mvp-soak -whe-soak-days $count -screen-width 640 -screen-height 480 -screen-fullscreen 0 -logFile `"$logPath`""
    $started = Get-Date
    $run = Start-Process -FilePath $exePath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    try {
        if (!$run.WaitForExit(55000)) { throw "MVP soak process $part timed out." }
        $reportPath = Join-Path $resultsPath 'mvp-soak-session.txt'
        if (!(Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTime -lt $started) { throw 'Soak report missing/stale.' }
        $report = Get-Content -LiteralPath $reportPath
        Copy-Item -LiteralPath $reportPath -Destination (Join-Path $resultsPath "mvp-soak-part-$part.txt") -Force
        $report
        if ($run.ExitCode -ne 0 -or $report[0] -ne 'PASS') { throw "MVP soak part $part failed." }
        $checkpoint = Get-Content -Raw -LiteralPath (Join-Path $resultsPath 'mvp-soak-checkpoint.json') | ConvertFrom-Json
        $state = $checkpoint.payload | ConvertFrom-Json
        $expectedDay += $count
        if ($state.day -ne $expectedDay -or $state.version -ne 2 -or $state.phase -ne 'preparation') { throw 'Wrong process checkpoint calendar/schema.' }
        if ($part -gt 0 -and $state.worldId -ne $previousWorld) { throw 'Restart replaced rather than resumed the hotel.' }
        $previousWorld = $state.worldId
    } finally { if (!$run.HasExited) { Stop-Process -Id $run.Id -ErrorAction SilentlyContinue } }
}
Write-Output 'PASS: ten simulated gameplay days across three Windows player processes; same saved hotel. This is an automated bot, not a human playtest.'
