#Requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string[]]$ConfigPath)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (Get-Process -Name retroarch -ErrorAction SilentlyContinue) {
    throw 'Feche o RetroArch antes de corrigir: ele pode sobrescrever o arquivo ao sair.'
}
# A one-to-one byte encoding preserves BOM, comments and all unrelated settings.
$encoding = [Text.Encoding]::GetEncoding(28591)
foreach ($candidate in $ConfigPath) {
    $item = Get-Item -LiteralPath $candidate -Force
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "A configuracao precisa ser um arquivo regular: $candidate"
    }
    $path = $item.FullName
    $original = [IO.File]::ReadAllBytes($path)
    $text = $encoding.GetString($original)
    $updated = $text
    # Shared WASAPI avoids exclusive ownership/format failures. Preserve the
    # selected driver/device, volume, timing and all emulator/server settings.
    foreach ($key in @('audio_wasapi_exclusive_mode', 'audio_mute_enable')) {
        $pattern = '(?m)(^[ \t]*' + $key + '[ \t]*=[ \t]*)"(?:true|false)"'
        $updated = [regex]::Replace($updated, $pattern, '${1}"false"')
    }
    # Only the accidental letter-O mute binding is disabled; F9/custom keys stay.
    $updated = [regex]::Replace($updated,
        '(?mi)(^[ \t]*input_audio_mute[ \t]*=[ \t]*)"o"', '${1}"nul"')
    if ($updated -ceq $text) {
        Write-Output "SEM_ALTERACAO=$path"
        continue
    }
    $suffix = [Guid]::NewGuid().ToString('N')
    $backup = $path + '.audio-backup-' + $suffix
    $temporary = $path + '.audio-tmp-' + $suffix
    try {
        [IO.File]::WriteAllBytes($temporary, $encoding.GetBytes($updated))
        # Atomic replacement produces a byte-exact backup before changing config.
        [IO.File]::Replace($temporary, $path, $backup)
        Write-Output "CORRIGIDO=$path"
        Write-Output "BACKUP=$backup"
    }
    finally {
        if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
    }
}
