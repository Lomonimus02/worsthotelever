$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$resultsPath = Join-Path $projectRoot 'TestResults'
$exePath = Join-Path $projectRoot 'Builds\Windows\WorstHotelEver.exe'
$persistent = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\LocalLow\Almost Grand\Worst Hotel Ever'
function Slot-Fingerprint([string]$Name) {
    $path = Join-Path $persistent $Name
    if (Test-Path -LiteralPath $path) { return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
    return 'ABSENT'
}
$before = @{}
foreach ($name in @('hotel-slot-1.json','hotel-slot-1.json.bak','hotel-playtest-mvp.json','hotel-playtest-mvp.json.bak')) { $before[$name] = Slot-Fingerprint $name }
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null
for ($part = 1; $part -le 2; $part++) {
    $started = Get-Date
    $logPath = Join-Path $resultsPath "playtest-path-$part-player.log"
    $arguments = "-whe-playtest -whe-path-check -screen-width 1280 -screen-height 800 -screen-fullscreen 0 -logFile `"$logPath`""
    $run = Start-Process -FilePath $exePath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    try {
        if (!$run.WaitForExit(20000)) { throw 'Playtest path check timed out.' }
        $reportPath = Join-Path $resultsPath 'playtest-path-session.txt'
        if (!(Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTime -lt $started) { throw 'Playtest path report missing/stale.' }
        $report = Get-Content -LiteralPath $reportPath
        $report
        Copy-Item -LiteralPath $reportPath -Destination (Join-Path $resultsPath "playtest-path-$part-session.txt") -Force
        if ($run.ExitCode -ne 0 -or $report[0] -ne 'PASS') { throw 'Human launcher slot routing failed.' }
    } finally { if (!$run.HasExited) { Stop-Process -Id $run.Id -ErrorAction SilentlyContinue } }
}
foreach ($name in $before.Keys) { if ((Slot-Fingerprint $name) -ne $before[$name]) { throw "Menu-only path check changed a persistent file: $name" } }
Write-Output 'PASS: two actual player processes selected the persistent playtest path; both normal/playtest saves and backups unchanged.'
