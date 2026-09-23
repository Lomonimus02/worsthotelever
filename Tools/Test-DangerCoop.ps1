param([switch]$Impaired)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$resultsPath = Join-Path $projectRoot 'TestResults'
. (Join-Path $PSScriptRoot 'Get-WheBuildDirectory.ps1')
$exePath = Join-Path (Get-WheBuildDirectory) 'WorstHotelEver.exe'
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null
$label = if ($Impaired) { 'danger-delayed' } else { 'danger-coop' }
$clientPort = if ($Impaired) { 17795 } else { 17794 }
$extra = if ($Impaired) { '-whe-no-reconnect' } else { '' }
$runs = @()
$started = Get-Date
try {
    if ($Impaired) {
        $proxyLog = Join-Path $resultsPath "$label-proxy.log"
        $proxyErrors = Join-Path $resultsPath "$label-proxy-errors.log"
        $proxyScript = Join-Path $PSScriptRoot 'udp-test-proxy.cjs'
        $nodePath = (Get-Command node -ErrorAction Stop).Source
        $proxy = Start-Process -FilePath $nodePath -ArgumentList "`"$proxyScript`" 17795 17794 75 1" -WindowStyle Hidden -PassThru -RedirectStandardOutput $proxyLog -RedirectStandardError $proxyErrors
        $runs += $proxy
        Start-Sleep -Milliseconds 800
        if ($proxy.HasExited -or !(Select-String -LiteralPath $proxyLog -Pattern 'READY' -Quiet)) { throw 'Proxy did not start.' }
    }
    foreach ($role in @('host','client')) {
        $log = Join-Path $resultsPath "$label-$role-player.log"
        $connection = if ($role -eq 'host') { '-whe-host -whe-port 17794' } else { "-whe-client 127.0.0.1 -whe-port $clientPort" }
        $playerArguments = "$connection -whe-session-tests $extra -whe-case danger-coop-$role -screen-width 640 -screen-height 480 -screen-fullscreen 0 -logFile `"$log`""
        $runs += Start-Process -FilePath $exePath -ArgumentList $playerArguments -WindowStyle Hidden -PassThru
        if ($role -eq 'host') { Start-Sleep -Seconds 2 }
    }
    $players = @($runs | Where-Object { $_.ProcessName -eq 'WorstHotelEver' })
    if ($players.Count -ne 2) { throw 'Expected exactly two test player processes.' }
    $deadline = (Get-Date).AddSeconds(110)
    while ((Get-Date) -lt $deadline -and @($players | Where-Object { !$_.HasExited }).Count) { Start-Sleep -Milliseconds 300 }
    if (@($players | Where-Object { !$_.HasExited }).Count) { throw 'Danger network test exceeded deadline.' }
    foreach ($role in @('host','client')) {
        $path = Join-Path $resultsPath "danger-coop-$role-session.txt"
        if (!(Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).LastWriteTime -lt $started) { throw "Missing/stale $role report." }
        $report = Get-Content -LiteralPath $path
        Copy-Item -LiteralPath $path -Destination (Join-Path $resultsPath "$label-$role.txt") -Force
        $report
        if ($report[0] -ne 'PASS') { throw "Danger $role failed." }
    }
    if (@($players | Where-Object { $_.ExitCode -ne 0 }).Count) { throw 'Danger player returned failure.' }
    if ($Impaired) {
        Get-Content -LiteralPath $proxyLog
        if ((Get-Item -LiteralPath $proxyErrors).Length -ne 0 -or !(Select-String -LiteralPath $proxyLog -Pattern 'dropped=[1-9]' -Quiet)) { throw 'Impairment not verified.' }
    }
} finally { foreach ($run in $runs) { if (!$run.HasExited) { Stop-Process -Id $run.Id -ErrorAction SilentlyContinue } } }
