# Parse only PE headers/resource directories; never load or execute the image.
# RVA mappings must stay in raw section data, not virtual padding.
function Get-SuiteEmbeddedPayload([string]$Path) {
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
        return [pscustomobject]@{ Offset = $offset; Size = $size }
    }
    finally { $reader.Dispose(); $stream.Dispose() }
}

# Only the exact resource approved at build time can receive scoped treatment
# in marker tests. A malformed resource or hash mismatch is a hard failure.
function Assert-SuiteEmbeddedPayloadHash([string]$Path, [string]$ExpectedHash) {
    if ($ExpectedHash -notmatch '^[0-9a-fA-F]{64}$') { throw 'SHA-256 Suite invalido.' }
    $payload = Get-SuiteEmbeddedPayload $Path
    $stream = [IO.File]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $stream.Position = $payload.Offset
        $remaining = [long]$payload.Size
        $buffer = New-Object byte[] (1024 * 1024)
        while ($remaining -gt 0) {
            $count = [int][Math]::Min($buffer.Length, $remaining)
            $read = $stream.Read($buffer, 0, $count)
            if ($read -le 0) { throw 'Payload Suite truncado.' }
            [void]$sha.TransformBlock($buffer, 0, $read, $buffer, 0)
            $remaining -= $read
        }
        [void]$sha.TransformFinalBlock([byte[]]@(), 0, 0)
        $actual = [BitConverter]::ToString($sha.Hash).Replace('-', '')
        if ($actual -ne $ExpectedHash) { throw 'Payload Suite nao corresponde ao hash aprovado.' }
    }
    finally { $sha.Dispose(); $stream.Dispose() }
    return $payload
}
