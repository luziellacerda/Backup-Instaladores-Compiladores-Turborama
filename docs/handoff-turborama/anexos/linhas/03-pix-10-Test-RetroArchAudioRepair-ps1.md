# 03-pix: TurboramaEmulationStation/tools/tests/Test-RetroArchAudioRepair.ps1

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Teste automatizado: preparação, execução e asserções com dados sintéticos.

- Antes: `76b214874973fe24017823401216896f3d7a6f40`.
- Depois: `476e06179f89ac209ff808dffb27555d740f93d2`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/tools/tests/Test-RetroArchAudioRepair.ps1).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 0, depois 1

Arquivo novo nesta comparação; não existe na revisão anterior. [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/tools/tests/Test-RetroArchAudioRepair.ps1#L1)

```text
ANTES | DEPOIS |   CÓDIGO
      |      1 | + $ErrorActionPreference = 'Stop'
      |      2 | + $root = Join-Path ([IO.Path]::GetTempPath()) ('audio-repair-test-' + [Guid]::NewGuid().ToString('N'))
      |      3 | + New-Item -ItemType Directory -Path $root | Out-Null
      |      4 | + $script = Join-Path $PSScriptRoot '..\Repair-RetroArchAudio.ps1'
      |      5 | + $path = Join-Path $root 'retroarch.cfg'
      |      6 | + $inputText = "# caff$([char]233)`r`naudio_driver = `"wasapi`"`r`naudio_wasapi_exclusive_mode = `"true`"`r`naudio_mute_enable = `"true`"`r`ninput_audio_mute = `"o`"`r`naudio_volume = `"-3.0`"`r`nserver_setting = `"unchanged`"`r`n"
      |      7 | + $bytes = [Text.Encoding]::UTF8.GetPreamble() + [Text.Encoding]::UTF8.GetBytes($inputText)
      |      8 | + [IO.File]::WriteAllBytes($path, $bytes)
      |      9 | + & $script -ConfigPath $path | Out-Null
      |     10 | + $expected = $inputText.Replace('audio_wasapi_exclusive_mode = "true"', 'audio_wasapi_exclusive_mode = "false"').Replace('audio_mute_enable = "true"', 'audio_mute_enable = "false"').Replace('input_audio_mute = "o"', 'input_audio_mute = "nul"')
      |     11 | + $expectedBytes = [Text.Encoding]::UTF8.GetPreamble() + [Text.Encoding]::UTF8.GetBytes($expected)
      |     12 | + if ([Convert]::ToBase64String([IO.File]::ReadAllBytes($path)) -cne [Convert]::ToBase64String($expectedBytes)) { throw 'Unexpected settings/encoding change' }
      |     13 | + $backup = @(Get-ChildItem -LiteralPath $root -Filter '*.audio-backup-*')
      |     14 | + if ($backup.Count -ne 1 -or [Convert]::ToBase64String([IO.File]::ReadAllBytes($backup[0].FullName)) -cne [Convert]::ToBase64String($bytes)) { throw 'Backup mismatch' }
      |     15 | + & $script -ConfigPath $path | Out-Null
      |     16 | + if (@(Get-ChildItem -LiteralPath $root -Filter '*.audio-backup-*').Count -ne 1) { throw 'Not idempotent' }
      |     17 | + [IO.File]::WriteAllText($path, 'input_audio_mute = "f9"')
      |     18 | + & $script -ConfigPath $path | Out-Null
      |     19 | + if ([IO.File]::ReadAllText($path) -cne 'input_audio_mute = "f9"') { throw 'Custom key changed' }
      |     20 | + Write-Host 'RETROARCH_AUDIO_REPAIR_TEST=OK (exact bytes, backup, idempotence, custom hotkey)'
```

Conferência: 1 trechos, 20 linhas adicionadas e 0 removidas.
