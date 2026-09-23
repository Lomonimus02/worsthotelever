$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Get-WheBuildDirectory.ps1')
$build = Get-WheBuildDirectory
$results = Join-Path $projectRoot 'TestResults'
$reportPath = Join-Path $results 'stamina-results.txt'
$records = [System.Collections.Generic.List[string]]::new()
$records.Add('Stamina verification: ' + (Get-Date).ToString('o'))
$records.Add('Build directory: ' + $build)
$run = $null
try {
    $dll = Join-Path $build 'WorstHotelEver_Data/Managed/Assembly-CSharp.dll'
    $hash = (Get-FileHash -LiteralPath $dll).Hash
    $records.Add('Runtime SHA256: ' + $hash)
    $started = Get-Date
    $log = Join-Path $results 'stamina-player.log'
    $run = Start-Process -FilePath (Join-Path $build 'WorstHotelEver.exe') -ArgumentList "-whe-host -whe-port 17797 -whe-session-tests -whe-case stamina -screen-width 640 -screen-height 480 -screen-fullscreen 0 -logFile `"$log`"" -WindowStyle Hidden -PassThru
    if (!$run.WaitForExit(120000)) { throw 'Stamina player timed out.' }
    $sessionPath = Join-Path $results 'stamina-session.txt'
    if (!(Test-Path -LiteralPath $sessionPath) -or (Get-Item -LiteralPath $sessionPath).LastWriteTime -lt $started) { throw 'Stale/missing stamina session report.' }
    $session = @(Get-Content -LiteralPath $sessionPath)
    $session
    $records.AddRange([string[]]$session)
    if ($run.ExitCode -ne 0 -or $session[0] -ne 'PASS' -or !($session -contains 'STAMINA_REAL_IDLE_WALK_SPRINT_SPEEDS_AND_DRAIN')) { throw 'Stamina player failed.' }
    if ((Get-FileHash -LiteralPath $dll).Hash -ne $hash) { throw 'Runtime changed during stamina test.' }
    $records.Add('OVERALL_PASS: process-isolated stamina input/lifecycle checks. Logical focus fixture is not an OS focus test.')
} catch {
    $records.Add('OVERALL_FAIL: ' + $_.Exception.Message)
    throw
} finally {
    if ($null -ne $run -and !$run.HasExited) { Stop-Process -InputObject $run -ErrorAction SilentlyContinue }
    [IO.File]::WriteAllLines($reportPath,$records)
}
$records
