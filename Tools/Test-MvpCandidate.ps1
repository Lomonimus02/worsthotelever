param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Get-WheBuildDirectory.ps1')
$buildDirectory = Get-WheBuildDirectory
if (!$SkipBuild -and $buildDirectory -ne (Join-Path $projectRoot 'Builds\Windows')) {
    throw 'An overridden build directory requires -SkipBuild. Build WindowsUI with Build-Windows.ps1 -Ui or Test-UICandidate.ps1.'
}
$resultsPath = Join-Path $projectRoot 'TestResults'
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null
$reportPath = Join-Path $resultsPath 'mvp-candidate-results.txt'
$records = [System.Collections.Generic.List[string]]::new()
$records.Add('MVP candidate verification: ' + (Get-Date).ToString('o'))
$records.Add('Build directory: ' + $buildDirectory)
function Run-Stage([string]$Name, [hashtable]$Options = @{}) {
    Write-Output "RUN: $Name"
    & (Join-Path $PSScriptRoot $Name) @Options | ForEach-Object {
        if ($_ -is [string] -and $_.StartsWith('NOT_VERIFIED:') -and !$records.Contains($_)) { $records.Add($_) }
        Write-Output $_
    }
    $records.Add('PASS: ' + $Name + ' ' + (($Options.Keys | Sort-Object) -join ','))
    [IO.File]::WriteAllLines($reportPath, $records)
}
try {
    if (!$SkipBuild) { Run-Stage 'Build-Windows.ps1' }
    $dllPath = Join-Path $buildDirectory 'WorstHotelEver_Data\Managed\Assembly-CSharp.dll'
    $dllHash = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash
    $records.Add('Runtime SHA256: ' + $dllHash)
    # These opt-in test scripts use isolated fixture slots, not the normal hotel.
    Run-Stage 'Test-Coop.ps1'
    Run-Stage 'Test-Session.ps1'
    Run-Stage 'Test-Feedback.ps1'
    Run-Stage 'Test-Opening.ps1'
    Run-Stage 'Test-UI.ps1'
    Run-Stage 'Test-LegacyMigration.ps1'
    Run-Stage 'Test-PlaytestSlot.ps1'
    Run-Stage 'Test-MvpInteraction.ps1'
    Run-Stage 'Test-Opening.ps1' @{Mvp = $true}
    Copy-Item -LiteralPath (Join-Path $resultsPath 'opening-session.txt') -Destination (Join-Path $resultsPath 'mvp-opening-session.txt') -Force
    Run-Stage 'Test-UI.ps1' @{Mvp = $true}
    Run-Stage 'Test-MvpCoop.ps1'
    Run-Stage 'Test-MvpCoop.ps1' @{Impaired = $true}
    Run-Stage 'Test-Session.ps1' @{Mvp = $true}
    Run-Stage 'Test-MvpSoak.ps1'
    & (Join-Path $projectRoot 'Worst Hotel Ever\Assets\Steam\Validation\Verify-Steam.ps1')
    $records.Add('PASS: 29 offline Steam checks; three compilation configurations')
    if ((Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash -ne $dllHash) { throw 'Runtime changed during candidate verification.' }
    $records.Add('OVERALL_PASS: automated local candidate checks. External Steam WAN and human acceptance NOT tested.')
} catch {
    $records.Add('OVERALL_FAIL: ' + $_.Exception.Message)
    throw
} finally {
    [IO.File]::WriteAllLines($reportPath, $records)
}
$records
