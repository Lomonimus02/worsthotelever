# Dot-source this file; resolving a directory never creates it or starts a process.
function Get-WheBuildDirectory {
    [CmdletBinding()]
    param([string]$BuildDirectory = $env:WHE_TEST_BUILD_DIRECTORY)

    $repositoryRoot = Split-Path -Parent $PSScriptRoot
    $buildsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'Builds')).TrimEnd('\', '/')
    if ([string]::IsNullOrEmpty($BuildDirectory)) {
        $BuildDirectory = Join-Path $buildsRoot 'Windows'
    }
    if ($BuildDirectory -notmatch '^[A-Za-z]:[\\/]' -or
        $BuildDirectory -match '(^|[\\/])\.{1,2}([\\/]|$)' -or
        $BuildDirectory -match '(^|[\\/])[^\\/]*[. ]([\\/]|$)') {
        throw 'Build directory must be an explicit full drive path under the repository Builds directory, without traversal.'
    }
    $resolvedDirectory = [IO.Path]::GetFullPath($BuildDirectory).TrimEnd('\', '/')
    $buildsPrefix = $buildsRoot + [IO.Path]::DirectorySeparatorChar
    if (!$resolvedDirectory.StartsWith($buildsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Build directory must be a child of $buildsRoot."
    }
    $relativeDirectory = $resolvedDirectory.Substring($buildsPrefix.Length)
    foreach ($component in ($relativeDirectory -split '[\\/]')) {
        if (!$component -or $component.EndsWith('.') -or $component.EndsWith(' ') -or
            $component.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
            $component -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)') {
            throw 'Build directory contains an invalid or ambiguous path component.'
        }
    }
    # A lexical prefix alone would permit a junction/symlink to redirect outside Builds.
    $directoryToCheck = $resolvedDirectory
    while ($directoryToCheck.Length -ge $buildsRoot.Length) {
        $entry = Get-Item -LiteralPath $directoryToCheck -Force -ErrorAction SilentlyContinue
        if ($null -ne $entry) {
            if (!$entry.PSIsContainer -or ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw "Build directory must contain only real directories, not files or links: $directoryToCheck"
            }
        }
        $directoryToCheck = Split-Path -Parent $directoryToCheck
    }
    return $resolvedDirectory
}
