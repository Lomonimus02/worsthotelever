param([switch]$TestsOnly,[switch]$Ui)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Get-WheBuildDirectory.ps1')
$buildName = if ($Ui) { 'WindowsUI' } else { 'Windows' }
$buildDirectory = Get-WheBuildDirectory -BuildDirectory (Join-Path $projectRoot "Builds\$buildName")
if (!$TestsOnly -and $env:WHE_TEST_BUILD_DIRECTORY -and (Get-WheBuildDirectory) -ne $buildDirectory) {
    throw 'The test build-directory override does not match this build target. Use -Ui for WindowsUI, or clear the override for Windows.'
}
$unityPath = 'C:\Program Files\Unity\Hub\Editor\6000.3.2f1\Editor\Unity.exe'
$projectPath = Join-Path $projectRoot 'Worst Hotel Ever'
$resultsPath = Join-Path $projectRoot 'TestResults'
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null
if (!(Test-Path -LiteralPath $unityPath)) { throw "Unity 6000.3.2f1 not found at $unityPath" }
$assetGuids = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($meta in Get-ChildItem -LiteralPath (Join-Path $projectPath 'Assets') -Recurse -File -Filter '*.meta') {
    $match = [regex]::Match([System.IO.File]::ReadAllText($meta.FullName), '(?m)^guid: ([0-9a-fA-F]{32})\r?$')
    if (!$match.Success) { throw "Invalid Unity GUID: $($meta.FullName)" }
    if (!$assetGuids.Add($match.Groups[1].Value)) { throw "Duplicate Unity GUID: $($meta.FullName)" }
}
$method = if ($TestsOnly) { 'WorstHotel.BuildTools.HotelBuild.Validate' } elseif ($Ui) { 'WorstHotel.BuildTools.HotelBuild.BuildWindowsUi' } else { 'WorstHotel.BuildTools.HotelBuild.BuildWindows' }
$logPath = Join-Path $resultsPath 'build.log'
$arguments = "-batchmode -nographics -quit -projectPath `"$projectPath`" -executeMethod $method -logFile `"$logPath`""
$run = Start-Process -FilePath $unityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
# Wait for the editor itself, not its persistent licensing-service process tree.
$run.WaitForExit()
if ($run.ExitCode -ne 0) { Get-Content -LiteralPath $logPath -Tail 70; throw "Unity failed: $($run.ExitCode). Log: $logPath" }
Get-Content -LiteralPath (Join-Path $resultsPath 'simulation-tests.txt')
if (!$TestsOnly) { Get-Content -LiteralPath (Join-Path $resultsPath 'build-summary.txt'); Write-Output (Join-Path $buildDirectory 'WorstHotelEver.exe') }
