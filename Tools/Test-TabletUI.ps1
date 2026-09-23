# Runs an already-built, parent-wired player. Does not build Unity or run editor tests.
[CmdletBinding()]
param([ValidateRange(30, 300)][int]$TimeoutSeconds = 120)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Get-WheBuildDirectory.ps1')
$buildDirectory = Get-WheBuildDirectory
$exePath = Join-Path $buildDirectory 'WorstHotelEver.exe'
$dllPath = Join-Path $buildDirectory 'WorstHotelEver_Data\Managed\Assembly-CSharp.dll'
$resultsPath = Join-Path $projectRoot 'TestResults'
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null
$reportPath = Join-Path $resultsPath 'tablet-ui-results.txt'
$sessionReportPath = Join-Path $resultsPath 'tablet-ui-session.txt'
$logPath = Join-Path $resultsPath 'tablet-ui-player.log'
$records = [System.Collections.Generic.List[string]]::new()
$records.Add('Tablet UI verification: ' + (Get-Date).ToString('o'))
$records.Add('Build directory: ' + $buildDirectory)
$run = $null
try {
    if (!(Test-Path -LiteralPath $exePath -PathType Leaf) -or !(Test-Path -LiteralPath $dllPath -PathType Leaf)) {
        throw 'Build the selected Windows player with the tablet-ui dispatch and render observers first.'
    }
    $dllHash = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash
    $records.Add('Runtime SHA256: ' + $dllHash)
    $started = Get-Date
    # Isolated session-test save only. Port 17796 does not reuse journal/danger-coop port 17795.
    # Force a real first resize: hidden players can produce a blank first capture at launch size.
    $arguments = "-whe-host -whe-port 17796 -whe-session-tests -whe-case tablet-ui -screen-width 640 -screen-height 480 -screen-fullscreen 0 -logFile `"$logPath`""
    $run = Start-Process -FilePath $exePath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (!$run.WaitForExit($TimeoutSeconds * 1000)) { throw "Tablet UI test exceeded $TimeoutSeconds seconds." }
    if (!(Test-Path -LiteralPath $sessionReportPath -PathType Leaf) -or
        (Get-Item -LiteralPath $sessionReportPath).LastWriteTime -lt $started) {
        throw 'Tablet UI session report is missing or stale; check the parent tablet-ui dispatch.'
    }
    $sessionReport = @(Get-Content -LiteralPath $sessionReportPath)
    $sessionReport
    $records.AddRange([string[]]$sessionReport)
    if ($run.ExitCode -ne 0 -or $sessionReport.Count -eq 0 -or $sessionReport[0] -ne 'PASS') {
        throw 'Tablet UI player failed; inspect tablet-ui-session.txt and tablet-ui-player.log.'
    }
    # A generic/fall-through session PASS must never be mistaken for this scenario's evidence.
    foreach ($required in @(
        'TABLET_PRODUCTION_V3_WITH_PROCESS_ISOLATED_SAVE',
        'TABLET_REAL_H_HIDES_HINT_WITHOUT_CHANGING_SHARED_TUTORIAL_OR_PACE',
        'TABLET_MENU_SUPPRESSES_HINT_WITHOUT_REPLAY',
        'TABLET_FINAL_ISOLATED_SAVE_SUCCEEDED',
        'TABLET_PERSISTENT_SAVE_ERROR_REMAINS_WITH_ACTIVE_ALERT_AND_RECOVERS',
        'TABLET_IDLE_FOCUS_HELD_WHITELIST_ALL_RESOLUTIONS',
        'TABLET_DANGER_HUD_QUIET_ALERT_RENDERED_IN_TABLET_ALL_RESOLUTIONS',
        'TABLET_SUMMARY_NEVER_AUTO_OPENS',
        'TABLET_REAL_TAB_ESCAPE_NAVIGATION',
        'TABLET_1280x800_960x600_1920x1080_CAPTURED',
        'TABLET_NO_UNITY_OR_IMGUI_ERRORS'
    )) {
        if ($sessionReport -cnotcontains $required) { throw "Tablet UI report lacks required evidence: $required" }
    }
    $captures = [System.Collections.Generic.List[string]]::new()
    foreach ($width in @(1280, 960, 1920)) {
        foreach ($label in @('idle', 'focus', 'held', 'tasks', 'danger', 'danger-tasks')) {
            $captures.Add("ui-$width-tablet-$label.png")
        }
    }
    $captures.AddRange([string[]]@(
        'ui-1280-tablet-hint-visible.png', 'ui-1280-tablet-hint-dismissed.png',
        'ui-1280-tablet-tasks-from-hint.png', 'ui-1920-tablet-pause.png',
        'ui-960-tablet-summary-no-popup.png', 'ui-960-tablet-summary.png', 'ui-1920-tablet-save-error.png'
    ))
    foreach ($capture in $captures) {
        $capturePath = Join-Path $resultsPath $capture
        if (!(Test-Path -LiteralPath $capturePath -PathType Leaf)) { throw "Missing tablet screenshot: $capture" }
        $captureInfo = Get-Item -LiteralPath $capturePath
        if ($captureInfo.LastWriteTime -lt $started -or $captureInfo.Length -eq 0) {
            throw "Stale or empty tablet screenshot: $capture"
        }
        $prefix = 'TABLET_CAPTURE ' + $capture + ' '
        if (!($sessionReport | Where-Object { $_.StartsWith($prefix, [StringComparison]::Ordinal) })) {
            throw "Screenshot lacks matching render-observer evidence: $capture"
        }
    }
    if ((Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash -ne $dllHash) {
        throw 'Tablet UI runtime changed during verification.'
    }
    $records.Add('LIMITATION: editor HotelTabletTests.RunAll is wired/run separately; this script tests the standalone player only.')
    $records.Add('LIMITATION: native mouse checks and visual review of the 25 captures are separate from this keyboard/observer run.')
    $records.Add('OVERALL_PASS: isolated tablet-ui player, required evidence, 25 fresh captures and unchanged runtime hash.')
} catch {
    $records.Add('OVERALL_FAIL: ' + $_.Exception.Message)
    throw
} finally {
    # Cleanup is limited to the process object created by this invocation; no process-name kill.
    if ($null -ne $run -and !$run.HasExited) { Stop-Process -InputObject $run -ErrorAction SilentlyContinue }
    [IO.File]::WriteAllLines($reportPath, $records)
}
$records
