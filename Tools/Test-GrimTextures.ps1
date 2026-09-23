param([ValidateRange(30,300)][int]$TimeoutSeconds=120)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Get-WheBuildDirectory.ps1')
. (Join-Path $PSScriptRoot 'Get-WheVisualFingerprint.ps1')
$build = Get-WheBuildDirectory
$results = Join-Path $projectRoot 'TestResults'
$reportPath = Join-Path $results 'grim-textures-results.txt'
$sessionReport = Join-Path $results 'grim-textures-session.txt'
$records = [System.Collections.Generic.List[string]]::new()
$records.Add('Grim texture verification: ' + (Get-Date).ToString('o'))
$records.Add('Build directory: ' + $build)
$run = $null
try {
    $dll = Join-Path $build 'WorstHotelEver_Data/Managed/Assembly-CSharp.dll'
    $hash = (Get-FileHash -LiteralPath $dll).Hash
    $visualHash = Get-WheVisualFingerprint $build
    $records.Add('Runtime SHA256: ' + $hash)
    $records.Add('Visual assets SHA256: ' + $visualHash)
    $started = Get-Date
    $exe = Join-Path $build 'WorstHotelEver.exe'
    $log = Join-Path $results 'grim-textures-player.log'
    $arguments = "-whe-host -whe-port 17798 -whe-session-tests -whe-case grim-textures -screen-width 640 -screen-height 480 -screen-fullscreen 0 -logFile `"$log`""
    $run = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (!$run.WaitForExit($TimeoutSeconds*1000)) { throw 'Grim textures player timed out.' }
    if (!(Test-Path -LiteralPath $sessionReport) -or (Get-Item -LiteralPath $sessionReport).LastWriteTime -lt $started) { throw 'Missing/stale grim-textures report.' }
    $evidence = @(Get-Content -LiteralPath $sessionReport)
    $records.AddRange([string[]]$evidence)
    if ($run.ExitCode -ne 0 -or $evidence.Count -eq 0 -or $evidence[0] -ne 'PASS') { throw 'Grim-textures scenario failed.' }
    foreach ($marker in @(
        'GRIM_PRODUCTION_V3_ISOLATED_HOST_VALIDATED','GRIM_12_DISTINCT_NONFALLBACK_RESOURCES_VERIFIED',
        'GRIM_ALL_WORLD_AND_HAND_SURFACES_USE_CUSTOM_BASEMAP','GRIM_REPRESENTATIVE_WORLD_CAPTURES_COMPLETE',
        'GRIM_STAFF_GUEST_FACTORY_CAPTURES_COMPLETE','GRIM_ALL_13_PORTABLE_FACTORY_CAPTURES_COMPLETE',
        'GRIM_TEN_FAMILY_MATERIAL_PANORAMA_CAPTURED','GRIM_TABLET_RESOURCE_REFERENCES_AND_REPAINTS_VERIFIED',
        'GRIM_HUD_TABLET_1280x800_960x600_1920x1080_CAPTURED','GRIM_FINAL_STATE_VALID_UNCHANGED_GEOMETRY_REFERENCES_STABLE',
        'GRIM_NO_UNITY_OR_IMGUI_ERRORS'
    )) { if ($evidence -cnotcontains $marker) { throw ('Missing required evidence: ' + $marker) } }
    $captures = [System.Collections.Generic.List[string]]::new()
    foreach ($label in @('lobby','corridor','services','bedroom-clean','bedroom-dirty','bedroom-power-off','staff','guest','material-panorama',
        'item-bag','item-toolbox','item-mop','item-linen','item-towel','item-dirtylinen','item-dirtytowel','item-trashbag',
        'item-cart','item-plunger','item-coffee','item-coffeecup','item-bag-large')) { $captures.Add('grim-1280x800-' + $label + '.png') }
    foreach ($size in @('1280x800','960x600','1920x1080')) {
        foreach ($label in @('hud','tablet')) { $captures.Add('grim-' + $size + '-' + $label + '.png') }
    }
    foreach ($capture in $captures) {
        $path = Join-Path $results $capture
        if (!(Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).LastWriteTime -lt $started -or
            (Get-Item -LiteralPath $path).Length -eq 0 -or $evidence -cnotcontains ('GRIM_CAPTURE ' + $capture)) { throw ('Missing/stale/unobserved capture: ' + $capture) }
    }
    if ((Get-FileHash -LiteralPath $dll).Hash -ne $hash -or (Get-WheVisualFingerprint $build) -ne $visualHash) { throw 'Runtime/assets changed during test.' }
    $records.Add('OVERALL_PASS: isolated production player; all required markers and 28 fresh captures; runtime and serialized artwork unchanged. Visual/manual acceptance separate.')
} catch {
    $records.Add('OVERALL_FAIL: ' + $_.Exception.Message)
    throw
} finally {
    if ($null -ne $run -and !$run.HasExited) { Stop-Process -InputObject $run -ErrorAction SilentlyContinue }
    [IO.File]::WriteAllLines($reportPath,$records)
}
$records
