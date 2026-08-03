param(
    [Parameter(Mandatory=$true)][string]$Bootstrapper,
    [Parameter(Mandatory=$true)][string]$Installer,
    [Parameter(Mandatory=$true)][string]$SevenZip,
    [Parameter(Mandatory=$true)][string]$Payload,
    [Parameter(Mandatory=$true)][string]$Output
)
$ErrorActionPreference = 'Stop'
$files = @($Bootstrapper, $Installer, $SevenZip, $Payload)
foreach ($file in $files) { if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Arquivo ausente: $file" } }
$temporary = $Output + '.new'
$replacementBackup = $Output + '.previous'
$outputDirectory = Split-Path -Parent $Output
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$footerLength = 140L
$expectedLength = $footerLength
foreach ($file in $files) { $expectedLength += (Get-Item -LiteralPath $file).Length }
$reclaimableTemporary = if ([IO.File]::Exists($temporary)) { (Get-Item -LiteralPath $temporary).Length } else { 0L }
$driveRoot = [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($temporary))
$available = ([IO.DriveInfo]::new($driveRoot)).AvailableFreeSpace + $reclaimableTemporary
$safetyMargin = 256MB
if ($available -lt ($expectedLength + $safetyMargin)) {
    $freeMiB = [math]::Round($available / 1MB, 1)
    $requiredMiB = [math]::Round(($expectedLength + $safetyMargin) / 1MB, 1)
    throw "Espaco insuficiente para montar o instalador: livres ${freeMiB} MiB; necessarios ${requiredMiB} MiB (inclui margem de seguranca)."
}

try {
    $stream = [IO.File]::Open($temporary, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        foreach ($file in $files) {
            $input = [IO.File]::OpenRead($file)
            try { $input.CopyTo($stream) } finally { $input.Dispose() }
        }
        $writer = [IO.BinaryWriter]::new($stream, [Text.Encoding]::ASCII, $true)
        try {
            $magic = [Text.Encoding]::ASCII.GetBytes("TRPIXV14PACKAGE`0")
            $writer.Write($magic)
            $writer.Write([uint32]14)
            $writer.Write([uint64](Get-Item -LiteralPath $Installer).Length)
            $writer.Write([uint64](Get-Item -LiteralPath $SevenZip).Length)
            $writer.Write([uint64](Get-Item -LiteralPath $Payload).Length)
            foreach ($file in @($Installer, $SevenZip, $Payload)) {
                $hex = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
                $bytes = [byte[]]::new($hex.Length / 2)
                for ($index = 0; $index -lt $bytes.Length; $index++) {
                    $bytes[$index] = [Convert]::ToByte($hex.Substring($index * 2, 2), 16)
                }
                $writer.Write($bytes)
            }
            $writer.Flush()
        } finally { $writer.Dispose() }
        $stream.Flush($true)
    } finally { $stream.Dispose() }

    if ((Get-Item -LiteralPath $temporary).Length -ne $expectedLength) {
        throw "Tamanho do instalador temporario invalido: esperado $expectedLength bytes."
    }
    if ([IO.File]::Exists($Output)) {
        if ([IO.File]::Exists($replacementBackup)) { [IO.File]::Delete($replacementBackup) }
        [IO.File]::Replace($temporary, $Output, $replacementBackup, $true)
        try { if ([IO.File]::Exists($replacementBackup)) { [IO.File]::Delete($replacementBackup) } } catch { }
    }
    else {
        [IO.File]::Move($temporary, $Output)
    }
}
catch {
    $message = $_.Exception.Message
    if ([IO.File]::Exists($temporary)) {
        try { [IO.File]::Delete($temporary) } catch { }
    }
    if (-not [IO.File]::Exists($Output) -and [IO.File]::Exists($replacementBackup)) {
        try { [IO.File]::Move($replacementBackup, $Output) } catch { }
    }
    throw "Falha ao montar o instalador comercial: $message"
}
