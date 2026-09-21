param([switch]$TestsOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$unityPath = 'C:\Program Files\Unity\Hub\Editor\6000.3.2f1\Editor\Unity.exe'
$projectPath = Join-Path $projectRoot 'Worst Hotel Ever'
$resultsPath = Join-Path $projectRoot 'TestResults'
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null
if (!(Test-Path -LiteralPath $unityPath)) { throw "Unity 6000.3.2f1 not found at $unityPath" }
$method = if ($TestsOnly) { 'WorstHotel.BuildTools.HotelBuild.Validate' } else { 'WorstHotel.BuildTools.HotelBuild.BuildWindows' }
$logPath = Join-Path $resultsPath 'build.log'
$arguments = "-batchmode -nographics -quit -projectPath `"$projectPath`" -executeMethod $method -logFile `"$logPath`""
$run = Start-Process -FilePath $unityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
if ($run.ExitCode -ne 0) { Get-Content -LiteralPath $logPath -Tail 70; throw "Unity failed: $($run.ExitCode). Log: $logPath" }
Get-Content -LiteralPath (Join-Path $resultsPath 'simulation-tests.txt')
if (!$TestsOnly) { Get-Content -LiteralPath (Join-Path $resultsPath 'build-summary.txt'); Write-Output (Join-Path $projectRoot 'Builds\Windows\WorstHotelEver.exe') }
