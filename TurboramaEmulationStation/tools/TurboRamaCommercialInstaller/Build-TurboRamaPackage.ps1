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
$outputDirectory = Split-Path -Parent $Output
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
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
if ([IO.File]::Exists($Output)) {
    [IO.File]::Delete($Output)
}
[IO.File]::Move($temporary, $Output)
