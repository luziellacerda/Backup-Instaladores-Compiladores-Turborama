param(
    [Parameter(Mandatory=$true)][string]$PackageDirectory,
    [ValidateSet(0,21)][int]$ExpectedProbeExit = 21
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
$exe = Join-Path $package 'emulationstation.exe'
$helper = Join-Path $package 'TurboRama.Suite.Access.exe'
if (!(Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'EXE ausente.' }
if (Test-Path -LiteralPath $helper) {
    throw 'Use staging limpo, sem helper adjacente. Nenhum arquivo existente sera removido.'
}

function Assert-DiagnosticExit([string]$Path, [string]$Argument, [int]$Expected) {
    $process = Start-Process -FilePath $Path -WorkingDirectory $package `
        -ArgumentList $Argument -WindowStyle Hidden -PassThru
    try {
        if (!$process.WaitForExit(30000)) {
            $process.Kill()
            $process.WaitForExit()
            throw "Teste $Argument excedeu o prazo."
        }
        if ($process.ExitCode -ne $Expected) {
            throw "$Argument : esperado $Expected, recebido $($process.ExitCode)."
        }
    }
    finally { $process.Dispose() }
}

# Parse only PE headers/resource directories; never load or execute the image.
# RVA mappings must stay in raw section data, not virtual padding.
function Get-EmbeddedPayloadOffset([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        function Read-U16([long]$At) {
            if ($At -lt 0 -or $At -gt $stream.Length - 2) { throw 'PE fora dos limites.' }
            $stream.Position = $At
            $reader.ReadUInt16()
        }
        function Read-U32([long]$At) {
            if ($At -lt 0 -or $At -gt $stream.Length - 4) { throw 'PE fora dos limites.' }
            $stream.Position = $At
            $reader.ReadUInt32()
        }
        if ((Read-U16 0) -ne 0x5A4D) { throw 'Assinatura MZ invalida.' }
        $pe = [long](Read-U32 0x3C)
        if ((Read-U32 $pe) -ne 0x4550 -or (Read-U16 ($pe + 4)) -ne 0x8664) { throw 'PE x64 invalido.' }
        $count = Read-U16 ($pe + 6)
        $optionalSize = Read-U16 ($pe + 20)
        $optional = $pe + 24
        if ($count -lt 1 -or $count -gt 96 -or $optionalSize -lt 136 -or
            (Read-U16 $optional) -ne 0x20B -or (Read-U32 ($optional + 108)) -lt 3) {
            throw 'Cabecalho PE32+ invalido.'
        }
        $resourceRva = [long](Read-U32 ($optional + 128))
        $resourceSize = [long](Read-U32 ($optional + 132))
        $sections = @()
        for ($i = 0; $i -lt $count; $i++) {
            $section = $optional + $optionalSize + 40 * $i
            $sections += @{
                Rva = [long](Read-U32 ($section + 12))
                Size = [long](Read-U32 ($section + 16))
                Raw = [long](Read-U32 ($section + 20))
            }
        }
        function Resolve-Rva([long]$Rva, [long]$Size) {
            if ($Size -le 0) { throw 'Recurso vazio.' }
            foreach ($section in $sections) {
                $delta = $Rva - $section.Rva
                if ($delta -ge 0 -and $delta + $Size -le $section.Size) {
                    $offset = $section.Raw + $delta
                    if ($offset -ge 0 -and $offset + $Size -le $stream.Length) { return $offset }
                }
            }
            throw 'RVA fora dos dados do arquivo.'
        }
        $root = Resolve-Rva $resourceRva $resourceSize
        function Read-ResourceU32([long]$Relative) {
            if ($Relative -lt 0 -or $Relative + 4 -gt $resourceSize) { throw 'Recurso fora dos limites.' }
            Read-U32 ($root + $Relative)
        }
        function Find-ResourceEntry([long]$Directory, [int]$Id) {
            if ($Directory -lt 0 -or $Directory + 16 -gt $resourceSize) { throw 'Diretorio invalido.' }
            $named = Read-U16 ($root + $Directory + 12)
            $ids = Read-U16 ($root + $Directory + 14)
            if ($named + $ids -gt 4096) { throw 'Diretorio excessivo.' }
            for ($j = 0; $j -lt $named + $ids; $j++) {
                $entry = $Directory + 16 + 8 * $j
                $name = [long](Read-ResourceU32 $entry)
                if ($name -lt 2147483648 -and ($Id -lt 0 -or $name -eq $Id)) {
                    return [long](Read-ResourceU32 ($entry + 4))
                }
            }
            throw "Recurso $Id nao localizado."
        }
        $type = Find-ResourceEntry 0 10 # RT_RCDATA
        if ($type -lt 2147483648) { throw 'Diretorio RCDATA ausente.' }
        $name = Find-ResourceEntry ($type - 2147483648) 31001
        if ($name -lt 2147483648) { throw 'Diretorio Suite ausente.' }
        $data = Find-ResourceEntry ($name - 2147483648) -1 # first numeric language
        if ($data -ge 2147483648) { throw 'Entrada de dados invalida.' }
        $rva = [long](Read-ResourceU32 $data)
        $size = [long](Read-ResourceU32 ($data + 4))
        $offset = Resolve-Rva $rva $size
        if ($size -lt 64 -or (Read-U16 $offset) -ne 0x5A4D) { throw 'Payload nao e um EXE.' }
        return $offset + $size - 1
    }
    finally { $reader.Dispose(); $stream.Dispose() }
}

$originalHash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
Assert-DiagnosticExit $exe '--suite-access-integrity-self-test' 0
Assert-DiagnosticExit $exe '--suite-access-probe-identity' $ExpectedProbeExit

# A bogus adjacent module must not be selected or executed. Only remove the
# marker this test successfully created; never replace a user's helper.
$markerCreated = $false
try {
    $marker = [IO.File]::Open($helper, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $markerCreated = $true
    try { $marker.Write([byte[]]@(0,1,2,3), 0, 4) }
    finally { $marker.Dispose() }
    Assert-DiagnosticExit $exe '--suite-access-integrity-self-test' 0
    Assert-DiagnosticExit $exe '--suite-access-probe-identity' $ExpectedProbeExit
}
finally { if ($markerCreated) { Remove-Item -LiteralPath $helper -Force } }

# Corrupt a uniquely named test COPY, never the binary that will be released.
$altered = Join-Path $package ('suite-integrity-test-' + [Guid]::NewGuid().ToString('N') + '.exe')
$copyCreated = $false
try {
    [IO.File]::Copy($exe, $altered, $false)
    $copyCreated = $true
    $position = Get-EmbeddedPayloadOffset $altered
    $stream = [IO.File]::Open($altered, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $stream.Position = $position
        $originalByte = $stream.ReadByte()
        $stream.Position = $position
        $stream.WriteByte([byte]($originalByte -bxor 1))
    }
    finally { $stream.Dispose() }
    Assert-DiagnosticExit $altered '--suite-access-integrity-self-test' 44
    Assert-DiagnosticExit $altered '--suite-access-probe-identity' 44
}
finally { if ($copyCreated) { Remove-Item -LiteralPath $altered -Force } }
if ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -ne $originalHash) {
    throw 'EXE original foi alterado durante os testes.'
}
Assert-DiagnosticExit $exe '--suite-access-integrity-self-test' 0
'SUITE_EMBEDDED_PACKAGE=OK (embutido, adjacente ignorado, probe, payload adulterado rejeitado, original intacto)'
