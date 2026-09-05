# Arquitetura e inicialização: da fonte à tela

[Início](README.md) · [Guia de leitura](00-COMO-LER.md)

## 1. A fronteira deste produto

O repositório contém outros instaladores, serviços e ferramentas. Os workflows deste handoff constroem o projeto TurboramaEmulationStation. Construir esse frontend não reinstala o servidor PIX, não instala emuladores e não migra banco de dados. Para PIX, as integrações já existentes permanecem dependências do ambiente.

~~~text
fontes C++ + CMake + tema/recursos + bibliotecas fixadas
                 |
                 v
       compilador + linker + recursos Windows
                 |
                 v
       emulationstation.exe + DLLs + pastas
                 |
                 v
janela -> tema -> sistemas/jogos -> seleção -> emulador externo
                                           |
                     somente PIX: crédito/agente/supervisão
~~~

## 2. Onde cada responsabilidade mora

| Caminho | Responsabilidade | O que não fazer |
|---|---|---|
| TurboramaEmulationStation/CMakeLists.txt | Dependências, plataforma, flags e subprojetos | Misturar flags comerciais de assinatura com otimização genérica |
| es-app/CMakeLists.txt | Seleção de fontes do aplicativo, tema como recurso, EXE | Presumir que ocultar um menu retira os serviços do binário |
| es-core/CMakeLists.txt e es-core/src | Janela, recursos, entradas, áudio, vídeo, renderização | Remover cache só porque ele continua ocupando memória |
| es-app/src/main.cpp | Inicialização, loop principal e encerramento | Inicializar tema pesado sem janela/progresso |
| es-app/src/SystemData.cpp e FileData.cpp | Sistemas, catálogo, jogos, comando e execução | Confundir caminho de ROM com executável do frontend |
| es-app/src/views | Navegação e telas | Acoplar autenticação de menu a cobrança |
| es-app/src/guis | Menus e diálogos | Apagar opções não comerciais junto das opções PIX |
| es-core/src/components/CarouselComponent.cpp | Células e vídeos do carrossel | Criar players ilimitados para células invisíveis |
| es-core/src/components/VideoVlcComponent.cpp | LibVLC, buffers, callbacks e liberação | Confundir silêncio/mute com dispositivo já liberado |
| es-core/src/resources/ResourceManager.cpp | Localização e carregamento de recursos | Manter caminhos negativos depois da extração |
| tools/Pack-EmbeddedTheme.ps1 | Construção do recurso do tema | Reaproveitar um binário de tema antigo sem validação |
| tools/tests | Testes e fixtures | Executar fixtures contra o servidor real |
| .github/workflows | Compilação e entrega no GitHub | Compartilhar tag/artefato entre variantes |

As regras específicas da variante são detalhadas nos capítulos 03 e 04. A tabela é um mapa de navegação, não uma lista de arquivos que este handoff alterou.

## 3. Como o CMake transforma o projeto em EXE

Na revisão PIX documentada, o CMake principal exige C++17 no Windows e escolhe as dependências conforme x64/x86. Procura primeiro uma pasta de dependências existente. Se ela não existir, há um fallback que busca origin/master. O workflow evita depender dessa referência móvel fazendo checkout explícito do commit de bibliotecas antes do CMake. Fonte: [CMake, início e descoberta de bibliotecas](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/CMakeLists.txt#L28).

O projeto reúne external, es-core e es-app. es-core é ligado ao aplicativo. Isso permite que janela, carrossel, áudio e recursos sejam compartilhados sem colocar regras do servidor dentro deles. Fonte: [subprojetos](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/CMakeLists.txt#L557).

### Tema incorporado, passo a passo

Leia [es-app/CMakeLists.txt, linha 286 em diante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/CMakeLists.txt#L286):

1. Dentro de MSVC, find_program procura Windows PowerShell. Sem ele, a configuração falha: não há fallback silencioso para um tema velho.
2. A variável EMBEDDED_THEME_PACKER aponta para o script de empacotamento; a ausência também é erro.
3. GLOB_RECURSE com CONFIGURE_DEPENDS rastreia os arquivos do tema.
4. Os payloads de decorações protegidas têm sua presença conferida.
5. EMBEDDED_THEME_BIN fica no diretório de build, em generated. Cada build deve ter seu próprio diretório.
6. add_custom_command cria a pasta e chama o empacotador com Source e Output explícitos.
7. DEPENDS relaciona script e entradas à saída: uma mudança exige regeneração.
8. O arquivo RC depende do binário gerado e recebe seu caminho pela definição TURBORAMA_EMBEDDED_THEME_BIN.
9. add_executable monta o EXE; add_dependencies obriga a geração do tema antes da ligação.
10. O pós-build copia screensaver_videos ao lado do EXE quando a origem existe.

Portanto, o EXE grande não é apenas código C++: ele contém o tema como recurso Windows. A entrega PIX examinada tem aproximadamente 790 MB. Não reduzir esse tamanho apagando conteúdo sem autorização.

## 4. O problema da primeira inicialização

A correção comum 5414039 trata o trabalho pesado de preparar o tema e a consistência dos caches. A primeira extração pode levar tempo e usar disco. Demora sem progresso visível pode parecer travamento; isso não prova, sozinho, deadlock. O fluxo corrigido cria a janela antes da inicialização demorada e atualiza a tela de progresso.

### main.cpp: leitura guiada

As linhas abaixo são da [revisão PIX 476e061](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-app/src/main.cpp#L679). O cliente tem deslocamentos de linhas por causa das condições de compilação; use o anexo da base para comparar.

| Linhas | O que acontece | Por que a ordem importa |
|---|---|---|
| 679–681 | ResourceManager é materializado na thread principal | Trabalho posterior recebe uma instância já criada |
| 690–697 | Tarefas são enfileiradas no ThreadPool | Enfileirar não é iniciar o pool nesta sequência |
| 699–708 | Window/ViewController são criados e a janela é inicializada | Há um lugar para mostrar progresso/erros |
| 715–721 | EmbeddedTheme::initialize recebe callback que redesenha o splash | A extração não fica invisível ao usuário |
| 722–728 | Falha gera aviso; sucesso invalida e recarrega recursos | Caminhos obtidos antes do tema podem estar desatualizados |
| 731–733 | O pool é iniciado após estabilizar tema e recursos | Reduz disputa entre leituras e mudança de configuração |
| 740–755 | Evento start, subsistemas e carregamento dos sistemas | A tela principal passa a usar os recursos preparados |

A preparação do tema aqui é síncrona com callback de progresso; não é correto dizer que toda a extração foi transferida para uma thread de fundo.

## 5. EmbeddedTheme.cpp, função por função

Fonte congelada: [EmbeddedTheme.cpp](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/EmbeddedTheme.cpp). As linhas adicionadas/removidas estão no anexo 01-base.

### Estado publicado, linhas 28–47

THEME_SET_ID identifica o tema embutido. sAvailable é atômico; sInitMutex serializa a inicialização; sInitializationAttempted evita repetir uma tentativa malsucedida dentro do mesmo processo. sRootPath só é publicado depois da preparação. O arquivo usa blocos de 4 MiB para decodificar o payload e reserva 64 MiB ao avaliar espaço livre.

O formato possui identidade de 32 caracteres e usa MD5 como identidade/verificação do conteúdo. A transformação XOR com chave embutida é ofuscação, não um mecanismo forte de sigilo. Nem isso nem o marcador substituem a assinatura de uma distribuição comercial.

### getCacheDirectory, linhas 88–97

Obtém o caminho canônico de .runtime dentro do perfil EmulationStation, cria a pasta e confirma que ela existe. Se falhar, retorna caminho vazio e a inicialização deve abortar. O cache pertence ao perfil, não à pasta de ROMs nem ao banco PIX.

### ScopedThemeCacheLock, linhas 100–150

No Windows, o nome do mutex deriva do caminho normalizado do cache. Duas instâncias usando o mesmo cache na mesma sessão Windows disputam a mesma trava. O namespace Local não coordena automaticamente sessões Windows distintas. A espera ocorre em intervalos de um segundo, até dois minutos, informando progresso. O destrutor libera o mutex adquirido e fecha o handle. Isso protege a preparação comum; não é uma trava de saldo.

### Cache válido e limpeza, linhas 307–384

isValidCacheDirectory exige formato esperado do nome, diretório normal, marcador e theme.xml regulares, recusando links nesses pontos. A identidade do marcador deve coincidir com o nome e, na procura do cache atual, com a identidade completa do payload.

pruneObsoleteThemeCaches não apaga toda a pasta .runtime: filtra candidatos reconhecidos, preserva o atual e o cache anterior mais recente, e só considera os outros após 24 horas. Essas restrições existem para não destruir conteúdo desconhecido ou um cache usado por uma instância recente. Não substitua a rotina por uma exclusão recursiva geral.

### Espaço livre, linhas 387–415

A soma do conteúdo com a reserva é verificada contra overflow. No Windows, a rotina consulta espaço disponível ao chamador. Espaço insuficiente é erro; se o sistema não permite consultar o espaço, há aviso e a rotina permite tentar. Portanto, essa checagem não garante que nunca haverá falta de disco.

### Decodificação, linhas 418–490

A entrada é o payload incorporado. A saída é um ZIP temporário. Um buffer limitado é redimensionado para cada trecho; os bytes são transformados, gravados e acumulados no cálculo de identidade. Falha de escrita ou arquivo incompleto interrompe a operação. Não há necessidade de alocar outra cópia de todo o tema decodificado na RAM.

### Extração, linhas 493–586

isPathInside normaliza caminhos e compara o prefixo com separador. A extração soma tamanhos com checagem de overflow, verifica disco para arquivos expandidos, valida destino e membros e rejeita um membro fora da pasta permitida. O callback é atualizado durante o laço.

Ao final, FileSystemCache::reset descarta resultados negativos coletados antes de a extração criar os arquivos. Depois, theme.xml é conferido novamente. Trata-se de validações implementadas; não de uma certificação de segurança de todos os formatos ZIP ou cenários concorrentes.

### Publicação, linhas 622–720

publishTheme estabelece o caminho, aplica seleção/padrões e publica disponibilidade com memory_order_release. initialize consulta disponibilidade com acquire, adquire a trava, lê o payload e o cache, limpa o temporário reconhecido e tenta reutilizar o cache válido.

Sem cache válido, verifica espaço, decodifica e extrai. Só grava/verifica .payload depois da extração. Se a preparação falha, não anuncia disponibilidade; tenta limpar os resíduos conhecidos. Se conclui, publica o tema e encerra o progresso.

## 6. ResourceManager: por que invalidar caminhos?

[ResourceManager.cpp, linhas 27–88](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/TurboramaEmulationStation/es-core/src/resources/ResourceManager.cpp#L27) guarda uma lista de diretórios e um mapa de caminhos resolvidos. Antes de a extração terminar, um recurso pode parecer ausente. Reutilizar essa resposta depois mantém a interface com resolução errada.

invalidatePathCache limpa lista, nome do tema em cache e mapa sob mutex. getResourcePaths reconstrói a ordem: recursos do tema embutido ativo, temas de usuário/sistema, recursos do perfil, instalação, EXE e diretório atual quando aplicável. getResourcePath resolve nomes começando com :/ e memoriza o resultado.

Invalidação de resolução de recursos não é desligar todos os caches de vídeo. São mecanismos distintos. O capítulo de memória descreve o que continua sendo reutilizado.

## 7. Da seleção do jogo ao retorno

FileData::launchGame prepara opções/comando, suspende elementos da interface, desmonta os recursos previstos e abre o processo externo. Ao retornar, o frontend restaura seus subsistemas. O emulador tem seu próprio backend de áudio.

Na versão cliente, o lançamento não deve passar por cobrança. Na PIX, os caminhos de crédito, agente e supervisão continuam ativos. A espera de liberação do VLC na PIX fica antes do ponto que inicia a sessão supervisionada, para que a espera introduzida não seja deslocada para dentro desse período. Detalhes e limites estão no capítulo 04.

## 8. O que deve permanecer invariável numa manutenção

Preserve a janela/progresso antes do tema pesado; mantenha invalidação após extração; mantenha limites/reuso de vídeo; diferencie UI administrativa de pagamento; não mude a autoridade de crédito ao corrigir áudio; não afirme que uma DLL foi embutida só porque o tema está embutido.

Para comparar literalmente a construção comum, abra o grupo 01-base dos [anexos](anexos/linhas/README.md). Para entender a otimização herdada, siga o [capítulo de memória](02A-MEMORIA-E-VIDEOS.md).
