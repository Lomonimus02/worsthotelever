# Reads selected UTF-8 game logs/build files; writes sanitized copies only into a NEW folder.
# Compatible with Windows PowerShell 5.1 and PowerShell 7. Does not launch any process.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][ValidateSet('Host', 'Client')][string]$Role,
    [string[]]$LogPath,
    [string]$BuildDirectory,
    [string]$ArchivePath,
    [ValidateRange(1, 32)][int]$MaxLogMiB = 8
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

function Protect-LogText([string]$Text) {
    # Redact secrets through the end of the line, including quoted/multi-word values.
    $Text = [regex]::Replace($Text,
        '(?im)(?:--?)?\b(?:password|passwd|pwd|passphrase|access[_-]?token|refresh[_-]?token|token|authorization|api[_-]?key|secret|cookie|ticket|(?:auth|session)[_-]?ticket|connection(?:data|string))["'']?\s*[=:]\s*[^\r\n]*',
        '[SECRET_FIELD_REDACTED]')
    $Text = [regex]::Replace($Text,
        '(?im)--?(?:password|passwd|pwd|passphrase|token|secret|api-key|auth-ticket)\s+[^\r\n]*',
        '[SECRET_ARGUMENT_REDACTED]')
    $Text = [regex]::Replace($Text, '(?i)\bBearer\s+[^\s"'']+', '[BEARER_REDACTED]')
    $Text = [regex]::Replace($Text, '\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b', '[JWT_REDACTED]')
    $Text = [regex]::Replace($Text, '(?i)(\b[a-z][a-z0-9+.-]*://)[^\s/@]+@', '$1[CREDENTIALS]@')
    $Text = [regex]::Replace($Text, '(?i)\bSTEAM_\d+:\d+:\d+\b|\[U:\d+:\d+\]', '[STEAM_ID]')
    $Text = [regex]::Replace($Text, '(?i)\b(?:steam[_ -]?id|account[_ -]?id|lobby[_ -]?id|persona(?:name)?|username|machine(?:name)?)["'']?\s*[=:]\s*[^\r\n]*', '[IDENTITY_FIELD_REDACTED]')
    # Steam64 / lobby IDs, and opaque long decimal identifiers. NGO IDs 0/1 survive.
    $Text = [regex]::Replace($Text, '(?<!\d)\d{15,20}(?!\d)', '[LONG_ID]')
    $Text = [regex]::Replace($Text, '(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b', '[EMAIL]')

    # IPv6 first, so an IPv4-mapped IPv6 address is removed as a whole.
    $Text = [regex]::Replace($Text,
        '(?<![\w])\[?(?:[0-9a-fA-F]{0,4}:){2,}[0-9a-fA-F:.]*(?:%[A-Za-z0-9_.-]+)?\]?(?::\d{1,5})?',
        [System.Text.RegularExpressions.MatchEvaluator]{ param($match)
            $candidate = $match.Value
            if ($candidate.StartsWith('[')) {
                $end = $candidate.IndexOf(']')
                if ($end -lt 0) { return $candidate }
                $candidate = $candidate.Substring(1, $end - 1)
            }
            $candidate = ($candidate -split '%', 2)[0]
            $address = $null
            if ([System.Net.IPAddress]::TryParse($candidate, [ref]$address) -and
                $address.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetworkV6) { return '[IPV6]' }
            # Sentence punctuation / a log separator may have been consumed by the candidate.
            $candidate = $candidate.TrimEnd([char[]]'.:')
            if ([System.Net.IPAddress]::TryParse($candidate, [ref]$address) -and
                $address.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetworkV6) { return '[IPV6]' }
            return $match.Value
        })
    $octet = '(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)'
    $Text = [regex]::Replace($Text, ('(?<![\w.])' + $octet + '(?:\.' + $octet + '){3}(?![\w.])(?::\d{1,5})?'), '[IPV4]')

    # Absolute paths can contain account/profile names. Keep a stack-trace line number.
    $Text = [regex]::Replace($Text, '(?i)(?:\b[A-Z]:[\\/]|\\\\)[^\r\n"<>|]*',
        [System.Text.RegularExpressions.MatchEvaluator]{ param($match)
            $lineNumber = [regex]::Match($match.Value, '(?i):line\s+\d+')
            return '[PATH]' + $lineNumber.Value
        })
    $Text = [regex]::Replace($Text, '(?i)/(?:Users|home)/[^\s"<>]+', '[USER_PATH]')
    # Unlabelled current Windows identity may occur outside a path. Do not collect it.
    foreach ($identity in @($env:USERNAME, $env:COMPUTERNAME)) {
        if (![string]::IsNullOrWhiteSpace($identity) -and $identity.Length -ge 3) {
            $Text = [regex]::Replace($Text, ('(?i)(?<![\w])' + [regex]::Escape($identity) + '(?![\w])'), '[LOCAL_IDENTITY]')
        }
    }
    return $Text
}

function Write-NewText([string]$Name, [string]$Text) {
    # Fixed output filenames only; fail if another file has appeared, never overwrite it.
    $target = Join-Path $script:destination $Name
    $stream = [System.IO.File]::Open($target, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try {
        $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($Text)
        $stream.Write($bytes, 0, $bytes.Length)
    } finally { $stream.Dispose() }
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { throw 'Specify an explicitly NEW output folder.' }
$destination = [System.IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $destination) { throw 'Output folder already exists. Choose a different NEW folder; nothing was changed.' }
$parent = Split-Path -Parent $destination
if ([string]::IsNullOrWhiteSpace($parent) -or !(Test-Path -LiteralPath $parent -PathType Container)) {
    throw 'The parent of the NEW output folder must already exist.'
}
if (!$LogPath -or $LogPath.Count -eq 0) {
    $gameLogs = Join-Path $env:USERPROFILE 'AppData/LocalLow/Almost Grand/Worst Hotel Ever'
    $LogPath = @((Join-Path $gameLogs 'Player.log'), (Join-Path $gameLogs 'Player-prev.log'))
}
if ($LogPath.Count -gt 8) { throw 'At most eight explicitly selected game logs are accepted.' }
foreach ($source in $LogPath) {
    if ([string]::IsNullOrWhiteSpace($source) -or [System.IO.Path]::GetExtension($source) -notin @('.log', '.txt')) {
        throw 'Only .log and .txt inputs are accepted. Saves, dumps and account files are not collected.'
    }
}
if ($BuildDirectory -and !(Test-Path -LiteralPath $BuildDirectory -PathType Container)) { throw 'BuildDirectory must be an existing game folder.' }
if ($ArchivePath -and ([System.IO.Path]::GetExtension($ArchivePath) -ne '.zip' -or !(Test-Path -LiteralPath $ArchivePath -PathType Leaf))) {
    throw 'ArchivePath must be an existing candidate ZIP.'
}

# No -Force and no recursive creation. Existing output directories are refused above.
New-Item -ItemType Directory -Path $destination -ErrorAction Stop | Out-Null
$logs = @()
$maxBytes = [long]$MaxLogMiB * 1024 * 1024
$index = 0
foreach ($source in $LogPath) {
    $index++
    $entry = [ordered]@{ sourceIndex = $index; output = ('log-{0:D2}.sanitized.txt' -f $index); status = 'missing' }
    if (Test-Path -LiteralPath $source -PathType Leaf) {
        $stream = $null
        try {
            $sourceInfo = Get-Item -LiteralPath $source
            $stream = [System.IO.File]::Open($sourceInfo.FullName, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read,
                ([System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete))
            $originalBytes = $stream.Length
            $offset = [Math]::Max(0L, $originalBytes - $maxBytes)
            $null = $stream.Seek($offset, [System.IO.SeekOrigin]::Begin)
            $buffer = New-Object byte[] ([int]($originalBytes - $offset))
            $read = 0
            while ($read -lt $buffer.Length) {
                $count = $stream.Read($buffer, $read, $buffer.Length - $read)
                if ($count -eq 0) { break }
                $read += $count
            }
            $text = [System.Text.Encoding]::UTF8.GetString($buffer, 0, $read)
            if ($offset -gt 0) {
                # Discard the incomplete first line: it may be a fragment of a secret.
                $newline = $text.IndexOf("`n")
                $text = if ($newline -ge 0) { $text.Substring($newline + 1) } else { '' }
            }
            Write-NewText $entry.output (Protect-LogText $text)
            $entry.status = 'sanitized'
            $entry.originalBytes = $originalBytes
            $entry.readBytes = $read
            $entry.tailOnly = ($offset -gt 0)
            $entry.lastWriteUtc = $sourceInfo.LastWriteTimeUtc.ToString('o')
        } catch {
            $entry.status = 'read_or_write_failed'
            # Deliberately avoid exception text, which can itself contain a private path.
            $entry.errorType = $_.Exception.GetType().Name
        } finally { if ($null -ne $stream) { $stream.Dispose() } }
    }
    $logs += [pscustomobject]$entry
}

$artifacts = @()
if ($BuildDirectory) {
    # Exact allowlist, no traversal, no save files or binary contents copied.
    foreach ($relative in @('WorstHotelEver.exe', 'WorstHotelEver_Data/Managed/Assembly-CSharp.dll',
        'WorstHotelEver_Data/Managed/Facepunch.Steamworks.Win64.dll', 'WorstHotelEver_Data/Plugins/x86_64/steam_api64.dll')) {
        $file = Join-Path $BuildDirectory $relative
        if (Test-Path -LiteralPath $file -PathType Leaf) {
            try {
                $artifacts += [pscustomobject]@{ file = $relative; sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash; status = 'hashed' }
            } catch { $artifacts += [pscustomobject]@{ file = $relative; status = 'hash_failed' } }
        } else { $artifacts += [pscustomobject]@{ file = $relative; status = 'missing' } }
    }
}
if ($ArchivePath) {
    try { $artifacts += [pscustomobject]@{ file = 'candidate.zip'; sha256 = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash; status = 'hashed' } }
    catch { $artifacts += [pscustomobject]@{ file = 'candidate.zip'; status = 'hash_failed' } }
}
$manifest = [ordered]@{
    schema = 1; role = $Role; collectedUtc = [DateTime]::UtcNow.ToString('o')
    windowsVersion = [Environment]::OSVersion.Version.ToString()
    powershellVersion = $PSVersionTable.PSVersion.ToString()
    maxBytesPerLog = $maxBytes; logs = @($logs); artifacts = @($artifacts)
    caution = 'Redaction is best effort. Review before sharing. No account data, saves, network probes, Steam initialization, or online verification collected.'
}
Write-NewText 'manifest.json' ($manifest | ConvertTo-Json -Depth 8)
Write-NewText 'READ_BEFORE_SHARING.txt' @'
Only sanitized UTF-8 log tails and optional file hashes are included.
No original log, save, dump, credentials store, machine inventory, or IP lookup is copied.
Redaction masks labelled secrets, IDs, emails, IP addresses and absolute paths.
It can miss unlabelled/multiline secrets or human names; manually inspect ALL files.
It can also remove useful text. Keep originals locally; provide a narrowly reviewed excerpt if needed.
Nothing is uploaded. Only share this NEW folder after review.
For a bug report include role, candidate ZIP/DLL hash, local time + timezone, steps,
expected/actual result and screenshots reviewed for personal data.
This folder does not prove WAN/relay connectivity, two distinct accounts, or a passed playtest.
'@
$failed = @($logs | Where-Object { $_.status -ne 'sanitized' }).Count
Write-Output "Collected sanitized diagnostics for $Role. Logs unavailable/failed: $failed."
Write-Output 'Review the NEW folder before sharing; nothing was uploaded. Original logs and saves were not modified.'
