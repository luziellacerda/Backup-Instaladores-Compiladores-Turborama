# 03-pix: TurboramaEmulationStation/tools/tests/Test-CreditManagerFailClosed.ps1

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Teste automatizado: preparação, execução e asserções com dados sintéticos.

- Antes: `76b214874973fe24017823401216896f3d7a6f40`.
- Depois: `476e06179f89ac209ff808dffb27555d740f93d2`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/tools/tests/Test-CreditManagerFailClosed.ps1).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 134, depois 134

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/tools/tests/Test-CreditManagerFailClosed.ps1#L134) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/tools/tests/Test-CreditManagerFailClosed.ps1#L134)

```text
ANTES | DEPOIS |   CÓDIGO
  134 |    134 |   player=Ana;id=wallet-player-0123456789abcdef;playedSeconds=5;remainingSeconds=120;totalMinutesPurchased=2;archived=0;tombstonedAt=0
  135 |    135 |   "@
  136 |    136 |   
      |    137 | + # Here-strings inherit the checkout's CRLF on Windows. Normalize fixtures before
      |    138 | + # regex mutations, otherwise invalid-config cases accidentally test valid input.
      |    139 | + foreach ($fixtureName in @('config', 'validCredit', 'validMirror', 'legacyCredit',
      |    140 | +     'legacyMirror', 'validWalletGraph', 'validSchema5PlayerMirror')) {
      |    141 | +     $fixture = Get-Variable -Name $fixtureName -ValueOnly
      |    142 | +     Set-Variable -Name $fixtureName -Value $fixture.Replace("`r`n", "`n")
      |    143 | + }
      |    144 | + 
  137 |    145 |   $largeSchema5GuestWallet = ($validCredit -replace '(?m)^remainingSeconds=120$', 'remainingSeconds=36000') `
  138 |    146 |       -replace 'guestRemainingSeconds=120', 'guestRemainingSeconds=36000'
  139 |    147 |   
```

Conferência: 1 trechos, 8 linhas adicionadas e 0 removidas.

