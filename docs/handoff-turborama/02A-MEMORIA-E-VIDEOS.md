# 02A — Memória e vídeos: o que foi preservado e como funciona

## Escopo da leitura

Leitura dirigida aos nove arquivos do commit comum [0e02780b761cb488c591416d2986130efcc166dd](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/commit/0e02780b761cb488c591416d2986130efcc166dd), com linhas conferidas no HEAD PIX `476e06179f89ac209ff808dffb27555d740f93d2`. O funcionamento descrito é o código presente, não resultado de benchmark. Não foi auditado todo o upstream, todos os gerenciadores de texturas nem todos os drivers VLC. `VideoComponent.cpp` foi lido apenas para explicar o ciclo de vida herdado; ele não pertence aos nove arquivos alterados por `0e02780`.

Checkout de referência: `C:\Users\Admin\Documents\Codex\2026-09-04\pr\work\turborama-pix-audio\TurboramaEmulationStation`. Os links abaixo são fixados no commit, evitando que a numeração mude com a branch.

### Evidência de que a separação não apagou a otimização

| Comparação dos nove arquivos de `0e02780` | Resultado lido no Git |
|---|---|
| `0e02780..476e061` (PIX) | Só `FileData.cpp` (+7 linhas), `VideoVlcComponent.cpp` (+25) e seu `.h` (+1) diferem: acréscimos de sincronização de áudio. Os outros seis são idênticos. Nenhuma remoção nesses nove arquivos. |
| `0e02780..947dad4` (cliente) | Só `FileData.cpp` (+18/−2), `SystemView.cpp` (+14) e seu `.h` (+6) diferem. Os hunks lidos são guards `TURBORAMA_NO_COMMERCIAL_SERVICES` ao redor de crédito/QR/avisos comerciais. Os outros seis arquivos são idênticos, incluindo `Settings.cpp`, `CarouselComponent.{cpp,h}` e `VideoVlcComponent.{cpp,h}`. |

Assim, há prova de preservação dessas implementações no código, além dos testes estruturais. Isso é mais específico que apenas encontrar rótulos de “otimização” no menu. Não significa que todas as máquinas usem os mesmos valores, pois o perfil pode sobrescrever os padrões.

## Não confundir os cinco tipos de recurso

| Recurso | Onde está / objetivo | O que sua existência não significa |
|---|---|---|
| Cache de **caminhos** | Strings e timestamps de `FileData` e `SystemView`; evita pesquisar arquivos e percorrer pastas a cada frame | Não mantém o vídeo inteiro carregado, nem é cache de frames |
| Pool de **componentes de células** | `CarouselComponent::mCellVideoPool`; reaproveita objetos C++/GUI já preparados | Um objeto ocioso no pool não mantém mídia/contexto VLC ativo; não é o mesmo pool de pixels |
| Pool de **pixels** | `sVideoBufferPool`; duas superfícies RGBA por entrada, alocadas com `new[]` | É RAM do processo, apesar do nome `MaxVideoRAM`; não representa toda a VRAM da GPU |
| Player/decoder VLC | `mMediaPlayer`, mídia, parsing e estado interno do VLC | Sua memória interna não é medida integralmente pela conta das duas superfícies |
| Textura de desenho | `mTexture`, atualizada a partir dos pixels e compartilhável | Há custo de GPU/driver separado; a limitação do pool de pixels não é teto universal da memória do processo/GPU |

A otimização combina evitar trabalho, limitar novas alocações, reaproveitar recursos ociosos e liberar recursos pesados. “Esconder a imagem” isoladamente não demonstra que o decoder foi encerrado.

## 1. `FileData`: localizar uma vez, reutilizar nas leituras

Campos: `mCarouselVideoPathCache`, `mCarouselVideoMetadataPathCache`, `mCarouselVideoCacheGeneration`, `mCarouselVideoCacheCheckedAt`, `mCarouselVideoPathCacheValid` ([FileData.h, linha 211](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/FileData.h#L211)). São metadados pequenos, não buffers de vídeo.

`getCarouselVideoPath()` delega a `resolveCarouselVideoPath(false)` ([FileData.cpp, linha 712](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/FileData.cpp#L712)):

1. Se a geração global e o caminho configurado continuam iguais, devolve o caminho já encontrado sem repetir `exists()`.
2. Resultado **negativo** (sem vídeo) vence em 5000 ms. Resultado positivo não tem esse TTL: permanece até uma invalidação relevante. Portanto, copiar um arquivo onde antes não havia vídeo pode ser detectado no próximo ciclo; apagar/trocar externamente um caminho positivo não promete detecção imediata.
3. Ao resolver, tenta o vídeo direto, alternativas pelo nome real da ROM e o layout `media/videos/<caminho relativo da ROM>`, com `.mp4`, `.webm`, `.mkv`, `.avi`.
4. Para pasta sem vídeo próprio, percorre descendentes até encontrar um vídeo. Ao atualizar essa pasta, força a atualização dos filhos consultados, evitando duas janelas de TTL acumuladas.
5. Guarda caminho/metadado/geração depois da resolução, pois a própria busca pode atualizar metadados.

`sCarouselVideoCacheGeneration` é contador atômico comum; `invalidateCarouselVideoPathCache()` avança a geração ([linha 424](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/FileData.cpp#L424)). Mudar pai, metadado de vídeo, adicionar/remover filhos ou limpar pasta invalida caches dependentes, sem manter outra árvore de dependências. O getter genérico `carouselVideo` expõe esse resultado às células. O ganho esperado é reduzir I/O e percursos por frame; não foi medido numericamente nesta revisão.

## 2. `SystemView`: atualizar somente a janela de células relevante

`SystemViewData` guarda player, caminho configurado, resolvido e hora da consulta; a vista guarda `mFrontCarouselActiveVideoIndices`, cursor/quantidade/modo sincronizados e flags de validade/dirty ([SystemView.h, linha 33](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/views/SystemView.h#L33), [linha 156](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/views/SystemView.h#L156)).

`syncFrontCarouselVideos()` ([SystemView.cpp, linha 2160](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L2160)) distingue:

- Vista oculta, screensaver ou desabilitação sem prévia: interrompe os vídeos frontais ativos.
- Modo `images`: zero vídeos frontais.
- Modo `all`: até `min(número de sistemas, maxLogoCount válido)`; outros modos de vídeo usam uma célula.
- Estado estável: não recalcula toda a biblioteca nem reprova cada caminho positivo no disco. Só verifica as poucas células ativas, repetindo resultados negativos após 5 segundos ou reativando uma célula ausente.
- Mudança de cursor/contagem/modo/lifecycle: calcula uma janela circular exata, prioriza a seleção para receber decoder, retira saídas antes de ativar entradas e guarda os novos parâmetros. Em quantidade par, a célula excedente fica no sentido positivo.

Troca de modo marca dirty e reassocia mídia ao reativar, evitando voltar do menu com players parados mostrando somente a capa. O limite de reprodução é atualizado quando a configuração muda, não reatribuído a toda a biblioteca a cada frame.

`showFrontCarouselVideo()` usa caminho já validado e `setVideo(path, false)`, evitando segunda consulta de existência; posiciona dentro da célula/contêiner temático para preservar animações. `hideFrontCarouselVideo()` chama **`stopPlayback()`**, invisibilidade e `onHide()`: não é apenas esconder textura ([linha 2307](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L2307), [linha 2389](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L2389)).

O carregamento do tema cria componentes frontais, inicialmente parados/ocultos. Não confundir a existência desses componentes por sistema com todos os decoders abertos simultaneamente. Vídeos decorativos de `SystemView` têm áudio desabilitado; a prévia principal de jogo é outro componente e mantém sua política.

Há também compartilhamento de **frame já decodificado** quando um extra de fundo tem caminho, posição, tamanho e origem iguais ao fundo-base. `setSharedVideoSource()` conserva desenho/opacity/z/storyboard do extra, mas usa a textura da fonte sem novo player próprio ([SystemView.cpp, linha 301](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L301), [VideoVlcComponent.cpp, linha 708](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L708)). Não é deduplicação universal de todos os vídeos iguais.

## 3. `CarouselComponent`: pool de objetos e células centrais

Os campos centrais são `mCellVideoPool`, `mActiveCellVideoIndices`, `mActiveCellVideoCount` ([CarouselComponent.h, linha 189](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/CarouselComponent.h#L189)). Este pool usa vetor/pilha (`push_back`/`pop_back`), **não LRU**; o LRU fica no pool de pixels, descrito adiante.

Ao desenhar, os buffers de imagens/animações que antecipam a rolagem continuam existentes. Porém, somente as entradas centrais distintas mais próximas da câmera recebem vídeo, em quantidade limitada pelo XML. Entradas fora dessa janela podem continuar com capas sem abrir decoder ([CarouselComponent.cpp, linha 534](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L534)). A seleção de entrada é circular e remove duplicatas; as saídas são devolvidas antes das entradas novas, permitindo reutilização no mesmo frame.

| Função | Comportamento relevante |
|---|---|
| `getCellVideoPoolLimit()` | Limite `min(maxLogoCount, número de entradas)`; zero quando vídeo de célula desabilitado ou lista vazia |
| `acquireCellVideo()` | Recusa quando ativos atingem limite; reduz ociosos excedentes; reutiliza objeto ocioso ou cria novo `VideoVlcComponent`; ativos + ociosos são limitados |
| `prepareCellVideo()` | Exige vista ativa, permissão de vídeo, mídia válida em cache e, se configurado, pasta; tamanho alvo é a célula, sem duplicar escala do logo; células são silenciosas |
| `releaseCellVideo()` | Para mídia, limpa caminho, oculta, chama `onHide`, desanexa do pai e zera ligação com entrada; só guarda wrapper no pool se houver espaço |
| `trimCellVideoPool()` | Ao diminuir `maxLogoCount`, mantém entradas próximas e aposenta excesso, reduz ociosos e atualiza limites dos componentes |
| `remove()` / `clear()` | Desanexa players antes de destruir entradas; remapeia índices após remoção e preserva semântica de cursor do IList |

Fontes: [preparo, linha 797](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L797), [liberação/aluguel/limpeza, linha 875](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L875), [clear/remove, linha 88](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L88). XML inválido de `maxLogoCount` é normalizado para pelo menos 1, prevenindo divisão por zero/conversão negativa para tamanho enorme (linha 704).

## 4. `VideoVlcComponent`: orçamento real das superfícies e LRU

`VideoContext` contém duas superfícies, mutex por superfície, `surfaceId`/`hasFrame` atômicos e índice do pool. `VideoBufferPoolEntry` guarda dimensões, ponteiros, `inUse`, `retiring`, bucket e `lastUsed` ([VideoVlcComponent.h, linha 19](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.h#L19)).

A conta lida é `largura × altura × 4 bytes RGBA × 2 superfícies`. Exemplo matemático: 1920×1080 usa 16588800 bytes, aproximadamente 15,82 MiB, somente nas superfícies; 320×180 usa 460800 bytes, aproximadamente 0,44 MiB. Não somar isso como se fosse o consumo total do VLC/GPU.

`getBufferPoolCacheLimitBytes()` limita pixels **ociosos** a `min(128 MiB, MaxVideoRAM/4)`. `trimBufferPoolLocked()` contabiliza bytes totais e livres e remove os livres de menor `lastUsed` até caber nos limites. Não expulsa `inUse`, inclusive os `retiring` ainda pertencentes a um decoder que está fechando ([linha 330](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L330)). Esse é o LRU: descartar os buffers livres menos recentemente usados primeiro.

`rentContext()` ([linha 1197](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L1197)):

1. Adquire locks na ordem players → pool; contabiliza reservas dos parsers ainda sem contexto.
2. Reutiliza duas superfícies livres de dimensões exatamente iguais.
3. Para alocação nova, desconta as reservas pendentes e remove pixels ociosos por LRU até caber.
4. Se recursos em uso impedirem caber, retorna `nullptr`; usa `new (std::nothrow)` para tratar falha de alocação.
5. Reutiliza entrada vazia do vetor antes de acrescentar outra; mantém as superfícies vinculadas ao contexto até release seguro.

`releaseContext()` só devolve a entrada ao pool **depois** do release do player; marca livre, remove `retiring`, atualiza `lastUsed` e aplica trim. `clearBufferPool()` não apaga pixels em uso defensivamente ([linha 409](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L409)). Retenção de até uma fração do orçamento como cache ocioso é intencional, não necessariamente vazamento.

### Reserva antes de parse e concorrência

`estimatePendingVideoBufferBytes()` estima resolução de tela, reduzida ao alvo quando `OptimizeVideo` permite. `acquirePlaybackSlot()` reserva bytes **antes** de iniciar parse/criar player, impedindo vários candidatos simultâneos de ignorarem a memória dos outros. Após conhecer dimensões, `updatePlaybackReservation()` recalcula a reserva ([linha 309](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L309), [linha 464](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L464)).

Não existe aqui um “máximo global de três vídeos” aplicado indistintamente. O código separa buckets: carrossel, vídeos comuns/builtin e vídeos decorativos geridos pelo tema. Carrossel combina limite por componente/XML e opcional global `MaxConcurrentCarouselVideos`; vídeos comuns usam `MaxConcurrentVideos`; theme-managed não recebe esse teto de contagem, mas continua sob o orçamento de pixels. `EnforceVideoLimit=false` desativa a barreira de **contagem**, não a de **bytes**.

Se a contagem lotar, `computePlaybackPriority()` considera visibilidade, opacidade, tags, z-index e screensaver; `acquirePlaybackSlot()` pode parar um candidato de menor prioridade no mesmo bucket. Esse player em retirada continua contado: não libera instantaneamente seu token para permitir avalanche de substitutos. Sem vaga/memória, o início é adiado e retenta; não cria indefinidamente players novos ([linha 516](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L516)).

## 5. Resolução, uploads de textura e falhas de decoder

`onMediaParsed()` lê dimensões/trilhas, ajusta dimensões de saída à tela/alvo quando `OptimizeVideo=true`, atualiza reserva e configura callbacks RGBA nessas dimensões ([linha 1716](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L1716)). Isso reduz comprovadamente o tamanho solicitado das superfícies e da textura. Não permite prometer que todo codec/driver descomprime internamente o vídeo-fonte já em resolução reduzida; esse trabalho interno do VLC não foi medido.

`render()` descarta desenho quando invisível/fora da tela e, no Windows/RPi com `OptimizeVideo`, limita atualizações de textura a intervalos de pelo menos 33 ms, cerca de 30 uploads/s. Callbacks publicam a disponibilidade de frames com sincronização; mudanças de estado do componente ocorrem em `update()` na UI, não no callback decoder ([render, linha 1039](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L1039), [update, linha 1979](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L1979)). **Limitar upload/desenho não é limitar necessariamente o FPS interno do decode**, nem por si só liberar player.

No Windows, `createMedia()` tenta hardware quando não existe opção explícita contrária; `trySoftwareDecoderFallback()` tenta software uma vez para o caminho, voltando pelo alocador de slots para continuar respeitando reservas/players em retirada. `update()` detecta abertura sem primeiro frame: 8 s em hardware, 12 s em software. Falhas repetidas sofrem atraso e, após mais de três, pausa de retentativas de 60 s, evitando reabrir um arquivo quebrado a cada frame ([linha 1406](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L1406), [linha 1542](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L1542), [linha 1570](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L1570)). Isso é recuperação limitada, não garantia de compatibilidade com todo formato/GPU.

## 6. Ocultar, pausar, parar e liberar são operações diferentes

| Operação | Recurso pode continuar existindo? |
|---|---|
| Retornar cedo de `render()` | Sim: apenas deixa de desenhar/upload naquele momento |
| `pauseVideo()` | Sim: pausa VLC, preserva mídia/player/contexto para retomar rapidamente |
| `stopVideo()` | Retira registro ativo, silencia/desliga vínculos da UI e envia release pesado à fila; decoder pode continuar em teardown até worker terminar |
| `releaseContext()` após VLC release | Devolve pixels ao pool ocioso e aplica LRU; pode manter buffers para reutilização |
| Descartar buffer ocioso pelo trim | Finalmente executa `delete[]` das superfícies; não corresponde a encerrar todo o processo |

`VideoComponent::manageState()` pode **pausar** ao perder top-window/ser desabilitado ainda visível; em outros casos de ocultação para. Por isso não tratar todo menu sobreposto como liberação completa. Já as rotinas específicas de saída de célula chamam `stopPlayback()` explicitamente ([VideoComponent.cpp, linha 465](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoComponent.cpp#L465), [VideoVlcComponent.cpp, linha 2099](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L2099)).

`MediaPlayerReleaseQueue` usa um worker serial de vida do processo, não uma thread detached por movimento. O limiar de 16 jobs (fila + em andamento) aplica contrapressão: excesso é liberado sincronicamente pelo chamador. Isso evita crescimento irrestrito da fila, mas pode custar uma espera sob rolagem patológica; não confundir o limiar da fila assíncrona com “nunca pode existir um 17º release síncrono”. Antes de enfileirar, o contexto perde o ponteiro para o componente e os pixels ficam `retiring`; só retornam ao pool após o VLC terminar. No encerramento, o worker é drenado/join e o pool limpo ([linha 43](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L43), [linha 232](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L232)).

### Relação com a correção posterior de áudio

A fila assíncrona é otimização comum de `0e02780`. A sincronização `waitForAudioRelease()` é adição posterior **só no PIX**, em `7de017c`: usa condition variable e só considera drenada quando não existem jobs nem releases em andamento, inclusive overflow síncrono. `FileData` espera até 3000 ms depois de `window->deinit`, antes do evento de início e do relógio da sessão. No timeout registra warning e continua. A navegação normal não espera a fila a cada célula; os pools permanecem ([FileData.cpp, linha 1237](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/FileData.cpp#L1237)).

Player silenciado não é necessariamente dispositivo de áudio liberado. A espera trata essa diferença, sem remover a otimização de teardown assíncrono. Ela não garante que qualquer emulador terá som: dispositivo, driver, configuração exclusiva/mute e o próprio emulador ainda precisam ser testados. Cliente `947dad4` não possui essa adição C++.

## 7. Padrões preservados em `Settings.cpp`

Valores atuais da base x64, não uma recomendação automática para todo hardware ([Settings.cpp, linha 224](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/Settings.cpp#L224), [linha 376](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/Settings.cpp#L376)):

| Chave | Padrão Windows x64 | Distinção necessária |
|---|---:|---|
| `MaxVRAM` | 3072 | Configuração do orçamento de imagens/texturas; seus consumidores completos não foram auditados neste capítulo |
| `MaxRAM` | 2048 | Configuração geral de cache; não é limitador do working set inteiro do processo |
| `MaxVideoRAM` | 768 | No código examinado, orçamento das duas superfícies de pixels, convertido com `1024×1024` (MiB) |
| `MaxAsyncQueue` | 12 | Fila de imagens; não é `MAX_RELEASE_JOBS=16` da fila VLC |
| `MaxConcurrentVideos` | 3 | Bucket de vídeos comuns, não total universal de vídeos do tema |
| `MaxConcurrentCarouselVideos` | 0 | Zero mantém quantidade de células do XML; não significa desligar orçamento de RAM |
| `EnforceVideoLimit` | true | Liga barreira de contagem; barreira de bytes permanece separada |
| `ThreadedLoading`, `AsyncImages`, `OptimizeVRAM`, `OptimizeVideo` | true | Flags preservadas; o efeito total de imagens não foi medido aqui |
| `PreloadUI` | false em x64 | Evita pré-carregamento total da UI nesse padrão; outras plataformas têm padrão true |

Os perfis32-bit/placas têm padrões menores próprios. `MaxVideoRAM<=0` não significa ilimitado: `getMaxVideoRamMb()` deriva fallback de `MaxRAM/4`, limitado entre64 e768, ou128 quando não há MaxRAM válido. Não confundir padrão, valor persistido pelo usuário e memória efetivamente observada.

## Limites da conclusão e roteiro de validação futura

Foi comprovada a presença e preservação da lógica lida, e documentados seus limites. Não foram realizados novo benchmark de RAM/VRAM/CPU, ensaio prolongado de rolagem, teste com todas as bibliotecas/temas/GPU, nem reprodução de áudio em jogo real. Sem medições comparáveis, não afirmar porcentagem de economia ou eliminação universal de vazamentos.

Uma validação futura pode registrar build/hash, configuração efetiva e mesmo conjunto de mídias; medir navegação em `images`/um vídeo/`all`, rolagem rápida, abrir/fechar menu e jogo, arquivo ausente/quebrado e retorno do emulador. Distinguir pico transitório de decoder em retirada, retenção limitada do pool ocioso e crescimento contínuo sem estabilização. Qualquer correção futura deve preservar essas invariantes e o isolamento comercial documentado, em vez de remover pools/caches para esconder sintomas.
