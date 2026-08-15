param(
    [Parameter(Mandatory=$true)][string]$Bootstrapper,
    [Parameter(Mandatory=$true)][string]$Installer,
    [Parameter(Mandatory=$true)][string]$SevenZip,
    [Parameter(Mandatory=$true)][string]$Payload,
    [Parameter(Mandatory=$true)][string]$Output,
    [Parameter(Mandatory=$true)][string]$DiretorioTemporarioBuild
)
$ErrorActionPreference = 'Stop'
$outputFull = [IO.Path]::GetFullPath($Output)
$outputParent = [IO.Path]::GetFullPath((Split-Path -Parent $outputFull)).TrimEnd('\')
$buildBoundary = [IO.Path]::GetFullPath($DiretorioTemporarioBuild).TrimEnd('\')
$buildDriveRoot = [IO.Path]::GetPathRoot($buildBoundary).TrimEnd('\')
if ([string]::IsNullOrWhiteSpace($buildBoundary) -or
    [string]::Equals($buildBoundary, $buildDriveRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'DiretorioTemporarioBuild nao pode ser vazio nem a raiz de uma unidade.'
}
$expectedCandidateParent = [IO.Path]::GetFullPath((Join-Path $buildBoundary 'TurboRama-v25-build\pack\PIX-COMERCIAL\GERADO-v25')).TrimEnd('\')
if (-not [string]::Equals($outputParent, $expectedCandidateParent, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Saida recusada: o empacotador so monta o candidato temporario esperado ($outputFull)."
}
$files = @($Bootstrapper, $Installer, $SevenZip, $Payload)
foreach ($file in $files) { if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Arquivo ausente: $file" } }
$expectedSevenZipHash = '223B873C50380FE9A39F1A22B6ABF8D46DB506E1C08D08312902F6F3CD1F7AC3'
$actualSevenZipHash = (Get-FileHash -LiteralPath $SevenZip -Algorithm SHA256).Hash
if ($actualSevenZipHash -ne $expectedSevenZipHash) {
    throw "7za.exe recusado: SHA-256 divergente (esperado $expectedSevenZipHash; atual $actualSevenZipHash)."
}
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
