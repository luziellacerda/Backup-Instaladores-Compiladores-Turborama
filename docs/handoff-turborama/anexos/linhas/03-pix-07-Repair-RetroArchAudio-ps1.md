# 03-pix: TurboramaEmulationStation/tools/Repair-RetroArchAudio.ps1

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Reparador explícito de configurações RetroArch com preservação de bytes, backup e operação idempotente.

- Antes: `76b214874973fe24017823401216896f3d7a6f40`.
- Depois: `476e06179f89ac209ff808dffb27555d740f93d2`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/tools/Repair-RetroArchAudio.ps1).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 0, depois 1

Arquivo novo nesta comparação; não existe na revisão anterior. [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/tools/Repair-RetroArchAudio.ps1#L1)

```text
ANTES | DEPOIS |   CÓDIGO
      |      1 | + #Requires -Version 5.1
      |      2 | + [CmdletBinding()]
      |      3 | + param([Parameter(Mandatory = $true)][string[]]$ConfigPath)
      |      4 | + 
      |      5 | + $ErrorActionPreference = 'Stop'
      |      6 | + Set-StrictMode -Version Latest
      |      7 | + if (Get-Process -Name retroarch -ErrorAction SilentlyContinue) {
      |      8 | +     throw 'Feche o RetroArch antes de corrigir: ele pode sobrescrever o arquivo ao sair.'
      |      9 | + }
      |     10 | + # A one-to-one byte encoding preserves BOM, comments and all unrelated settings.
      |     11 | + $encoding = [Text.Encoding]::GetEncoding(28591)
      |     12 | + foreach ($candidate in $ConfigPath) {
      |     13 | +     $item = Get-Item -LiteralPath $candidate -Force
      |     14 | +     if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
      |     15 | +         throw "A configuracao precisa ser um arquivo regular: $candidate"
      |     16 | +     }
      |     17 | +     $path = $item.FullName
      |     18 | +     $original = [IO.File]::ReadAllBytes($path)
      |     19 | +     $text = $encoding.GetString($original)
      |     20 | +     $updated = $text
      |     21 | +     # Shared WASAPI avoids exclusive ownership/format failures. Preserve the
      |     22 | +     # selected driver/device, volume, timing and all emulator/server settings.
      |     23 | +     foreach ($key in @('audio_wasapi_exclusive_mode', 'audio_mute_enable')) {
      |     24 | +         $pattern = '(?m)(^[ \t]*' + $key + '[ \t]*=[ \t]*)"(?:true|false)"'
      |     25 | +         $updated = [regex]::Replace($updated, $pattern, '${1}"false"')
      |     26 | +     }
      |     27 | +     # Only the accidental letter-O mute binding is disabled; F9/custom keys stay.
      |     28 | +     $updated = [regex]::Replace($updated,
      |     29 | +         '(?mi)(^[ \t]*input_audio_mute[ \t]*=[ \t]*)"o"', '${1}"nul"')
      |     30 | +     if ($updated -ceq $text) {
      |     31 | +         Write-Output "SEM_ALTERACAO=$path"
      |     32 | +         continue
      |     33 | +     }
      |     34 | +     $suffix = [Guid]::NewGuid().ToString('N')
      |     35 | +     $backup = $path + '.audio-backup-' + $suffix
      |     36 | +     $temporary = $path + '.audio-tmp-' + $suffix
      |     37 | +     try {
      |     38 | +         [IO.File]::WriteAllBytes($temporary, $encoding.GetBytes($updated))
      |     39 | +         # Atomic replacement produces a byte-exact backup before changing config.
      |     40 | +         [IO.File]::Replace($temporary, $path, $backup)
      |     41 | +         Write-Output "CORRIGIDO=$path"
      |     42 | +         Write-Output "BACKUP=$backup"
      |     43 | +     }
      |     44 | +     finally {
      |     45 | +         if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
      |     46 | +     }
      |     47 | + }
```

Conferência: 1 trechos, 47 linhas adicionadas e 0 removidas.
