param(
    [string]$EditorData = 'C:/Program Files/Unity/Hub/Editor/6000.3.2f1/Editor/Data',
    [string]$NgoDll = 'C:/Normal/Programs/Game/Worst Hotel Ever/Library/ScriptAssemblies/Unity.Netcode.Runtime.dll'
)
$ErrorActionPreference = 'Stop'
$steamRoot = Split-Path -Parent $PSScriptRoot
$assetsRoot = Split-Path -Parent $steamRoot
$checkRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('whe-steam-validation-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $checkRoot | Out-Null
$sources = @((Join-Path $assetsRoot 'Scripts/HotelSteam.cs')) + @(Get-ChildItem "$steamRoot/Runtime" -Filter '*.cs' | ForEach-Object FullName)
$refs = @(Get-ChildItem "$EditorData/NetStandard/ref/2.1.0" -Filter '*.dll' | ForEach-Object FullName)
$refs += @(Get-ChildItem "$EditorData/NetStandard/compat/2.1.0/shims/netfx" -Filter '*.dll' | ForEach-Object FullName)
$core = "$EditorData/Managed/UnityEngine/UnityEngine.CoreModule.dll"
$facepunch = "$steamRoot/Plugins/Facepunch.Steamworks.Win64.dll"
$refs += @($core, $NgoDll)
$compiler = "$EditorData/DotNetSdkRoslyn/csc.dll"
foreach ($target in @('UNITY_STANDALONE_WIN', 'UNITY_EDITOR_WIN', 'NO_STEAM_PLATFORM')) {
    $response = @('-nologo', '-target:library', '-langversion:9.0', '-nostdlib+', '-warnaserror+', "-define:$target", ('-out:"' + $checkRoot + '/' + $target + '.dll"'))
    $response += $refs | ForEach-Object { '-r:"' + $_ + '"' }
    if ($target -ne 'NO_STEAM_PLATFORM') { $response += '-r:"' + $facepunch + '"' }
    $response += $sources | ForEach-Object { '"' + $_ + '"' }
    $response | Set-Content "$checkRoot/$target.rsp"
    & dotnet $compiler "@$checkRoot/$target.rsp"
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $target" }
    Write-Output "PASS: $target compiled with installed Unity and NGO references, warnings as errors."
}
$response = @('-nologo', '-target:exe', '-langversion:9.0', '-nostdlib+', '-define:UNITY_STANDALONE_WIN', ('-out:"' + $checkRoot + '/OfflineChecks.dll"'))
$response += ($refs + $facepunch) | ForEach-Object { '-r:"' + $_ + '"' }
$response += ($sources + "$PSScriptRoot/OfflineChecks.cs.txt") | ForEach-Object { '"' + $_ + '"' }
$response | Set-Content "$checkRoot/checks.rsp"
& dotnet $compiler "@$checkRoot/checks.rsp"
if ($LASTEXITCODE -ne 0) { throw 'Offline checks compilation failed.' }
# Runtime dependencies are copied into a fresh temp folder. Native Steam DLL is deliberately absent.
Copy-Item $core, $NgoDll, $facepunch -Destination $checkRoot
$runtime = (& dotnet --list-runtimes | Where-Object { $_ -like 'Microsoft.NETCore.App *' } | Select-Object -Last 1).Split(' ')[1]
@{ runtimeOptions = @{ tfm = ('net' + $runtime.Split('.')[0] + '.0'); framework = @{ name = 'Microsoft.NETCore.App'; version = $runtime } } } | ConvertTo-Json -Depth 5 | Set-Content "$checkRoot/OfflineChecks.runtimeconfig.json"
& dotnet "$checkRoot/OfflineChecks.dll"
if ($LASTEXITCODE -ne 0) { throw 'Offline checks failed.' }
Write-Output "Artifacts: $checkRoot"
