param([switch]$Impaired)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path $projectRoot 'Builds\Windows\WorstHotelEver.exe'
$resultsPath = Join-Path $projectRoot 'TestResults'
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null
if (!(Test-Path -LiteralPath $exePath)) { throw 'Build the MVP Windows player first.' }
$label = if ($Impaired) { 'mvp-delayed' } else { 'mvp-coop' }
$hostLog = Join-Path $resultsPath "$label-host-player.log"
$clientLog = Join-Path $resultsPath "$label-client-player.log"
$clientPort = if ($Impaired) { 17785 } else { 17784 }
$hostArgs = "-whe-host -whe-smoke -whe-mvp-fixture -whe-port 17784 -screen-width 1280 -screen-height 800 -screen-fullscreen 0 -logFile `"$hostLog`""
$clientArgs = "-whe-client 127.0.0.1 -whe-smoke -whe-mvp-fixture -whe-port $clientPort -screen-width 1280 -screen-height 800 -screen-fullscreen 0 -logFile `"$clientLog`""
$testStarted = Get-Date
$runs = @()
try {
    if ($Impaired) {
        $nodePath = (Get-Command node -ErrorAction Stop).Source
        $proxyScript = Join-Path $PSScriptRoot 'udp-test-proxy.cjs'
        $proxyLog = Join-Path $resultsPath "$label-proxy.log"
        $proxyErrors = Join-Path $resultsPath "$label-proxy-errors.log"
        $proxyArgs = "`"$proxyScript`" 17785 17784 75 1"
        $proxy = Start-Process -FilePath $nodePath -ArgumentList $proxyArgs -WindowStyle Hidden -PassThru -RedirectStandardOutput $proxyLog -RedirectStandardError $proxyErrors
        $runs += $proxy
        Start-Sleep -Milliseconds 800
        if ($proxy.HasExited -or !(Test-Path -LiteralPath $proxyLog) -or !(Select-String -LiteralPath $proxyLog -Pattern 'READY' -Quiet)) { throw 'UDP impairment proxy did not start.' }
    }
    $hostRun = Start-Process -FilePath $exePath -ArgumentList $hostArgs -WindowStyle Hidden -PassThru
    $runs += $hostRun
    Start-Sleep -Seconds 3
    $clientRun = Start-Process -FilePath $exePath -ArgumentList $clientArgs -WindowStyle Hidden -PassThru
    $runs += $clientRun
    $deadline = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $deadline -and (!$hostRun.HasExited -or !$clientRun.HasExited)) { Start-Sleep -Milliseconds 500 }
    if (!$hostRun.HasExited -or !$clientRun.HasExited) { throw 'MVP co-op exceeded test deadline.' }
    foreach ($role in @('host','client')) {
        $reportPath = Join-Path $resultsPath "$role-smoke.txt"
        if (!(Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTime -lt $testStarted) { throw "Missing/stale $role report." }
        $report = Get-Content -LiteralPath $reportPath
        Copy-Item -LiteralPath $reportPath -Destination (Join-Path $resultsPath "$label-$role.txt") -Force
        $report
        if ($report[0] -ne 'PASS' -or !($report -contains 'SIX_ROOMS_FOUR_OWNED')) { throw "MVP $role failed or accidentally ran legacy fixture." }
    }
    if ($Impaired) {
        Get-Content -LiteralPath $proxyLog
        if ((Get-Item -LiteralPath $proxyErrors).Length -ne 0) { throw 'UDP proxy reported errors.' }
        if (!(Select-String -LiteralPath $proxyLog -Pattern 'dropped=[1-9]' -Quiet)) { throw 'Impairment test did not observe actual packet loss.' }
    }
} finally {
    foreach ($run in $runs) { if (!$run.HasExited) { Stop-Process -Id $run.Id -ErrorAction SilentlyContinue } }
}
