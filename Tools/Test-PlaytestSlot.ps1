param([switch]$Danger)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$resultsPath = Join-Path $projectRoot 'TestResults'
. (Join-Path $PSScriptRoot 'Get-WheBuildDirectory.ps1')
$exePath = Join-Path (Get-WheBuildDirectory) 'WorstHotelEver.exe'
$persistent = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\LocalLow\Almost Grand\Worst Hotel Ever'
function Slot-Fingerprint([string]$Name) {
    $path = Join-Path $persistent $Name
    if (Test-Path -LiteralPath $path) { return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
    return 'ABSENT'
}
$preserveLiveDanger = $env:WHE_TEST_PRESERVE_LIVE_DANGER_SESSION -eq '1'
$fingerprintNames = @('hotel-slot-1.json','hotel-slot-1.json.bak','hotel-playtest-mvp.json','hotel-playtest-mvp.json.bak')
if (!$preserveLiveDanger) { $fingerprintNames += @('hotel-playtest-danger.json','hotel-playtest-danger.json.bak') }
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null
$slotLabel = if ($Danger) { 'danger' } else { 'mvp' }
$scopeReportPath = Join-Path $resultsPath "playtest-slot-$slotLabel.txt"
$scopeRecords = [System.Collections.Generic.List[string]]::new()
$scopeRecords.Add('Playtest slot verification: ' + (Get-Date).ToString('o'))
if ($preserveLiveDanger) {
    $scopeCaveat = 'NOT_VERIFIED: unchanged fingerprints for hotel-playtest-danger.json and hotel-playtest-danger.json.bak are excluded because the danger playtest slot is concurrently active. Normal/MVP slot fingerprints and path routing/menu checks remain enabled.'
    $scopeRecords.Add($scopeCaveat)
    Write-Output $scopeCaveat
}
try {
    $before = @{}
    foreach ($name in $fingerprintNames) { $before[$name] = Slot-Fingerprint $name }
    for ($part = 1; $part -le 2; $part++) {
        $started = Get-Date
        $logPath = Join-Path $resultsPath "playtest-path-$part-player.log"
        $playtestFlag = if ($Danger) { '-whe-danger-playtest' } else { '-whe-playtest' }
        $arguments = "$playtestFlag -whe-path-check -screen-width 640 -screen-height 480 -screen-fullscreen 0 -logFile `"$logPath`""
        $run = Start-Process -FilePath $exePath -ArgumentList $arguments -WindowStyle Hidden -PassThru
        try {
            if (!$run.WaitForExit(20000)) { throw 'Playtest path check timed out.' }
            $reportPath = Join-Path $resultsPath 'playtest-path-session.txt'
            if (!(Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTime -lt $started) { throw 'Playtest path report missing/stale.' }
            $report = Get-Content -LiteralPath $reportPath
            $report
            Copy-Item -LiteralPath $reportPath -Destination (Join-Path $resultsPath "playtest-path-$part-session.txt") -Force
            if ($run.ExitCode -ne 0 -or $report[0] -ne 'PASS') { throw 'Human launcher slot routing failed.' }
            $scopeRecords.Add("PASS: path routing/menu checks, process $part")
        } finally { if (!$run.HasExited) { Stop-Process -Id $run.Id -ErrorAction SilentlyContinue } }
    }
    foreach ($name in $fingerprintNames) {
        if ((Slot-Fingerprint $name) -ne $before[$name]) { throw "Menu-only path check changed a persistent file: $name" }
        $scopeRecords.Add('CHECKED_UNCHANGED: ' + $name)
    }
    $verifiedSlots = if ($preserveLiveDanger) { 'normal/MVP saves and backups' } else { 'normal/MVP/danger saves and backups' }
    $summary = "PASS: two player processes passed path routing/menu checks; $verifiedSlots unchanged."
    $scopeRecords.Add($summary)
    Write-Output $summary
} catch {
    $scopeRecords.Add('FAIL: ' + $_.Exception.Message)
    throw
} finally { [IO.File]::WriteAllLines($scopeReportPath, $scopeRecords) }
