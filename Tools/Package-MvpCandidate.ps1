param(
    [Parameter(Mandatory=$true)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$SourceCommit
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Get-WheBuildDirectory.ps1')
$buildRoot = Get-WheBuildDirectory
$archivePath = Join-Path $projectRoot 'Builds\WorstHotelEver-MVP-1.0.0-rc1-Windows.zip'
$reportPath = Join-Path $projectRoot 'TestResults\mvp-candidate-results.txt'
$runtimeRelative = 'WorstHotelEver_Data/Managed/Assembly-CSharp.dll'
$runtimeHash = (Get-FileHash -LiteralPath (Join-Path $buildRoot $runtimeRelative) -Algorithm SHA256).Hash
$report = [IO.File]::ReadAllText($reportPath)
if (!$report.Contains('OVERALL_PASS:') -or !$report.Contains('Runtime SHA256: ' + $runtimeHash)) {
    throw 'The current runtime does not have a completed candidate verification report.'
}
if ((Test-Path -LiteralPath $archivePath) -or (Test-Path -LiteralPath ($archivePath + '.sha256'))) {
    throw 'This candidate archive/checksum already exists. Do not overwrite an issued candidate.'
}
$sourceAtHead = (& git -C $projectRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceAtHead -ne $SourceCommit) { throw 'SourceCommit must name the current committed source.' }
$sourceChanges = @(& git -C $projectRoot status --porcelain -- 'Worst Hotel Ever/Assets' 'Worst Hotel Ever/Packages' 'Worst Hotel Ever/ProjectSettings')
if ($LASTEXITCODE -ne 0 -or $sourceChanges.Count -ne 0) { throw 'Unity source/configuration must be committed before packaging.' }

# Refresh documentation only; never modify the tested runtime or persistent saves.
$copies = @{
    'README_MVP.md' = 'READ_ME_RU.md'
    'Tools/PLAYTEST_MVP.cmd' = 'PLAYTEST_MVP.cmd'
    'Tools/Collect-PlaytestDiagnostics.ps1' = 'Tools/Collect-PlaytestDiagnostics.ps1'
    'docs/MVP_TWO_PC_PLAYTEST_RU.md' = 'docs/MVP_TWO_PC_PLAYTEST_RU.md'
    'docs/MVP_EXECUTION_PLAN.md' = 'docs/MVP_EXECUTION_PLAN.md'
    'docs/MVP_COMPONENT_CONTRACT.md' = 'docs/MVP_COMPONENT_CONTRACT.md'
    'docs/IMPLEMENTATION_STATUS.md' = 'docs/IMPLEMENTATION_STATUS.md'
    'docs/reports/qa-mvp-rc1.md' = 'docs/reports/qa-mvp-rc1.md'
    'WORST_HOTEL_EVER_MASTER_PLAN.md' = 'WORST_HOTEL_EVER_MASTER_PLAN.md'
    'TestResults/mvp-candidate-results.txt' = 'LOCAL_TEST_RESULTS.txt'
    'TestResults/build-summary.txt' = 'BUILD_RESULT.txt'
}
foreach ($source in $copies.Keys) {
    $destination = Join-Path $buildRoot $copies[$source]
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot $source) -Destination $destination -Force
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'README_MVP.md') -Destination (Join-Path $buildRoot 'README_MVP.md') -Force
$manifest = [ordered]@{
    candidate = '1.0.0-mvp-rc1'
    sourceCommit = $SourceCommit
    unity = '6000.3.2f1'
    platform = 'Windows x64; Mono; DX11; development build'
    protocol = 'WHE-mvp-4'
    runtimePath = $runtimeRelative
    runtimeSha256 = $runtimeHash
    packagedAt = (Get-Date).ToString('o')
    localVerification = 'OVERALL_PASS; see LOCAL_TEST_RESULTS.txt'
    acceptance = 'External two-PC Steam WAN and human playtest remain unverified.'
    launch = 'PLAYTEST_MVP.cmd (isolated persistent playtest slot)'
}
[IO.File]::WriteAllText((Join-Path $buildRoot 'CANDIDATE.json'), ($manifest | ConvertTo-Json), [Text.UTF8Encoding]::new($false))

Add-Type -AssemblyName System.IO.Compression.FileSystem
$rootPrefix = [IO.Path]::GetFullPath($buildRoot).TrimEnd('\') + '\'
$files = @(Get-ChildItem -LiteralPath $buildRoot -Recurse -File | Where-Object {
    $_.FullName -notmatch '[\\/][^\\/]*_BurstDebugInformation_DoNotShip[\\/]' -and $_.Extension -ne '.pdb'
} | Sort-Object FullName)
$expected = @{}
$stream = [IO.File]::Open($archivePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($file in $files) {
            if (!$file.FullName.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'File escaped the build directory.' }
            $entryName = $file.FullName.Substring($rootPrefix.Length).Replace('\','/')
            $expected[$entryName] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose() }
} finally { $stream.Dispose() }

# Read and hash every compressed entry, not just the EXE bootstrap.
$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    if ($archive.Entries.Count -ne $expected.Count) { throw 'Archive entry count mismatch.' }
    foreach ($entry in $archive.Entries) {
        $entryStream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($sha.ComputeHash($entryStream)).Replace('-','') }
        finally { $sha.Dispose(); $entryStream.Dispose() }
        if ($hash -ne $expected[$entry.FullName]) { throw ('Archive hash mismatch: ' + $entry.FullName) }
    }
    foreach ($required in @('WorstHotelEver.exe', $runtimeRelative, 'WorstHotelEver_Data/Managed/Facepunch.Steamworks.Win64.dll',
        'WorstHotelEver_Data/Plugins/x86_64/steam_api64.dll', 'PLAYTEST_MVP.cmd', 'READ_ME_RU.md', 'CANDIDATE.json',
        'LOCAL_TEST_RESULTS.txt', 'Tools/Collect-PlaytestDiagnostics.ps1', 'docs/MVP_TWO_PC_PLAYTEST_RU.md')) {
        if (!$expected.ContainsKey($required)) { throw ('Missing required packaged file: ' + $required) }
    }
    if (@($expected.Keys | Where-Object { $_ -like 'ThirdPartyNotices/*' }).Count -eq 0) { throw 'Third-party notices missing.' }
} finally { $archive.Dispose() }
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
[IO.File]::WriteAllText(($archivePath + '.sha256'), ($archiveHash + '  ' + [IO.Path]::GetFileName($archivePath) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
[pscustomobject]@{ Archive=$archivePath; Bytes=(Get-Item -LiteralPath $archivePath).Length; Files=$expected.Count; SHA256=$archiveHash; RuntimeSHA256=$runtimeHash }
