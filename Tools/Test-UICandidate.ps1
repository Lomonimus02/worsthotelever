param([switch]$SkipBuild,[switch]$PreserveLiveDangerSession)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Get-WheBuildDirectory.ps1')
$uiBuildDirectory = Get-WheBuildDirectory -BuildDirectory (Join-Path $projectRoot 'Builds\WindowsUI')
$resultsPath = Join-Path $projectRoot 'TestResults'
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null
$reportPath = Join-Path $resultsPath 'ui-candidate-results.txt'
$records = [System.Collections.Generic.List[string]]::new()
$records.Add('UI candidate verification: ' + (Get-Date).ToString('o'))
$records.Add('Build directory: ' + $uiBuildDirectory)
if ($PreserveLiveDangerSession) {
    $scopeCaveat = 'NOT_VERIFIED: unchanged fingerprints for hotel-playtest-danger.json and hotel-playtest-danger.json.bak are excluded because the danger playtest slot is concurrently active. Normal/MVP slot fingerprints and path routing/menu checks remain enabled.'
    $records.Add($scopeCaveat)
    Write-Output $scopeCaveat
}
$previousBuildDirectory = [Environment]::GetEnvironmentVariable('WHE_TEST_BUILD_DIRECTORY', 'Process')
try {
    $env:WHE_TEST_BUILD_DIRECTORY = $uiBuildDirectory
    if (!$SkipBuild) { & (Join-Path $PSScriptRoot 'Build-Windows.ps1') -Ui }
    $dllPath = Join-Path $uiBuildDirectory 'WorstHotelEver_Data\Managed\Assembly-CSharp.dll'
    $dllHash = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash
    $records.Add('Runtime SHA256: ' + $dllHash)
    $started = Get-Date
    # Only Test-PlaytestSlot consumes this test-only flag. Default UI runs override
    # an inherited flag with strict checking; the caller's value is always restored.
    $previousLiveDangerFlag = [Environment]::GetEnvironmentVariable('WHE_TEST_PRESERVE_LIVE_DANGER_SESSION', 'Process')
    try {
        $env:WHE_TEST_PRESERVE_LIVE_DANGER_SESSION = if ($PreserveLiveDangerSession) { '1' } else { '0' }
        & (Join-Path $PSScriptRoot 'Test-DangerCandidate.ps1') -SkipBuild
    } finally {
        [Environment]::SetEnvironmentVariable('WHE_TEST_PRESERVE_LIVE_DANGER_SESSION', $previousLiveDangerFlag, 'Process')
    }
    $dangerReportPath = Join-Path $resultsPath 'danger-candidate-results.txt'
    if (!(Test-Path -LiteralPath $dangerReportPath) -or (Get-Item -LiteralPath $dangerReportPath).LastWriteTime -lt $started) {
        throw 'Danger candidate report is missing or stale.'
    }
    $dangerReport = @(Get-Content -LiteralPath $dangerReportPath)
    if (!($dangerReport -match '^OVERALL_PASS:') -or ($dangerReport -match '^OVERALL_FAIL:') -or
        !($dangerReport -contains ('Runtime SHA256: ' + $dllHash)) -or
        !($dangerReport -contains ('Build directory: ' + $uiBuildDirectory))) {
        throw 'Danger candidate report did not pass for the UI runtime.'
    }
    foreach ($caveat in ($dangerReport -match '^NOT_VERIFIED:')) {
        if (!$records.Contains($caveat)) { $records.Add($caveat) }
    }
    $records.Add('PASS: Test-DangerCandidate.ps1 SkipBuild (legacy, MVP and danger checks within the recorded scope)')
    $journalStarted = Get-Date
    & (Join-Path $PSScriptRoot 'Test-JournalUI.ps1')
    $journalReportPath = Join-Path $resultsPath 'journal-ui-results.txt'
    if (!(Test-Path -LiteralPath $journalReportPath) -or (Get-Item -LiteralPath $journalReportPath).LastWriteTime -lt $journalStarted) {
        throw 'Journal UI verification report is missing or stale.'
    }
    $journalReport = @(Get-Content -LiteralPath $journalReportPath)
    if (!($journalReport -match '^OVERALL_PASS:') -or ($journalReport -match '^OVERALL_FAIL:') -or
        !($journalReport -contains ('Runtime SHA256: ' + $dllHash)) -or
        !($journalReport -contains ('Build directory: ' + $uiBuildDirectory))) {
        throw 'Journal UI verification did not pass for the UI runtime.'
    }
    $records.Add('PASS: Test-JournalUI.ps1 (isolated journal-ui player, fresh report and matching runtime hash)')
    if ((Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash -ne $dllHash) {
        throw 'UI runtime changed during candidate verification.'
    }
    $records.Add('OVERALL_PASS: automated danger candidate and journal UI checks against WindowsUI within the recorded scope. Visual quality and human acceptance NOT tested.')
} catch {
    $records.Add('OVERALL_FAIL: ' + $_.Exception.Message)
    throw
} finally {
    [Environment]::SetEnvironmentVariable('WHE_TEST_BUILD_DIRECTORY', $previousBuildDirectory, 'Process')
    [IO.File]::WriteAllLines($reportPath, $records)
}
$records
