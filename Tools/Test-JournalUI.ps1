$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Get-WheBuildDirectory.ps1')
$buildDirectory = Get-WheBuildDirectory
$exePath = Join-Path $buildDirectory 'WorstHotelEver.exe'
$dllPath = Join-Path $buildDirectory 'WorstHotelEver_Data\Managed\Assembly-CSharp.dll'
$resultsPath = Join-Path $projectRoot 'TestResults'
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null
$reportPath = Join-Path $resultsPath 'journal-ui-results.txt'
$sessionReportPath = Join-Path $resultsPath 'journal-ui-session.txt'
$logPath = Join-Path $resultsPath 'journal-ui-player.log'
$records = [System.Collections.Generic.List[string]]::new()
$records.Add('Journal UI verification: ' + (Get-Date).ToString('o'))
$records.Add('Build directory: ' + $buildDirectory)
$run = $null
try {
    if (!(Test-Path -LiteralPath $exePath -PathType Leaf)) { throw 'Build the selected Windows player first.' }
    $dllHash = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash
    $records.Add('Runtime SHA256: ' + $dllHash)
    $started = Get-Date
    # Uses the existing isolated session-test slot, never a persistent playtest slot.
    # Run after danger co-op: its impairment proxy also uses port 17795.
    $arguments = "-whe-host -whe-port 17795 -whe-session-tests -whe-case journal-ui -screen-width 640 -screen-height 480 -screen-fullscreen 0 -logFile `"$logPath`""
    $run = Start-Process -FilePath $exePath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (!$run.WaitForExit(90000)) { throw 'Journal UI test exceeded 90 seconds.' }
    if (!(Test-Path -LiteralPath $sessionReportPath) -or (Get-Item -LiteralPath $sessionReportPath).LastWriteTime -lt $started) {
        throw 'Journal UI session report is missing or stale.'
    }
    $sessionReport = @(Get-Content -LiteralPath $sessionReportPath)
    $sessionReport
    if ($run.ExitCode -ne 0 -or $sessionReport.Count -eq 0 -or $sessionReport[0] -ne 'PASS') {
        throw 'Journal UI player failed; inspect journal-ui-session.txt and journal-ui-player.log.'
    }
    if ((Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash -ne $dllHash) {
        throw 'Journal UI runtime changed during verification.'
    }
    $records.AddRange([string[]]$sessionReport)
    $records.Add('OVERALL_PASS: isolated journal-ui player exited successfully with a fresh PASS report and unchanged runtime hash.')
} catch {
    $records.Add('OVERALL_FAIL: ' + $_.Exception.Message)
    throw
} finally {
    # Cleanup is limited to the process object created by this invocation.
    if ($null -ne $run -and !$run.HasExited) { Stop-Process -InputObject $run -ErrorAction SilentlyContinue }
    [IO.File]::WriteAllLines($reportPath, $records)
}
$records
