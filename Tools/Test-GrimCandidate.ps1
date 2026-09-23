param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Get-WheBuildDirectory.ps1')
. (Join-Path $PSScriptRoot 'Get-WheVisualFingerprint.ps1')
$build = Get-WheBuildDirectory -BuildDirectory (Join-Path $projectRoot 'Builds/WindowsGrim')
$reportPath = Join-Path $projectRoot 'TestResults/grim-candidate-results.txt'
$records = [System.Collections.Generic.List[string]]::new()
$records.Add('Grim texture candidate verification: ' + (Get-Date).ToString('o'))
$records.Add('Build directory: ' + $build)
$previousBuild = $env:WHE_TEST_BUILD_DIRECTORY
$previousLive = $env:WHE_TEST_PRESERVE_LIVE_DANGER_SESSION
try {
    $env:WHE_TEST_BUILD_DIRECTORY = $build
    $env:WHE_TEST_PRESERVE_LIVE_DANGER_SESSION = '0'
    if (!$SkipBuild) { & (Join-Path $PSScriptRoot 'Build-Windows.ps1') -Grim }
    $dll = Join-Path $build 'WorstHotelEver_Data/Managed/Assembly-CSharp.dll'
    $hash = (Get-FileHash -LiteralPath $dll).Hash
    $visualHash = Get-WheVisualFingerprint $build
    $records.Add('Runtime SHA256: ' + $hash)
    $records.Add('Visual assets SHA256: ' + $visualHash)
    foreach ($stage in @(
        @{ Script='Test-GrimTextures.ps1'; Report='grim-textures-results.txt'; Options=@{} },
        @{ Script='Test-Stamina.ps1'; Report='stamina-results.txt'; Options=@{} },
        @{ Script='Test-TabletUI.ps1'; Report='tablet-ui-results.txt'; Options=@{} },
        @{ Script='Test-DangerCandidate.ps1'; Report='danger-candidate-results.txt'; Options=@{SkipBuild=$true} },
        @{ Script='Test-JournalUI.ps1'; Report='journal-ui-results.txt'; Options=@{} }
    )) {
        $started = Get-Date
        Write-Output ('RUN: ' + $stage.Script)
        $options = $stage.Options
        & (Join-Path $PSScriptRoot $stage.Script) @options
        $path = Join-Path $projectRoot ('TestResults/' + $stage.Report)
        if (!(Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).LastWriteTime -lt $started) { throw ('Stale/missing report: ' + $stage.Report) }
        $result = @(Get-Content -LiteralPath $path)
        if (!($result -match '^OVERALL_PASS:') -or ($result -match '^OVERALL_FAIL:') -or
            !($result -contains ('Runtime SHA256: ' + $hash)) -or !($result -contains ('Build directory: ' + $build))) {
            throw ('Report does not pass for this candidate: ' + $stage.Report)
        }
        $records.Add('PASS: ' + $stage.Script)
        [IO.File]::WriteAllLines($reportPath,$records)
    }
    if ((Get-FileHash -LiteralPath $dll).Hash -ne $hash -or (Get-WheVisualFingerprint $build) -ne $visualHash) { throw 'Runtime or visual assets changed during verification.' }
    $records.Add('OVERALL_PASS: local texture, tablet, stamina, legacy, MVP, danger and journal checks; manual visual review recorded separately. Human acceptance and Steam WAN unverified.')
} catch {
    $records.Add('OVERALL_FAIL: ' + $_.Exception.Message)
    throw
} finally {
    [Environment]::SetEnvironmentVariable('WHE_TEST_BUILD_DIRECTORY',$previousBuild,'Process')
    [Environment]::SetEnvironmentVariable('WHE_TEST_PRESERVE_LIVE_DANGER_SESSION',$previousLive,'Process')
    [IO.File]::WriteAllLines($reportPath,$records)
}
$records
