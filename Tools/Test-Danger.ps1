param([switch]$Checkpoint,[switch]$Forfeit)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$resultsPath = Join-Path $projectRoot 'TestResults'
. (Join-Path $PSScriptRoot 'Get-WheBuildDirectory.ps1')
$exePath = Join-Path (Get-WheBuildDirectory) 'WorstHotelEver.exe'
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null
$scenarios = if ($Checkpoint) { @('danger-checkpoint-write','danger-checkpoint-read') } elseif ($Forfeit) { @('danger-forfeit') } else { @('danger-interaction') }
$world = ''
foreach ($scenario in $scenarios) {
    $started = Get-Date
    $logPath = Join-Path $resultsPath ($scenario + '-player.log')
    $extra = if ($Checkpoint) { '-whe-danger-checkpoint' } else { '' }
    if ($scenario -eq 'danger-checkpoint-read') { $extra += ' -whe-load' }
    $arguments = "-whe-host -whe-port 17793 -whe-session-tests $extra -whe-case $scenario -screen-width 640 -screen-height 480 -screen-fullscreen 0 -logFile `"$logPath`""
    $run = Start-Process -FilePath $exePath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    try {
        $deadline = (Get-Date).AddSeconds(150)
        while (!$run.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
        if (!$run.HasExited) { throw "$scenario timed out." }
        $path = Join-Path $resultsPath ($scenario + '-session.txt')
        if (!(Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).LastWriteTime -lt $started) { throw "Missing/stale $scenario report." }
        $report = Get-Content -LiteralPath $path
        $report
        if ($run.ExitCode -ne 0 -or $report[0] -ne 'PASS') { throw "$scenario failed." }
        if ($Checkpoint) {
            $envelope = Get-Content -Raw -LiteralPath (Join-Path $resultsPath 'danger-restart-checkpoint.json') | ConvertFrom-Json
            $saved = $envelope.payload | ConvertFrom-Json
            if ($saved.version -ne 3 -or $saved.danger.crew[0].life -ne 'downed') { throw 'Checkpoint is not durable downed v3.' }
            if ($world -and $world -ne $saved.worldId) { throw 'Restart replaced the hotel.' }
            $world = $saved.worldId
        }
    } finally { if (!$run.HasExited) { Stop-Process -Id $run.Id -ErrorAction SilentlyContinue } }
}
