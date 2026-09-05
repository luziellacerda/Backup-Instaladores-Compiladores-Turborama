# 04 — Versão PIX, servidor/contador e correção de áudio

[Início](README.md) · [Anexos linha a linha](anexos/linhas/README.md)

## Escopo deste capítulo

Este capítulo documenta somente a versão `PIX-SERVIDOR-CONTADOR-20260904-1605` e a correção de entrega do dispositivo de áudio antes de iniciar um emulador. Ela é a versão que conserva o ecossistema de crédito, contador e PIX. A versão sem serviços é tratada em outro capítulo e possui workflow e release próprios.

Não houve alteração de fonte, commit ou publicação durante a produção deste documento. As linhas indicadas como **conferido** foram conferidas no checkout local do commit completo `476e06179f89ac209ff808dffb27555d740f93d2`.

## 1. Identidade exata da versão

| Papel | Commit | O que representa |
|---|---|---|
| Base da comparação | [`76b214874973fe24017823401216896f3d7a6f40`](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/commit/76b214874973fe24017823401216896f3d7a6f40) | Workflow inicial de compilação x64 no GitHub. |
| Implementação de áudio | [`7de017cebabf87c7172ff874f044fa117233d829`](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/commit/7de017cebabf87c7172ff874f044fa117233d829) | Espera da fila VLC, reparador RetroArch, testes e hardening geral do release. |
| Código usado como referência funcional | [`2741543a980e928abd25b240ce9d1d0a70be5b39`](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/commit/2741543a980e928abd25b240ce9d1d0a70be5b39) | Ajusta somente o workflow para validar corretamente a ausência do agente no pacote do frontend. Não muda C++ nem o reparador. |
| HEAD e tag publicados | [`476e06179f89ac209ff808dffb27555d740f93d2`](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/commit/476e06179f89ac209ff808dffb27555d740f93d2) | Muda somente o workflow para publicar o EXE avulso e seu SHA-256. |

Assim, o HEAD da branch é `476e061`, mas o código funcional do programa é o mesmo presente em `2741543`; entre esses dois commits só existe mudança de CI/publicação. A implementação C++ de áudio entrou em `7de017c` e permaneceu igual nos dois commits seguintes.

## 2. O que realmente mudou e o que realmente foi preservado

Entre a base `76b2148` e o HEAD `476e061`, o diff contém dez arquivos:

- três arquivos do frontend para sincronizar a liberação do VLC;
- um arquivo CMake para habilitar proteções gerais de Release sem exigir o perfil comercial assinado;
- o workflow PIX;
- o reparador de configuração RetroArch e sua instrução de uso;
- dois testes novos (fila de áudio e reparador), execução do teste de compatibilidade de lançamento já existente e um pequeno ajuste em fixtures do teste de crédito.

Os blobs Git abaixo são idênticos na base e no HEAD:

| Arquivo preservado | Blob Git em `76b2148` e `476e061` |
|---|---|
| `CreditManager.cpp` | `c8e0f1eb9ffcb98994630f0cd3317c8ac0191f34` |
| `PixAgentManager.cpp` | `7bfdf3bbfdaa554834af9b54a56789b1d9692e18` |
| `PixBridge.cpp` | `848fdd19f59f759a9a97590b38a432a8777ba42d` |
| `main.cpp` | `7bf32a614649d595930e9fa8527c7b61265415c3` |
| `Settings.cpp` | `301712d5986e5d1ae71bf7860f1299764874d31f` |
| `CarouselComponent.cpp` | `a1cb3af7905df88c9654fd9efccbfcf302e40d86` |
| `SystemView.cpp` | `0685f4a3eaa345e575fead65254ed57f546ed4a0` |

Isso é uma comprovação mais forte que uma inspeção visual: servidor/ponte PIX, ledger, carteiras, saldo, contabilidade, supervisão do agente, configurações, carrosséis e limites/caches de vídeo não receberam nenhuma alteração entre a base e esta versão. `FileData.cpp` mudou somente no ponto de entrega do áudio; o bloqueio por saldo, a supervisão do processo do jogo e a contabilização da sessão continuam ao redor desse ponto.

Referências atuais: [CreditManager, aplicação PIX e persistência atômica — conferido, linhas 2431–2689](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/CreditManager.cpp#L2431-L2689), [PixBridge, consumo de eventos aprovados — conferido, linhas 835–942](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/PixBridge.cpp#L835-L942) e [PixAgentManager, início e supervisão — conferido, linhas 1453–1605](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/PixAgentManager.cpp#L1453-L1605).

## 3. Defeito de áudio que esta versão procura evitar

O menu podia tocar áudio normalmente e, ainda assim, o emulador iniciar mudo. Há dois níveis diferentes envolvidos:

1. `AudioManager`/SDL Mixer toca sons e música do frontend. Esse subsistema já era encerrado antes do jogo.
2. Os vídeos do menu são players libVLC separados. Silenciar um player com `libvlc_audio_set_mute` não equivale a destruir o player nem a liberar imediatamente o endpoint de áudio do Windows.

Ao parar um vídeo, o código silencia o player e o coloca numa fila de liberação. A fila existe para que `libvlc_media_player_release`, que pode aguardar threads internas do decoder, não congele cada movimento do carrossel. O encadeamento pode ser visto em [silenciar e enfileirar — conferido, linhas 1503–1527](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L1503-L1527), [entrada na fila — conferido, linhas 240–266](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L240-L266) e [parada do vídeo — conferido, linhas 1893–1927](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L1893-L1927).

Antes da correção, o frontend escondia a janela e iniciava o jogo sem esperar essa fila terminar. Isso criava uma janela de disputa: um player VLC já invisível e mudo ainda podia estar liberando o dispositivo enquanto o RetroArch tentava abrir o mesmo endpoint, especialmente quando configurado para WASAPI exclusivo.

A correção não troca o mecanismo de vídeo, não destrói os caches e não torna a navegação síncrona. Ela apenas acrescenta uma barreira limitada no momento específico de iniciar o jogo.

## 4. `FileData::launchGame`: antes e depois

### Antes, na base `76b2148`

A sequência era:

1. validar saldo e montar o comando;
2. encerrar `AudioManager` e `VolumeControl`;
3. desmontar/esconder a janela;
4. disparar `game-start`;
5. preparar a sessão supervisionada de crédito;
6. chamar `process.run()`.

O trecho original está em [FileData na base — linhas 1229–1239](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/FileData.cpp#L1229-L1239). SDL era liberado, mas não havia qualquer sincronização com os releases assíncronos do VLC.

### Depois, no HEAD `476e061`

A ordem atual é:

1. o bloqueio por falta de crédito continua acontecendo antes de desmontar áudio ou janela;
2. o comando continua sendo calculado antes de qualquer teardown;
3. `AudioManager::deinit()` e `VolumeControl::deinit()` continuam iguais;
4. `window->deinit(hideWindow)` faz as views esconderem/pararem seus vídeos;
5. o frontend chama uma espera da fila VLC cujo `wait_for` dura no máximo 3.000 ms;
6. somente depois dispara `game-start` e entra no fluxo supervisionado já existente.

Referência: [launch atual — conferido, linhas 1214–1246](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/FileData.cpp#L1214-L1246).

### Cada linha adicionada ao caminho de lançamento

| Linha no HEAD | Alteração | Efeito |
|---|---|---|
| [7](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/FileData.cpp#L7) | Inclui `VideoVlcComponent.h`. | Torna disponível a nova barreira estática sem criar outra instância de vídeo. |
| [1235–1236](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/FileData.cpp#L1235-L1236) | Comentário de contrato. | Registra que “mudo” não significa “dispositivo liberado” e fixa a posição anterior à sessão de crédito. |
| [1237](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/FileData.cpp#L1237) | `waitForAudioRelease(3000)`. | Bloqueia o início do jogo; o limite de três segundos vale para esta chamada, não para todo o teardown anterior. |
| [1238](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/FileData.cpp#L1238) | Log de `Warning` no timeout. | O jogo continua; o log deixa explícito que a entrega não foi confirmada. |
| [1239–1240](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/FileData.cpp#L1239-L1240) | Ramo `else` e log de `Info`. | Confirma que a fila conhecida chegou ao estado drenado antes do launch. |
| 1241 | Linha em branco. | Somente separação visual; sem efeito executável. |

O início da cobrança não foi movido para antes da espera. `beginGameSession()` permanece dentro do primeiro `pollCallback` do processo supervisionado, em [FileData — conferido, linhas 1287–1318](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/FileData.cpp#L1287-L1318). Portanto, os até três segundos de espera não consomem tempo de jogo. O `CreditGameGuard` também permanece encerrando somente uma sessão que de fato começou, em [linhas 1248–1270](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/FileData.cpp#L1248-L1270).

## 5. A fila VLC, linha por linha

A fila já possuía um worker único, limiar de backpressure em 16 releases, `mCondition`, `mInFlight` e fallback síncrono quando esse limiar era atingido. O defeito era não existir um modo confiável de esperar a conclusão — e o release síncrono de overflow não era contado em `mInFlight`.

Referência completa: [MediaPlayerReleaseQueue — conferido, linhas 43–160](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L43-L160).

### Linhas efetivamente acrescentadas ou alteradas

| Linha(s) | Mudança | Razão técnica |
|---|---|---|
| [21](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L21) | Adiciona `<chrono>`. | Fornece `std::chrono::milliseconds` para a espera temporizada. |
| [73–76](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L73-L76) | Coloca o ramo de overflow entre chaves e executa `++mInFlight`. | O release síncrono não entra em `mJobs`; sem essa contagem, outro thread poderia observar fila vazia e concluir incorretamente que tudo foi liberado. |
| [85–88](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L85-L88) | Após o release síncrono, adquire `mMutex` e faz `--mInFlight`. | Fecha exatamente a contagem aberta no ramo de overflow; o lock mantém o predicado coerente. |
| [89](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L89) | `mDrained.notify_all()`. | Acorda qualquer launch esperando o último release síncrono terminar. A notificação fica fora do lock. |
| [95–100](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L95-L100) | Adiciona `waitUntilReleased(timeoutMs)`. | Usa `wait_for` com predicado `mJobs.empty() && mInFlight == 0`; suporta fila já vazia, notificações espúrias, releases enfileirados e releases em execução. |
| [141](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L141) | O worker chama `mDrained.notify_all()` depois de decrementar `mInFlight`. | Permite que a espera termine quando o release assíncrono real retornar e o contexto for devolvido. |
| [148](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L148) | Adiciona `std::condition_variable mDrained`. | Separa o evento “há trabalho” do evento “todo o trabalho terminou”. |
| [156–161](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L156-L161) | Adiciona o wrapper público `waitForAudioRelease`. | Expõe somente a operação necessária ao launch; navegação normal não chama a espera e mantém os pools. |
| [VideoVlcComponent.h:102](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.h#L102) | Declara o método estático. | Integração mínima entre `FileData` e a fila interna. |

### Papéis de `mCondition`, `mDrained` e `mInFlight`

- `mCondition` já existia e continua acordando o worker quando um produtor coloca item em `mJobs`. Seu predicado é “parar ou existe trabalho”; ele não serve para informar drenagem.
- `mDrained` é novo e acorda o consumidor da barreira quando uma liberação termina. Seu predicado é “deque vazio e nenhum release em execução”.
- `mInFlight` já existia para o item retirado pelo worker. A correção amplia seu significado para incluir também o release síncrono de overflow. Assim, “saiu do deque” não é confundido com “terminou de liberar”.

### Caminho normal, fila cheia e timeout

No caminho normal, `enqueue` coloca o job em `mJobs` e sinaliza `mCondition`. O worker remove o primeiro job e incrementa `mInFlight` sob o mesmo lock, chama `libvlc_media_player_release`, devolve o `VideoContext`, decrementa `mInFlight` e sinaliza `mDrained`. Não existe uma janela em que o job já saiu do deque, mas ainda não está contado como em execução.

O valor `MAX_RELEASE_JOBS = 16` não mudou. Quando `mJobs.size() + mInFlight >= 16`, o novo job continua sendo liberado sincronicamente no thread chamador, como já acontecia na base. A novidade é contabilizar esse trabalho em `mInFlight`, para a barreira não passar por ele. Esse valor é um limiar de backpressure, não um máximo matemático: o próprio job de overflow faz o total observado chegar a 17, e produtores realmente concorrentes podem elevar `mInFlight` além disso enquanto cada um permanece bloqueado em seu release síncrono.

Essa distinção é importante para o tempo total: `window->deinit(hideWindow)` vem antes de `waitForAudioRelease`. Ao esconder/parar vídeos, ele pode chamar `enqueue`; se já houver 16 releases pendentes/em execução, o fallback síncrono chama `libvlc_media_player_release` no próprio thread do launch. Essa chamada não possui timeout neste código e pode demorar antes que comece o `wait_for(3000)`. Portanto, “até três segundos” descreve somente a fase da condition variable, não um teto end-to-end para a transição menu → emulador. O risco do overflow síncrono já existia na base; a correção apenas passa a contá-lo corretamente.

Depois que o fluxo chega à barreira, `wait_for` retorna `true` se o estado drenado for alcançado em até 3.000 ms. Se o prazo acabar, retorna `false`, o frontend registra `Warning` e inicia o jogo mesmo assim. Isso limita a espera na condition variable, mas não limita uma liberação síncrona que já esteja acontecendo dentro de `window->deinit`/`enqueue`.

### O que a barreira garante

- Quando retorna `true`, todos os jobs que a fila conhece naquele momento saíram do deque, concluíram `libvlc_media_player_release` e devolveram seus contextos.
- Ela cobre tanto o worker assíncrono quanto o release síncrono usado no overflow.
- O predicado é reavaliado sob lock, portanto notificações espúrias não produzem um sucesso falso.
- Ela não altera os limites de memória, o pool de buffers, a política de players concorrentes ou o comportamento assíncrono da navegação.
- Ela ocorre antes do evento `game-start`, do processo externo e do primeiro poll que abre a sessão de crédito.

### O que a barreira não garante

- Um retorno `false` não libera o endpoint; significa apenas que o prazo terminou. O jogo prossegue e ainda pode encontrar o dispositivo ocupado.
- Ela não impede, por contrato global, que algum produtor excepcional enfileire um novo player logo depois de a condição retornar. O fluxo normal reduz esse risco porque `window->deinit` já escondeu/parou as views.
- Ela espera somente players registrados nessa fila. Não consulta o mixer do Windows, não encerra áudio de outros programas e não prova que o driver do emulador abriu o endpoint.
- Apesar do nome `waitForAudioRelease`, a fila não classifica jobs por trilha de áudio: ela espera todos os players VLC enfileirados, inclusive vídeos criados com áudio desativado.
- Os 3.000 ms não são um teto total: um release síncrono acionado antes da barreira pode demorar sem timeout.
- O caminho em que há `player`, mas ainda não há `VideoContext`, chama `libvlc_media_player_release` diretamente e também não possui timeout nem entra na contagem da fila; ele é síncrono para o próprio chamador. Veja [VideoVlcComponent.cpp — conferido, linhas 232–245](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L232-L245).
- Mesmo quando retorna `true`, o código não pergunta ao Windows se o endpoint foi efetivamente desocupado; ele assume que o retorno de `libvlc_media_player_release` concluiu a entrega do recurso.
- Ela não corrige overrides por core/jogo, dispositivo de saída errado, volume do sistema, driver defeituoso ou um `retroarch.cfg` diferente daquele reparado.
- Ela não executa um shutdown global do VLC e não limpa os caches no launch. Isso é intencional para preservar o trabalho de desempenho da versão.

## 6. Reparador `Repair-RetroArchAudio.ps1`

O `retroarch.cfg` pertence ao pacote/instalação dos emuladores, não ao frontend. Por isso a correção possui duas partes: a barreira C++ reduz a disputa causada pelo VLC, enquanto o script corrige somente três estados conhecidos do RetroArch no arquivo explicitamente indicado.

Fonte completa: [Repair-RetroArchAudio.ps1 — conferido, linhas 1–47](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/tools/Repair-RetroArchAudio.ps1#L1-L47). Instrução distribuída junto do ZIP: [AUDIO-LEIA-ME.txt — conferido, linhas 1–23](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/tools/AUDIO-LEIA-ME.txt#L1-L23).

Nota de precisão para manutenção: as linhas 3–5 do `AUDIO-LEIA-ME.txt` resumem que o frontend “aguarda por até 3 segundos”. O código garante esse prazo apenas para `wait_for`; não para `window->deinit` nem para releases síncronos anteriores. O texto operacional é útil, mas não deve ser interpretado como SLA de três segundos para o launch inteiro.

### Funcionamento por bloco

- Linhas 1–6 exigem PowerShell 5.1, parâmetro obrigatório `ConfigPath`, falha imediata e modo estrito.
- Linhas 7–9 recusam a operação se qualquer processo `retroarch` estiver aberto. Isso evita que o emulador sobrescreva a correção ao encerrar.
- Linhas 10–20 leem o arquivo como bytes e os projetam por Windows-28591/ISO-8859-1. Essa codificação faz uma correspondência um-para-um para os 256 valores de byte. Como as substituições procuram somente chaves ASCII, BOM, CRLF/LF, comentários, UTF-8 multibyte e conteúdo não relacionado voltam com os mesmos bytes.
- Linhas 13–16 aceitam somente arquivo regular e rejeitam diretório ou reparse point. `-LiteralPath` evita expansão de curingas.
- Linhas 23–26 mudam valores booleanos existentes de `audio_wasapi_exclusive_mode` e `audio_mute_enable` para `"false"`. A expressão preserva indentação, chave, espaços e sinal de igual.
- Linhas 27–29 removem apenas o atalho de mudo ligado à letra `O`, transformando `input_audio_mute = "o"` em `"nul"`. `F9` e qualquer atalho personalizado diferente ficam intactos.
- Linhas 30–33 implementam a idempotência: se os bytes lógicos já representam o estado desejado ou nenhuma chave compatível existe, imprime `SEM_ALTERACAO` e não cria novo backup.
- Linhas 34–36 criam nomes irmãos com GUID para backup e temporário, sem colisão previsível.
- Linhas 37–42 gravam o temporário em bytes e usam `File.Replace` para substituir o destino, criando antes um backup byte-exato do original. Os marcadores `CORRIGIDO` e `BACKUP` tornam o resultado auditável.
- Linhas 44–46 removem apenas um temporário residual daquela tentativa; o backup nunca é apagado pelo script.

### Escopo exato das mudanças de configuração

O script:

- desativa o modo WASAPI exclusivo se a chave booleana já existir;
- remove um estado persistente de mute se a chave booleana já existir;
- remove somente o binding acidental da letra `O`;
- não troca `audio_driver` de WASAPI para XAudio/DirectSound;
- não escolhe `audio_device`, não muda volume, latência, sample rate, vídeo, controles ou configurações de servidor;
- não procura automaticamente outros arquivos, templates ou overrides; cada caminho deve ser passado explicitamente;
- não adiciona chaves ausentes e não interpreta formatos não correspondentes ao padrão citado.

### Bytes, backup e idempotência comprovados pela fixture

O teste [Test-RetroArchAudioRepair.ps1 — conferido, linhas 1–20](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/tools/tests/Test-RetroArchAudioRepair.ps1#L1-L20) cria um `retroarch.cfg` temporário com BOM UTF-8, CRLF, caractere acentuado, volume, driver e uma linha sentinela `server_setting`. Ele então verifica por Base64:

1. que somente os três valores esperados mudaram;
2. que o backup é byte por byte igual à entrada;
3. que a segunda execução não cria outro backup;
4. que o atalho `F9` não é alterado.

`server_setting = "unchanged"` é apenas uma sentinela sintética da fixture; não é configuração real do servidor PIX e não contém credencial.

### Configuração real versus fixture de teste

O relatório `REVISAO-PIX-AUDIO.md` registra duas correções locais, externas ao Git:

- template `D:\TURBOPCINSTALL\system\templates\retroarch\retroarch.cfg`: modo WASAPI exclusivo desativado; backup `retroarch.cfg.audio-backup-8bb153e37b0248adbdfad8b73bfac984`;
- instalação `D:\TURBOPCINSTALL\build\emulators\retroarch\retroarch.cfg`: modo exclusivo desativado e binding `O` removido; backup `retroarch.cfg.audio-backup-c3ffc72c953447c5aaf3187f85c48927`.

Esses dois arquivos reais e seus backups não fazem parte do commit nem do artefato GitHub. O ZIP inclui o reparador e o guia, mas não substitui automaticamente o `retroarch.cfg` de uma instalação. A fixture temporária do teste prova a transformação de bytes; ela não prova o som num emulador real.

## 7. Integração preservada entre `PixAgentManager`, `PixBridge` e `CreditManager`

Esta seção descreve somente as fronteiras necessárias para entender por que a espera de áudio não altera pagamentos ou contagem de tempo. Nenhum valor de token, chave, conta ou credencial é reproduzido aqui.

### 7.1 Inicialização e supervisão do agente

Na inicialização, `main.cpp` chama `PixAgentManager::startIfConfigured`. Se o proprietário ainda não configurou PIX, nada externo é iniciado. Referência: [main.cpp — conferido, linhas 664–671](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/main.cpp#L664-L671).

O manager carrega e valida a configuração persistente, confirma confiança/identidade do agente e, quando necessário, cria o processo daemon oculto e inicialmente suspenso. Ele confirma a identidade publicada usando PID, instante de criação e hash de um token efêmero antes de aceitar o agente. O material efêmero é limpo da memória de processo após o start. Esses comportamentos preexistiam e não foram modificados pela correção de áudio: [startIfConfigured — conferido, linhas 1453–1556](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/PixAgentManager.cpp#L1453-L1556).

Durante o menu, a supervisão roda a cada 15 segundos. Se o agente autenticado estiver ausente, tenta iniciá-lo; se a identidade estiver ambígua, falha fechado; se o heartbeat estiver velho fora da tolerância de startup, reinicia apenas o daemon autenticado. Referência: [ViewController — conferido, linhas 977–985](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/views/ViewController.cpp#L977-L985) e [superviseIfConfigured — conferido, linhas 1563–1605](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/PixAgentManager.cpp#L1563-L1605).

### 7.2 Pedido e evento aprovado

`PixBridge` lê somente opções públicas recentes do agente, obtém do `CreditManager` a carteira beneficiária ativa e valida pacote, limite e destino antes de escrever um pedido assinado por arquivo. Referência: [PixBridge — conferido, linhas 507–678](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/PixBridge.cpp#L507-L678).

A cada dois segundos no menu, a ponte procura eventos aprovados. Ela valida schema, nome/ID, assinatura, prazo e tombstones; eventos inválidos ou conflitantes são isolados para rejeição/reconciliação. Somente então chama `CreditManager::applyPixCredit`. Referência: [poll da ponte — conferido, linhas 966–975](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/views/ViewController.cpp#L966-L975) e [processApprovedCredits — conferido, linhas 835–942](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/PixBridge.cpp#L835-L942).

`CreditManager` valida ID de transação, duplicidade, carteira, teto absoluto e capacidade de persistência. Ledger, carteira e contabilidade entram no mesmo replace atômico; se a persistência autoritativa falhar, o estado em memória é restaurado e operações são bloqueadas. Referência: [applyPixCredit — conferido, linhas 2573–2689](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/CreditManager.cpp#L2573-L2689).

### 7.3 Lançamento, contador e término do jogo

No `launchGame`, a versão continua:

- bloqueando o jogo sem saldo antes de desmontar a interface;
- iniciando a sessão somente depois que o processo externo foi criado e chegou ao primeiro poll supervisionado;
- atualizando saldo pelo tempo supervisionado, exibindo avisos e encerrando a árvore do processo quando o crédito termina;
- finalizando uma única vez a sessão pelo guard RAII;
- persistindo o último intervalo ao encerrar o jogo.

A única inserção é a espera de áudio entre `window->deinit` e `game-start`. A contabilidade de jogo permanece em [CreditManager — conferido, linhas 2794–2866](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/CreditManager.cpp#L2794-L2866), cujo blob é idêntico ao da base.

## 8. Proteções de compilação sem alterar as regras PIX

O CMake ganhou a opção `TURBORAMA_RELEASE_HARDENING`, desligada por padrão: [CMakeLists.txt — conferido, linhas 19–26](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/CMakeLists.txt#L19-L26). No MSVC, ela ativa o mesmo conjunto de otimização/hardening de Release já disponível ao perfil comercial: `/O2`, `/GL`, `/guard:cf`, `/GS`, `/Gy`, `/Gw`, `/Brepro`, LTCG, ASLR, DEP, high-entropy VA, CET e eliminação de código/dados duplicados: [linhas 270–283 — conferido](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/CMakeLists.txt#L270-L283).

O workflow liga essa opção em [linhas 103–111 — conferido](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/.github/workflows/compilar-emulationstation-windows.yml#L103-L111). As opções comerciais de digest do bundle e exigência de assinatura continuam separadas e inalteradas. Portanto, o release geral fica otimizado/protegido sem inventar certificado, chave privada ou digest comercial. O executável publicado continua sem assinatura digital, fato declarado pelo workflow.

## 9. Testes e o alcance real de cada um

### 9.1 Fila C++ real extraída da fonte

`Test-AudioHandoff.ps1` extrai da própria `VideoVlcComponent.cpp` a classe da fila, compila um harness C++17 com MSVC e força o release a aguardar numa barreira. Ele verifica:

- fila inicialmente drenada;
- job já retirado do deque, mas ainda em execução, não é confundido com conclusão;
- timeout enquanto há trabalho;
- 15 jobs enfileirados mais um job em execução atingem o limite;
- o 17º release usa overflow síncrono e também aparece em `mInFlight`;
- após liberar a barreira, todos os 17 releases terminam e a fila volta a drenada.

Referência: [Test-AudioHandoff.ps1 — conferido, linhas 1–83](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/tools/tests/Test-AudioHandoff.ps1#L1-L83).

É um teste executável da lógica real da fila, mas usa stubs para libVLC e `VideoContext`. Ele não abre o driver de áudio do Windows nem inicia RetroArch.

Também não cobre `Window::deinit`, o caminho direto sem `VideoContext`, um `enqueue` posterior ao retorno da barreira ou o tempo sem limite do fallback síncrono. Por isso ele valida a contabilidade/concurrent queue, não um teto de três segundos para o launch completo.

### 9.2 Compatibilidade com crédito

`Test-LaunchCreditCompatibility.ps1` verifica estruturalmente que o modo de crédito é congelado antes do launch; que o bloqueio sem saldo permanece; que `beginGameSession` existe apenas no caminho supervisionado; que o guard não cobra sessão inexistente; que a ordem `game-start > process.run > game-end` continua; e que o caminho supervisionado usa Job Object, processo suspenso, associação ao job, resume e encerramento fail-closed.

Referência: [Test-LaunchCreditCompatibility.ps1 — conferido, linhas 1–69](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/tools/tests/Test-LaunchCreditCompatibility.ps1#L1-L69).

### 9.3 Fixtures do `CreditManager`: CRLF não é configuração real

O único ajuste em `Test-CreditManagerFailClosed.ps1` normaliza para LF sete here-strings mantidas em memória antes de aplicar regex que fabrica casos inválidos. Em checkout Windows, as fixtures herdavam CRLF; algumas regex ancoradas por linha deixavam de fazer a mutação e podiam testar acidentalmente uma entrada ainda válida.

Referência: [normalização das fixtures — conferido, linhas 137–143](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/tools/tests/Test-CreditManagerFailClosed.ps1#L137-L143).

Essa mudança não normaliza, migra ou grava a configuração real do cliente. Ela altera somente variáveis sintéticas do teste. O loader e os arquivos persistentes do `CreditManager` não mudaram.

### 9.4 Limitação do teste antigo `Test-PixAgentDaemonIdentity.ps1`

O teste histórico tenta extrair de `TurboRamaPixCredentialEditor.cpp` uma função chamada `duplicateLoggedKioskToken`. Essa função não existe nem na base `76b2148` nem no HEAD `476e061`. Assim, o teste para no próprio mecanismo `Get-BracedBlock` antes de validar o restante do contrato.

Isso caracteriza deriva entre teste e árvore de código, não evidência de regressão causada pelo áudio. O teste antigo não foi modificado para “passar”, não foi usado como gate desta compilação e nenhuma lógica do daemon foi alterada para satisfazê-lo. Referência do pressuposto desatualizado: [Test-PixAgentDaemonIdentity.ps1 — conferido, linhas 115–125](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/tools/tests/Test-PixAgentDaemonIdentity.ps1#L115-L125).

No workflow desta versão, a cobertura usada é:

- testes de áudio, reparador, compatibilidade de launch/crédito e fail-closed antes da compilação;
- `--credit-warning-overlay-self-test` e `--pix-agent-manager-self-test` no EXE empacotado;
- `--pix-agent-trust-self-test` esperando código **32**, pois o pacote do frontend deliberadamente não instala o agente/servidor.

Referência: [workflow, testes pré-build — conferido, linhas 116–129](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/.github/workflows/compilar-emulationstation-windows.yml#L116-L129) e [smokes do EXE — conferido, linhas 293–315](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/.github/workflows/compilar-emulationstation-windows.yml#L293-L315).

O sucesso desses autotestes confirma contratos locais do frontend e a rejeição correta de um agente ausente. Não equivale a iniciar o agente real de uma instalação completa, conectar a um provedor ou efetuar pagamento.

## 10. Workflow e release separados da outra versão

O workflow aceita push somente na branch `PIX-SERVIDOR-CONTADOR-20260904-1605`, usa grupo de concorrência por ref e publica na tag própria `build-PIX-SERVIDOR-CONTADOR-20260904-1605`: [workflow — conferido, linhas 1–30](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/.github/workflows/compilar-emulationstation-windows.yml#L1-L30). Isso evita que a compilação ou release desta versão substitua os artefatos da versão sem serviços.

As dependências Windows são fixadas no commit indicado pelo workflow. O estágio inclui o frontend, DLLs, plugins VLC, recursos, vídeos, runtimes MSVC/OpenMP, o reparador e seu guia. Não inclui automaticamente `retroarch.exe`, `retroarch.cfg`, servidor ou agente PIX: [empacotamento — conferido, linhas 166–195](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/.github/workflows/compilar-emulationstation-windows.yml#L166-L195).

O commit `476e061` publica o mesmo EXE que passou pelos smokes como arquivo avulso, calcula SHA-256 separado e ainda publica o ZIP. O EXE avulso depende das DLLs, plugins, recursos e configuração da instalação existente; não é instalador nem pacote autônomo. Referências: [hash do EXE — conferido, linhas 317–326](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/.github/workflows/compilar-emulationstation-windows.yml#L317-L326) e [publicação — conferido, linhas 359–417](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/.github/workflows/compilar-emulationstation-windows.yml#L359-L417).

Segundo `REVISAO-PIX-AUDIO.md`:

- execução final: [GitHub Actions 33959943942](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/actions/runs/33959943942);
- release: [build-PIX-SERVIDOR-CONTADOR-20260904-1605](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/releases/tag/build-PIX-SERVIDOR-CONTADOR-20260904-1605);
- EXE avulso: 789.809.152 bytes, SHA-256 `c7732005e26e8ce88e7a6c0493491101c40e9badbf8cfe96ec911fa970f6ae4e`;
- ZIP final: SHA-256 `d1e54db3ea4547279dcf47aa140ca8cc89b2021432b3dada55402064d0e414cb`;
- os arquivos grandes não foram baixados neste computador; hashes e resultado foram conferidos no GitHub/release;
- o executável não possui assinatura digital.

## 11. Procedimento de atualização e verificação em campo

### Atualizar apenas o frontend PIX

1. Fechar o EmulationStation.
2. Guardar cópia do `emulationstation.exe` anterior.
3. Substituir somente o EXE na instalação PIX já completa.
4. Manter DLLs, plugins, recursos, perfil `.emulationstation`, pasta `pix`, agente, servidor e configurações existentes.
5. Conferir o SHA-256 antes de executar.

### Corrigir a configuração RetroArch

1. Fechar RetroArch e confirmar que não ficou processo aberto.
2. Executar o reparador com o caminho literal do `retroarch.cfg` usado pela instalação.
3. Se houver template do instalador, executar também para esse caminho, separadamente.
4. Guardar o caminho `BACKUP=` exibido pelo script.
5. Repetir o mesmo jogo e observar o log do frontend.

Resultado esperado no log quando a fila drena:

```text
[AudioHandoff] VLC audio released before game launch
```

Resultado que exige investigação adicional:

```text
[AudioHandoff] VLC release exceeded 3000 ms; continuing game launch
```

Se a fila drenar e o jogo continuar mudo, verificar o log de inicialização do RetroArch, overrides do core/jogo e o dispositivo escolhido pelo Windows. Para reverter a configuração, fechar RetroArch e restaurar o backup correspondente.

## 12. Limites da validação

- Não havia `retroarch.exe` na instalação examinada. A fila e a transformação de configuração foram testadas, mas som em jogo real ainda precisa ser confirmado num equipamento com emulador instalado.
- Nenhum pagamento real foi feito, nenhuma conta de provedor foi acessada e nenhuma configuração do servidor foi alterada.
- Os autotestes PIX validam contratos locais e falha fechada; não prometem conectividade, aprovação bancária ou operação do daemon externo numa instalação específica.
- O teste antigo de identidade do daemon está desatualizado para esta árvore e não foi usado como prova.
- O EXE avulso foi publicado para atualização de instalação existente; sozinho, numa pasta vazia, não representa a versão operacional completa.
- O `wait_for` de três segundos é deliberadamente limitado, mas o teardown completo não tem teto estrito por causa do overflow síncrono anterior. Em timeout, a inicialização continua e o áudio não é garantido.

## 13. Critérios objetivos para considerar o handoff aceito

- Branch e tag são as exclusivas da versão PIX.
- HEAD conferido: `476e06179f89ac209ff808dffb27555d740f93d2`.
- Fonte funcional do programa preservada após `2741543`; `476e061` altera somente publicação.
- `CreditManager.cpp`, `PixAgentManager.cpp`, `PixBridge.cpp`, `main.cpp`, `Settings.cpp`, `CarouselComponent.cpp` e `SystemView.cpp` têm blobs idênticos à base.
- A barreira VLC ocorre depois de parar/esconder as views e antes de `game-start` e da sessão de crédito.
- Fila vazia **e** `mInFlight == 0` são exigidos para sucesso, inclusive no overflow síncrono.
- O `wait_for` possui timeout e deixa o estouro visível no log; o possível bloqueio anterior no overflow síncrono está documentado e não deve ser confundido com esse prazo.
- Reparador toca apenas as três configurações documentadas, cria backup byte-exato e é idempotente.
- Configurações reais, fixtures e estado do pacote estão claramente diferenciados.
- Não há alegação de teste de jogo ou pagamento real onde ele não ocorreu.
