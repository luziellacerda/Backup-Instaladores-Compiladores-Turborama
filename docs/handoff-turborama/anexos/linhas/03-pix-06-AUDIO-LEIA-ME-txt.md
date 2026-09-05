# 03-pix: TurboramaEmulationStation/tools/AUDIO-LEIA-ME.txt

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Instruções operacionais do reparo de áudio; diferencia sincronização VLC de configuração do emulador.

- Antes: `76b214874973fe24017823401216896f3d7a6f40`.
- Depois: `476e06179f89ac209ff808dffb27555d740f93d2`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/tools/AUDIO-LEIA-ME.txt).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 0, depois 1

Arquivo novo nesta comparação; não existe na revisão anterior. [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/tools/AUDIO-LEIA-ME.txt#L1)

```text
ANTES | DEPOIS |   CÓDIGO
      |      1 | + Audio do emulador (RetroArch)
      |      2 | + 
      |      3 | + O frontend aguarda por ate 3 segundos a liberacao dos videos VLC antes de
      |      4 | + iniciar o jogo. A espera acontece antes de iniciar a sessao de credito.
      |      5 | + Os caches e limites de memoria continuam ativos durante a navegacao.
      |      6 | + 
      |      7 | + O retroarch.cfg pertence a instalacao dos emuladores, nao ao frontend.
      |      8 | + Feche o RetroArch antes de corrigir a configuracao (ele salva ao sair).
      |      9 | + No PowerShell, execute com o caminho exato da sua instalacao:
      |     10 | + 
      |     11 | +   .\Repair-RetroArchAudio.ps1 -ConfigPath 'D:\sua-instalacao\emulators\retroarch\retroarch.cfg'
      |     12 | + 
      |     13 | + Se houver um template do instalador, aplique tambem nele para novas instalacoes.
      |     14 | + A ferramenta desativa WASAPI exclusivo, desfaz audio mudo persistido e remove
      |     15 | + somente o atalho de mudo na letra O. Outros atalhos, driver, dispositivo,
      |     16 | + volume e todas as configuracoes restantes sao preservados byte por byte.
      |     17 | + Cada arquivo alterado recebe uma copia .audio-backup-<identificador> para voltar.
      |     18 | + Nao execute com o emulador aberto. Overrides por core/jogo podem ter suas
      |     19 | + proprias configuracoes: use o caminho exato do override se ele repetir o problema.
      |     20 | + 
      |     21 | + Teste o mesmo jogo antes/depois. O log do frontend deve registrar AudioHandoff.
      |     22 | + Se o jogo continuar mudo, confira o log de inicializacao do RetroArch e a saida
      |     23 | + de audio escolhida pelo Windows. Esta correcao nao altera o servidor PIX.
```

Conferência: 1 trechos, 23 linhas adicionadas e 0 removidas.
