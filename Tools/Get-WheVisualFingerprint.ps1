function Get-WheVisualFingerprint([string]$BuildDirectory) {
    # Include every serialized asset and streamed payload: a DLL hash alone cannot identify artwork.
    $root = [IO.Path]::GetFullPath((Join-Path $BuildDirectory 'WorstHotelEver_Data')).TrimEnd('\') + '\'
    $files = @(Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object {
        $_.Extension -in @('.assets','.resS','.resource') -or $_.Name -in @('globalgamemanagers','globalgamemanagers.assets','level0')
    } | Sort-Object FullName)
    if ($files.Count -lt 3 -or !($files.Name -contains 'resources.assets')) { throw 'Missing serialized visual asset payloads.' }
    $rows = @($files | ForEach-Object {
        $_.FullName.Substring($root.Length).Replace('\','/') + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    })
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($rows -join "`n")))).Replace('-','') }
    finally { $sha.Dispose() }
}
