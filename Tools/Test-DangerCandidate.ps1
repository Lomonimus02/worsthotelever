param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$reportPath = Join-Path $projectRoot 'TestResults\danger-candidate-results.txt'
$records = [System.Collections.Generic.List[string]]::new()
$records.Add('Danger alpha candidate verification: ' + (Get-Date).ToString('o'))
function Run-Stage([string]$Name, [hashtable]$Options = @{}) {
    Write-Output "RUN: $Name"
    & (Join-Path $PSScriptRoot $Name) @Options
    $records.Add('PASS: ' + $Name + ' ' + (($Options.Keys | Sort-Object) -join ','))
    [IO.File]::WriteAllLines($reportPath, $records)
}
try {
    if (!$SkipBuild) { Run-Stage 'Build-Windows.ps1' }
    $dllPath = Join-Path $projectRoot 'Builds\Windows\WorstHotelEver_Data\Managed\Assembly-CSharp.dll'
    $dllHash = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash
    $records.Add('Runtime SHA256: ' + $dllHash)
    Run-Stage 'Test-MvpCandidate.ps1' @{SkipBuild = $true}
    Run-Stage 'Test-PlaytestSlot.ps1' @{Danger = $true}
    Run-Stage 'Test-Danger.ps1'
    Run-Stage 'Test-Danger.ps1' @{Checkpoint = $true}
    Run-Stage 'Test-Danger.ps1' @{Forfeit = $true}
    Run-Stage 'Test-DangerCoop.ps1'
    Run-Stage 'Test-DangerCoop.ps1' @{Impaired = $true}
    if ((Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash -ne $dllHash) { throw 'Runtime changed during danger verification.' }
    $records.Add('OVERALL_PASS: automated local checks only. External Steam WAN, human enjoyment and commercial release acceptance NOT tested.')
} catch {
    $records.Add('OVERALL_FAIL: ' + $_.Exception.Message)
    throw
} finally { [IO.File]::WriteAllLines($reportPath, $records) }
$records
