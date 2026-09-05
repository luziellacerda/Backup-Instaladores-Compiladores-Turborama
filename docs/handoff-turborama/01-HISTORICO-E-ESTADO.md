# 01 — Histórico e estado confirmado

[Início](README.md) · [Como ler](00-COMO-LER.md)

Consulta somente leitura em 05/09/2026 às 12:21 UTC / 09:21 Fortaleza (UTC−03:00). Nenhum EXE/ZIP grande baixado; nenhuma fonte, servidor, branch ou release alterada nesta revisão.

## Worktrees e ancestralidade

| Perfil | Caminho absoluto | HEAD local e remoto |
|---|---|---|
| PIX | `C:\Users\Admin\Documents\Codex\2026-09-04\pr\work\turborama-pix-audio` | `476e06179f89ac209ff808dffb27555d740f93d2` |
| Cliente | `C:\Users\Admin\Documents\Codex\2026-09-04\pr\work\turborama-es-0e02780` | `947dad45f5e9cd556cce6f15045a5dd6119bdf95` |

Ambos sem alterações rastreadas. O worktree cliente contém builds, perfis e logs não rastreados: preservar. Seu nome antigo não identifica sua versão atual. `git merge-base 476e061 947dad4` retorna `76b214874973fe24017823401216896f3d7a6f40`: as duas versões herdaram `0e02780` e `5414039` antes de se separarem.

## Histórico confirmado pelo Git

Datas abaixo são as datas dos commits, não de instalação. Caminhos da tabela são relativos a `TurboramaEmulationStation`, salvo `.github`.

| Commit | Fortaleza / UTC | Caminhos e efeito |
|---|---|---|
| [0e02780](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/commit/0e02780b761cb488c591416d2986130efcc166dd), comum | 20/08/2026 18:37:59 / 21:37:59 | Nove arquivos: `es-app/src/FileData.{cpp,h}`, `views/SystemView.{cpp,h}`, `es-core/src/Settings.cpp`, `components/CarouselComponent.{cpp,h}`, `components/VideoVlcComponent.{cpp,h}`. Otimizações de caches, players, buffers, decode e carrosséis. |
| [5414039](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/commit/5414039fc9cdfea9d45816a03fd86f5f8825c1b9), comum | 04/09/2026 16:16:18 / 19:16:18 | `main.cpp`, `EmbeddedTheme.{cpp,h}`, `ResourceManager.{cpp,h}`, teste do tema. Corrige preparação do tema embutido: janela/progresso, tratamento de falha, espaço livre, cache, limpeza protegida e bloqueio entre instâncias. |
| [76b2148](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/commit/76b214874973fe24017823401216896f3d7a6f40), base comum final | 04/09/2026 16:16:37 / 19:16:37 | Somente `.github/workflows/compilar-emulationstation-windows.yml`: compilação/publicação PIX no GitHub. |
| [bc51e72](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/commit/bc51e72d79659c1e966457f72e6c0edd0a3a90d4), cliente | 04/09/2026 18:44:01 / 21:44:01 | CMake, scripts, `FileData`, `GuiMenu`, `main`, `SystemView`, `ViewController`, teste. Introduz perfil sem serviços e exclusão dos módulos comerciais da compilação. |
| [12b8e75](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/commit/12b8e75975831c260b53ad6b572b15be30ebed87), cliente | 04/09/2026 18:47:58 / 21:47:58 | Dois workflows: separa compilação cliente da PIX. |
| [947dad4](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/commit/947dad45f5e9cd556cce6f15045a5dd6119bdf95), HEAD cliente | 04/09/2026 20:07:53 / 23:07:53 | CMake, `MainMenuAuth.{cpp,h}`, `GuiMenu`, `main`, scripts e teste. Preserva configurações e ações não comerciais, proteção administrativa independente START/F11 e hardening geral. |
| [7de017c](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/commit/7de017cebabf87c7172ff874f044fa117233d829), PIX | 05/09/2026 06:55:49 / 09:55:49 | `FileData.cpp`, `VideoVlcComponent.{cpp,h}`, CMake, workflow, reparador e testes. Espera até 3 segundos pelo release VLC antes de lançar o emulador/iniciar sessão; reparador RetroArch com backup. |
| [2741543](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/commit/2741543a980e928abd25b240ce9d1d0a70be5b39), PIX | 05/09/2026 06:57:44 / 09:57:44 | Só workflow: autoteste deve rejeitar agente ausente do pacote frontend com código 32; não enfraquece confiança. |
| [476e061](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/commit/476e06179f89ac209ff808dffb27555d740f93d2), HEAD PIX | 05/09/2026 07:10:15 / 10:10:15 | Só workflow: publica EXE avulso e SHA-256. Código do programa igual a `2741543`. |

## Branch, tag, release e run

| Perfil | Branch | Tag real / commit | Run atual |
|---|---|---|---|
| PIX | `PIX-SERVIDOR-CONTADOR-20260904-1605` | `build-PIX-SERVIDOR-CONTADOR-20260904-1605` → `476e06179f89ac209ff808dffb27555d740f93d2` | [33959943942](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/actions/runs/33959943942), success |
| Cliente | `CLIENTE-SEM-SERVICOS-20260904-1818` | `build-CLIENTE-SEM-SERVICOS-20260904-1818` → `947dad45f5e9cd556cce6f15045a5dd6119bdf95` | [33928366809](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/actions/runs/33928366809), success |

Sem divergência entre HEAD local/remoto/tag. As tags foram consultadas diretamente: [ref PIX](https://api.github.com/repos/luziellacerda/Backup-Instaladores-Compiladores-Turborama/git/ref/tags/build-PIX-SERVIDOR-CONTADOR-20260904-1605) e [ref cliente](https://api.github.com/repos/luziellacerda/Backup-Instaladores-Compiladores-Turborama/git/ref/tags/build-CLIENTE-SEM-SERVICOS-20260904-1818), ambas objetos `commit`.

Armadilha: o campo `target_commitish` da release ainda mostra `76b2148` (PIX) e `12b8e75` (cliente), herdados da criação. Ele não prova o destino atual de uma tag existente. As refs reais, notas atuais e `head_sha` dos runs comprovam `476e061` e `947dad4`. Portanto, não declarar release cliente antiga só pelo `target_commitish`.

[Release PIX](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/releases/tag/build-PIX-SERVIDOR-CONTADOR-20260904-1605) e [release cliente](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/releases/tag/build-CLIENTE-SEM-SERVICOS-20260904-1818): públicas, não rascunho, não prerelease. As tags são fixas mas atualizáveis: guardar commit e hash do artefato para reproduzir uma entrega histórica.

| Evento | UTC | Fortaleza |
|---|---|---|
| Publicação inicial da release PIX | 04/09/2026 19:23:58 | 04/09/2026 16:23:58 |
| Atualização atual da release PIX | 05/09/2026 10:17:28 | 05/09/2026 07:17:28 |
| Job PIX: início → fim | 05/09/2026 10:10:42 → 10:17:33 | 05/09/2026 07:10:42 → 07:17:33 |
| Publicação inicial da release cliente | 04/09/2026 21:54:46 | 04/09/2026 18:54:46 |
| Atualização atual da release cliente | 04/09/2026 23:15:31 | 04/09/2026 20:15:31 |
| Job cliente: início → fim | 04/09/2026 23:08:50 → 23:15:37 | 04/09/2026 20:08:50 → 20:15:37 |

Assets atuais criados às 10:16:46 UTC de 05/09 (PIX) e 23:15:08 UTC de 04/09 (cliente). `published_at` antigo da release não significa asset antigo.

## Artefatos remotos e locais

Hashes remotos: campo `digest` dos assets, conferido com notas da release; sem baixar os binários grandes para nova hashificação local.

| Artefato remoto | Bytes | SHA-256 |
|---|---:|---|
| PIX `emulationstation.exe` | 789809152 | `c7732005e26e8ce88e7a6c0493491101c40e9badbf8cfe96ec911fa970f6ae4e` |
| PIX `TurboramaEmulationStation-Windows-x64.zip` | 882065264 | `d1e54db3ea4547279dcf47aa140ca8cc89b2021432b3dada55402064d0e414cb` |
| Cliente `TurboramaEmulationStation-Cliente-Sem-Servicos-Windows-x64.zip` | 881422858 | `9262b2d0c0e34cc544b34abd4ee87ff8108bd310956d45c0f6b53f1ea32a51aa` |

PIX tem EXE avulso e ZIP, cada qual com `.sha256`. Cliente tem ZIP e `.sha256`, sem EXE avulso na release consultada. O ZIP PIX antigo de `2741543` tinha hash `1681a1f05d52c9733e93e4357a53e60453f14b79c6b059d21d4fe6cfac5a8498`; não usar esse hash para o pacote atual de `476e061`.

O EXE avulso não é instalador/autônomo: preservar DLLs, plugins VLC, recursos, runtimes, agente PIX e configurações da instalação existente. Os dois builds frontend são sem assinatura digital e não substituem a implantação do servidor.

| Arquivo local (caminho absoluto) | Hash calculado nesta revisão | Identificação |
|---|---|---|
| `C:\Users\Admin\Documents\Codex\2026-09-04\pr\outputs\TurboramaEmulationStation-0e02780-x64-Release\emulationstation.exe` | `944DFFF0136AB9F9DEB2160E1E622D6230679C4865870531C3520BAEB7BE0BA8` | 789757952 bytes; entrega histórica `0e02780` com correção local de inicialização segundo LEIA-ME. Não é PIX atual. |
| `C:\Users\Admin\Documents\Codex\2026-09-04\pr\outputs\TurboramaEmulationStation-Cliente-Sem-Servicos-Teste-Local\emulationstation.exe` | `47E197652B0E750F35163C49575E9AC0FFD51D90B38F6D15E7D9CCBEDE636C17` | 789156352 bytes; LEIA-ME identifica compilação local funcional `947dad45`. Não demonstrada identidade byte a byte com EXE dentro do ZIP remoto. |
| `C:\Users\Admin\Documents\Codex\2026-09-04\pr\work\turborama-es-0e02780\TurboramaEmulationStation\bin\x64\Release\emulationstation.exe` | Mesmo `47E19765…636C17` | Também cliente sem serviços; nome antigo da pasta não o torna PIX. |

Busca incluindo ignorados/ocultos em `work` e `outputs` não encontrou EXE PIX `2741543`/`476e061` local. Essa conclusão não cobre todos os discos.

## Testes: o que foi efetivamente demonstrado

Workflows ativos: PIX `.github/workflows/compilar-emulationstation-windows.yml`, nome `PIX-SERVIDOR-CONTADOR - EmulationStation Windows x64`; cliente `.github/workflows/compilar-cliente-sem-servicos-windows.yml`, nome `CLIENTE SEM SERVICOS - EmulationStation Windows x64`. O worktree cliente também contém o workflow PIX herdado, mas filtros/condição de branch separam execução. Todos os steps dos runs atuais retornaram success: [jobs PIX](https://api.github.com/repos/luziellacerda/Backup-Instaladores-Compiladores-Turborama/actions/runs/33959943942/jobs), [jobs cliente](https://api.github.com/repos/luziellacerda/Backup-Instaladores-Compiladores-Turborama/actions/runs/33928366809/jobs).

- PIX: empacotador do tema; `Test-AudioHandoff.ps1`, `Test-RetroArchAudioRepair.ps1`, `Test-LaunchCreditCompatibility.ps1`, `Test-CreditManagerFailClosed.ps1`; autotestes `--help`, `--protected-decorations-self-test`, `--credit-warning-overlay-self-test`, `--pix-agent-manager-self-test`, `--pix-agent-trust-self-test`. Neste último, 32 é rejeição esperada do agente ausente do pacote frontend, não prova de operação de um servidor instalado.
- Cliente: `Test-NoCommercialServicesBuild.ps1` confere serviços OFF, hardening ON, ausência de fontes comerciais no projeto gerado, fontes/assinaturas das otimizações, flags Release, autoteste do perfil, autenticação independente e rejeição de oito comandos comerciais com código34. Busca ainda marcadores proibidos/obrigatórios no PE. Pacote executa `--help`, `--protected-decorations-self-test`, `--no-commercial-services-self-test`; empacotador de tema também passou.
- A presença de fontes/marcadores e esses autotestes comprovam seus critérios, não benchmark completo nem ausência universal de bugs. Nesta revisão os builds não foram repetidos.
- Run PIX `33959290419` (`7de017c`) foi cancelado; sucessores `33959370062` (`2741543`) e `33959943942` (`476e061`) passaram. Não confundir cancelamento por substituição com falha de compilação.

## Preservado, diferente e não verificado

Diff PIX `76b2148..476e061`: `CreditManager.cpp`, `PixAgentManager.cpp`, `PixBridge.cpp`, menus, `main.cpp`, `Settings.cpp`, `CarouselComponent.cpp`, `SystemView.cpp` sem mudanças. A alteração do lançamento limita-se à espera VLC; servidor, protocolo e regras de saldo não foram editados. Preservar arquivos não prova configuração correta de servidor remoto.

Cliente `947dad4`: `Settings.cpp`, `CarouselComponent.cpp` e `VideoVlcComponent.cpp` idênticos a `0e02780`; a remoção comercial não reverteu essas implementações. Fontes comerciais continuam no repositório, mas são excluídas da compilação cliente: não confundir exclusão do binário com apagamento histórico.

**Diferença importante de áudio:** cliente `947dad4` não contém `waitForAudioRelease`, `[AudioHandoff]` nem `Repair-RetroArchAudio.ps1`, introduzidos depois no PIX `7de017c`. Não afirmar que a correção C++ e o reparador estão nas duas releases. Ajustar configuração RetroArch local não cria um commit nas duas branches.

O registro `C:\Users\Admin\Documents\Codex\2026-09-04\pr\outputs\REVISAO-PIX-AUDIO.md` documenta testes anteriores e reparos pontuais RetroArch com backup. Relata ausência de `retroarch.exe` na instalação examinada. Som em jogo real não foi confirmado nesta revisão; não houve pagamento real, teste bancário ponta a ponta nem nova varredura dos discos externos ao workspace.

O teste histórico `Test-PixAgentDaemonIdentity.ps1` foi registrado como incompatível com a base por procurar `duplicateLoggedKioskToken`, inexistente nela. Não foi gate, não passou e o daemon não foi alterado para satisfazê-lo.

Não usar logs antigos como prova de HEAD atual: `no-services-smoke-log.txt` contém entradas04/09 às18:36 e avisos de tema, anteriores a `947dad4` das20:07. `memory-fix-build.stdout.txt` registra geração do EXE e warning LNK4098 de mistura de runtimes; compilação concluída não equivale a ausência de warnings. A prova dos HEADs publicados é o run associado, não um nome de log parecido.

Para repetir sem baixar o programa: conferir HEAD/status/merge-base; consultar [branches](https://api.github.com/repos/luziellacerda/Backup-Instaladores-Compiladores-Turborama/branches?per_page=100), refs de tags, notas/digests dos assets e head_sha/conclusion/steps dos runs. Para um EXE já recebido, usar Get-FileHash no caminho exato. O nome `emulationstation.exe` não distingue os perfis.
