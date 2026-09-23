param(
    [Parameter(Mandatory=$true)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$SourceCommit,
    [ValidateLength(1,100)]
    [ValidatePattern('\A[0-9]+\.[0-9]+\.[0-9]+-[a-z0-9]+(?:-[a-z0-9]+)*\z')]
    [string]$CandidateName = '1.1.0-danger-alpha1',
    [string]$BuildDirectory = $env:WHE_TEST_BUILD_DIRECTORY,
    [string]$QaReport = 'docs/reports/qa-danger-alpha1.md',
    [string]$VerificationReport = 'TestResults/danger-candidate-results.txt',
    [string]$BuildReport = 'TestResults/build-summary.txt',
    [ValidateSet('Development','Player')]
    [string]$BuildConfiguration = 'Development'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Get-WheBuildDirectory.ps1')
$buildRoot = Get-WheBuildDirectory -BuildDirectory $BuildDirectory
$archivePath = Join-Path $projectRoot ("Builds\WorstHotelEver-$CandidateName-Windows.zip")

function Resolve-CandidateReport([string]$Path, [string]$Directory, [string]$Extension) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path -match '(^|[\\/])\.{1,2}([\\/]|$)' -or
        $Path -match '(^|[\\/])[^\\/]*[. ]([\\/]|$)') {
        throw 'Report path must be nonempty and must not contain traversal.'
    }
    $reportRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot $Directory)).TrimEnd('\', '/')
    $fullPath = if ([IO.Path]::IsPathRooted($Path)) { [IO.Path]::GetFullPath($Path) } else {
        [IO.Path]::GetFullPath((Join-Path $projectRoot $Path))
    }
    if (!$fullPath.StartsWith($reportRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetExtension($fullPath) -ne $Extension -or !(Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Report must be an existing $Extension file under $reportRoot."
    }
    $entryPath = $fullPath
    while ($entryPath.Length -ge $reportRoot.Length) {
        if ((Get-Item -LiteralPath $entryPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Report paths must not use links: $entryPath"
        }
        $entryPath = Split-Path -Parent $entryPath
    }
    return $fullPath
}
$reportPath = Resolve-CandidateReport $VerificationReport 'TestResults' '.txt'
$qaReportPath = Resolve-CandidateReport $QaReport 'docs/reports' '.md'
$buildReportPath = Resolve-CandidateReport $BuildReport 'TestResults' '.txt'
$qaFileName = [IO.Path]::GetFileName($qaReportPath)
$qaEntry = 'docs/reports/' + $qaFileName
$runtimeRelative = 'WorstHotelEver_Data/Managed/Assembly-CSharp.dll'
$runtimeHash = (Get-FileHash -LiteralPath (Join-Path $buildRoot $runtimeRelative) -Algorithm SHA256).Hash
$report = @(Get-Content -LiteralPath $reportPath)
if (!($report -match '^OVERALL_PASS:') -or ($report -match '^OVERALL_FAIL:') -or
    !($report -contains ('Runtime SHA256: ' + $runtimeHash))) {
    throw 'The current runtime does not have a completed candidate verification report.'
}
if (($report -match '^Build directory: ') -and !($report -contains ('Build directory: ' + $buildRoot))) {
    throw 'The candidate verification report names a different build directory.'
}
$visualHash = $null
if (($report -match '^Visual assets SHA256: ') -or $CandidateName -like '*-grim-*') {
    . (Join-Path $PSScriptRoot 'Get-WheVisualFingerprint.ps1')
    $visualHash = Get-WheVisualFingerprint $buildRoot
    if (!($report -contains ('Visual assets SHA256: ' + $visualHash))) { throw 'Visual assets differ from the verified candidate.' }
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
    'README_DANGER.md' = 'READ_ME_RU.md'
    'Tools/PLAYTEST_DANGER.cmd' = 'PLAYTEST_DANGER.cmd'
    'Tools/Collect-PlaytestDiagnostics.ps1' = 'Tools/Collect-PlaytestDiagnostics.ps1'
    'docs/MVP_TWO_PC_PLAYTEST_RU.md' = 'docs/MVP_TWO_PC_PLAYTEST_RU.md'
    'docs/MVP_EXECUTION_PLAN.md' = 'docs/MVP_EXECUTION_PLAN.md'
    'docs/MVP_COMPONENT_CONTRACT.md' = 'docs/MVP_COMPONENT_CONTRACT.md'
    'docs/IMPLEMENTATION_STATUS.md' = 'docs/IMPLEMENTATION_STATUS.md'
    'docs/DANGER_PLAYTEST_RU.md' = 'docs/DANGER_PLAYTEST_RU.md'
    'docs/RELEASE_GAMEPLAY_PLAN.md' = 'docs/RELEASE_GAMEPLAY_PLAN.md'
    'docs/UI_REDESIGN_PLAN.md' = 'docs/UI_REDESIGN_PLAN.md'
    'docs/TABLET_UI_PLAN.md' = 'docs/TABLET_UI_PLAN.md'
    'docs/images/ui-alpha1-hud.png' = 'docs/images/ui-alpha1-hud.png'
    'docs/images/ui-alpha1-journal.png' = 'docs/images/ui-alpha1-journal.png'
    'docs/images/tablet-alpha1-hud.png' = 'docs/images/tablet-alpha1-hud.png'
    'docs/images/tablet-alpha1-device.png' = 'docs/images/tablet-alpha1-device.png'
    'docs/images/tablet-alpha1-hint.png' = 'docs/images/tablet-alpha1-hint.png'
    'WORST_HOTEL_EVER_MASTER_PLAN.md' = 'WORST_HOTEL_EVER_MASTER_PLAN.md'
}
$copies[$qaReportPath] = $qaEntry
if ($CandidateName -like '*-grim-*') {
    foreach ($relative in @('docs/GRIM_TEXTURES_PLAN.md','docs/art/grim-textures-v1.json','docs/images/grim-alpha1-lobby.png','docs/images/grim-alpha1-bedroom.png','docs/images/grim-alpha1-tablet.png')) { $copies[$relative] = $relative }
}
$copies[$reportPath] = 'LOCAL_TEST_RESULTS.txt'
$copies[$buildReportPath] = 'BUILD_RESULT.txt'
foreach ($source in $copies.Keys) {
    $destination = Join-Path $buildRoot $copies[$source]
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    $sourcePath = if ([IO.Path]::IsPathRooted($source)) { $source } else { Join-Path $projectRoot $source }
    Copy-Item -LiteralPath $sourcePath -Destination $destination -Force
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'README_DANGER.md') -Destination (Join-Path $buildRoot 'README_DANGER.md') -Force
$manifest = [ordered]@{
    candidate = $CandidateName
    sourceCommit = $SourceCommit
    unity = '6000.3.2f1'
    platform = 'Windows x64; Mono; DX11; ' + $BuildConfiguration
    protocol = 'WHE-danger-5'
    runtimePath = $runtimeRelative
    runtimeSha256 = $runtimeHash
    visualAssetsSha256 = $visualHash
    packagedAt = (Get-Date).ToString('o')
    localVerification = 'OVERALL_PASS; see LOCAL_TEST_RESULTS.txt'
    qaReport = $qaEntry
    acceptance = 'External two-PC Steam WAN and human playtest remain unverified.'
    launch = 'PLAYTEST_DANGER.cmd (isolated persistent playtest slot)'
}
[IO.File]::WriteAllText((Join-Path $buildRoot 'CANDIDATE.json'), ($manifest | ConvertTo-Json), [Text.UTF8Encoding]::new($false))

Add-Type -AssemblyName System.IO.Compression.FileSystem
$rootPrefix = [IO.Path]::GetFullPath($buildRoot).TrimEnd('\') + '\'
$files = @(Get-ChildItem -LiteralPath $buildRoot -Recurse -File | Where-Object {
    $_.FullName -notmatch '[\\/][^\\/]*_BurstDebugInformation_DoNotShip[\\/]' -and $_.Extension -ne '.pdb' -and $_.Name -notin @('README_MVP.md','PLAYTEST_MVP.cmd') -and
    ($_.FullName -notmatch '[\\/]docs[\\/]reports[\\/]qa-[^\\/]+[.]md$' -or $_.Name -eq $qaFileName)
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
if ($visualHash -and (Get-WheVisualFingerprint $buildRoot) -ne $visualHash) { throw 'Visual assets changed during packaging.' }
$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    if ($expected[$runtimeRelative] -ne $runtimeHash) { throw 'Packaged runtime no longer matches the candidate verification report.' }
    if ($archive.Entries.Count -ne $expected.Count) { throw 'Archive entry count mismatch.' }
    foreach ($entry in $archive.Entries) {
        $entryStream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($sha.ComputeHash($entryStream)).Replace('-','') }
        finally { $sha.Dispose(); $entryStream.Dispose() }
        if ($hash -ne $expected[$entry.FullName]) { throw ('Archive hash mismatch: ' + $entry.FullName) }
    }
    foreach ($required in @('WorstHotelEver.exe', $runtimeRelative, 'WorstHotelEver_Data/Managed/Facepunch.Steamworks.Win64.dll',
        'WorstHotelEver_Data/Plugins/x86_64/steam_api64.dll', 'PLAYTEST_DANGER.cmd', 'READ_ME_RU.md', 'CANDIDATE.json',
        'LOCAL_TEST_RESULTS.txt', 'BUILD_RESULT.txt', $qaEntry, 'Tools/Collect-PlaytestDiagnostics.ps1', 'docs/MVP_TWO_PC_PLAYTEST_RU.md')) {
        if (!$expected.ContainsKey($required)) { throw ('Missing required packaged file: ' + $required) }
    }
    if (@($expected.Keys | Where-Object { $_ -like 'ThirdPartyNotices/*' }).Count -eq 0) { throw 'Third-party notices missing.' }
} finally { $archive.Dispose() }
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
$checksumBytes = [Text.UTF8Encoding]::new($false).GetBytes($archiveHash + '  ' + [IO.Path]::GetFileName($archivePath) + [Environment]::NewLine)
$checksumStream = [IO.File]::Open(($archivePath + '.sha256'), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try { $checksumStream.Write($checksumBytes, 0, $checksumBytes.Length) } finally { $checksumStream.Dispose() }
[pscustomobject]@{ Archive=$archivePath; Bytes=(Get-Item -LiteralPath $archivePath).Length; Files=$expected.Count; SHA256=$archiveHash; RuntimeSHA256=$runtimeHash }
