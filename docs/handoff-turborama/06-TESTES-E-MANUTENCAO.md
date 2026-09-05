# Testes, instalação, diagnóstico e continuidade

[Início](README.md) · [Construção](05-COMPILACAO-E-RELEASES.md)

## 1. Separe evidência de expectativa

Um teste aprovado responde à pergunta que ele efetivamente verifica. Teste de fila com funções VLC simuladas comprova o comportamento da fila nesses cenários; não reproduz todos os drivers de áudio. Teste de crédito com dados sintéticos não executa pagamento real. Teste de EXE com agente ausente comprova rejeição dessa condição, não autenticação de um agente válido.

O [histórico](01-HISTORICO-E-ESTADO.md) registra as execuções GitHub confirmadas. Os comandos abaixo são tutorial de reprodução, não alegação de que foram novamente executados durante a escrita deste manual. Os últimos executáveis aprovados continuam os mesmos; este handoff é documentação.

## 2. Matriz dos gates existentes

| Verificação | Cliente 947dad4 | PIX 476e061 | Limite da conclusão |
|---|---|---|---|
| Test-EmbeddedThemeBuild.ps1 | No workflow | No workflow | Empacotamento/fixtures, não todo hardware de destino |
| Construção Release x64 | Aprovada no run registrado | Aprovada no run registrado | Compilar não é validar toda interação |
| Test-NoCommercialServicesBuild.ps1 | Gate com projeto gerado/EXE | Não se aplica | Ausência comercial e invariantes previstos pelo teste |
| Test-AudioHandoff.ps1 | Não incluído nesta branch | Gate | Fila C++ real com stub de liberação VLC |
| Test-RetroArchAudioRepair.ps1 | Não incluído nesta branch | Gate | Edição de fixtures, bytes e backups |
| Test-LaunchCreditCompatibility.ps1 | Não é gate cliente | Gate | Invariantes do código de lançamento supervisionado |
| Test-CreditManagerFailClosed.ps1 | Crédito não é compilado no perfil cliente | Gate | Harness e fixtures de crédito; sem pagamento real |
| --help | Smoke test | Smoke test | Inicialização do caminho de ajuda |
| --protected-decorations-self-test | Smoke test | Smoke test | Decorações protegidas |
| --no-commercial-services-self-test | Smoke test | Não é o perfil PIX | Autoidentificação/verificações cliente |
| --credit-warning-overlay-self-test | Não se aplica | Smoke test | Exercício do overlay no runner |
| --pix-agent-manager-self-test | Não se aplica | Smoke test | Cenários internos do gerenciador |
| --pix-agent-trust-self-test | Não se aplica | Espera código 32 | Rejeição de agente ausente no pacote frontend |
| Som de jogo real | Não comprovado por estes gates | Não comprovado por estes gates | Precisa de emulador, jogo, dispositivo e instalação reais |
| Pagamento/servidor real | Não se aplica | Não realizado nesta rodada | Exige ambiente de homologação autorizado |

O antigo Test-PixAgentDaemonIdentity.ps1 referencia um símbolo ausente na base examinada. Isso está registrado no capítulo PIX; ele não foi “consertado” alterando o daemon nem contado como teste aprovado. Investigar/atualizar esse teste é uma tarefa separada.

## 3. Reproduzir os testes de desenvolvimento

Somente em uma cópia de desenvolvimento, com dependências de compilação já disponíveis, dentro de TurboramaEmulationStation. Cada script deve ser executado em processo PowerShell separado como no workflow; isso evita interferência entre tipos C# carregados em testes.

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\tests\Test-EmbeddedThemeBuild.ps1
~~~

Na PIX, repetir a forma acima para:

- Test-AudioHandoff.ps1
- Test-RetroArchAudioRepair.ps1
- Test-LaunchCreditCompatibility.ps1
- Test-CreditManagerFailClosed.ps1

O parâmetro ExecutionPolicy Bypass vale para aquele processo de teste; o tutorial não manda alterar a política global do computador.

No cliente, depois da construção local com a pasta do tutorial:

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\tests\Test-NoCommercialServicesBuild.ps1 -BuildDirectory .\build-handoff-cliente -Executable .\bin\x64\Release\emulationstation.exe
~~~

Pare em qualquer código de saída não zero. Não continue até a publicação com um gate falhando. Antes de mudar o programa para satisfazer um teste, confirme se o próprio teste corresponde à revisão/variante e se a fixture é válida.

## 4. Aceitação manual em uma máquina de teste

Não faça ensaio de cobrança em produção. Use perfil e ambiente de homologação autorizados, sem reutilizar credenciais ou saldo reais. O modo --home isola o perfil do frontend, mas não é uma sandbox universal dos emuladores, arquivos externos e serviços configurados.

| Cenário | Procedimento | Resultado esperado / registro |
|---|---|---|
| Primeiro início | Usar um perfil de teste novo, com espaço suficiente | Progresso de tema, conclusão e tela utilizável; registrar duração e log |
| Reinício com cache | Fechar normalmente e abrir no mesmo perfil | Cache reconhecido, sem extração completa desnecessária |
| Navegação de sistemas | Alternar carrosséis, listas e modos de vídeo repetidamente | Células corretas, sem tela preta persistente nem travamentos |
| Abrir/fechar menu | Repetir START/F11 e voltar | Autenticação administrativa e vídeos restaurados conforme variante |
| Memória | Medir RAM privada e GPU após aquecimento e várias voltas iguais | Recursos tendem a estabilizar no cenário; investigar crescimento contínuo |
| Lançamento | Abrir e fechar diferentes emuladores | Frontend retorna, entradas e áudio do menu funcionam |
| Áudio | Ouvir menu, lançar jogo, ouvir jogo, sair; repetir | Ambos audíveis; registrar backend, dispositivo e overrides se houver falha |
| Cliente | Inspecionar menus e autoteste | Sem fluxo PIX/locadora/tempo; demais funções preservadas |
| PIX sem agente | Executar cenário de ausência deliberada no ambiente de teste | Rejeição prevista, sem concessão indevida de serviço |
| PIX homologado | Com agente/servidor de teste válidos, usar roteiro existente do ecossistema | Registrar início/fim/supervisão, sem inventar novas regras de crédito |

Não exigir “RAM volta a zero”: processo ativo, bibliotecas, buffers e caches reutilizáveis continuam alocados. Também não declarar que qualquer patamar alto é normal sem comparar com os limites e a carga. O capítulo de memória detalha os pools.

## 5. Atualizar o executável com segurança

1. Confirme a variante instalada e o commit/hash do arquivo novo.
2. Feche frontend e emuladores. Verifique também que não há outra instância usando o EXE.
3. Guarde cópia do executável anterior e seu BUILD-INFO. Para uma troca completa de pacote, preserve um backup da instalação/configurações.
4. Se for atualizar só o EXE PIX, substitua somente emulationstation.exe na instalação PIX compatível. Mantenha DLLs, plugins, recursos, arquivos do agente e configurações.
5. Para uma instalação nova ou com bibliotecas desconhecidas, o EXE avulso não basta: use o pacote e o procedimento de instalação compatível.
6. Teste em ambiente isolado antes de usar na operação normal.

O EXE PIX avulso não executa automaticamente Repair-RetroArchAudio.ps1. Esse script é uma operação explícita de configuração. Baixar apenas o EXE também não baixa o reparador nem as DLLs.

Para conferir um arquivo já baixado:

~~~powershell
Get-FileHash -LiteralPath .\emulationstation.exe -Algorithm SHA256
~~~

Compare o resultado ao hash do asset correto e da execução correspondente; não compare um SHA-256 de EXE com o SHA Git do commit. Os identificadores são de objetos distintos.

O EXE de aproximadamente 790 MB ainda é grande porque incorpora o tema. Esta documentação não mandou baixar novamente esse arquivo.

## 6. Reparar configuração RetroArch

Leia primeiro o capítulo 04 e AUDIO-LEIA-ME.txt da PIX. O script só atua nos caminhos explicitamente fornecidos; não varre automaticamente todos os emuladores.

Com RetroArch fechado, depois de confirmar qual arquivo efetivamente é carregado:

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Repair-RetroArchAudio.ps1 -ConfigPath "C:\CAMINHO-DA-SUA-INSTALACAO\retroarch.cfg"
~~~

O caminho é um exemplo a substituir, não o caminho descoberto da sua máquina. Confira a saída CORRIGIDO/BACKUP ou SEM_ALTERACAO. Guarde o backup.

O reparador modifica as chaves reconhecidas que já existem: modo WASAPI exclusivo e mute vão para false; somente o atalho literal O de mute é trocado por nul. Não acrescenta todas as chaves ausentes, não troca o driver/dispositivo/volume, não edita o servidor e não resolve todos os tipos de ausência de som.

Se um arquivo de override por núcleo/jogo reintroduz a configuração, o arquivo principal sozinho não basta. Identifique o override antes de pedir qualquer outra alteração. O script não é ferramenta de diagnóstico de todo áudio Windows.

## 7. Diagnóstico sem destruir estado

| Sintoma | Conferir primeiro | Evitar |
|---|---|---|
| Primeira abertura parece congelada | Espaço em disco, progresso, log EmbeddedTheme, instância concorrente | Apagar perfil inteiro |
| Tema não aparece após extração | Mensagens de marcador, recursos e cache negativo; variante/commit | Desligar todos os caches |
| DLL ausente / erro de arquitetura | DLLs e plugins do pacote x64, runtimes e pasta de trabalho | Baixar DLL solta de site aleatório |
| Menu tem som, jogo não | Backend/dispositivo do emulador, mute, atalho O, modo exclusivo, overrides, AudioHandoff na PIX | Alterar crédito ou desativar segurança PIX |
| Jogo inicia com demora | Log de teardown/fila, overflow e driver VLC; comando externo | Interpretar a espera de 3 s como limite total de tudo |
| Memória cresce a cada volta | Mesma carga/mídia, contadores/pools e caches descritos | Remover reuso sem medir |
| Menu perdeu ajustes não comerciais | Branch cliente, MainMenuAuth, filtros de compilação e commit947dad4 | Copiar GuiMenu de outra versão inteira |
| PIX não conecta | Instalação/agente/servidor autorizados e mensagens existentes | Resetar saldo, credenciais ou desligar verificação de confiança |
| Git baixa demais | Partial clone, caminhos explícitos e ferramentas locais | git grep de todo o monorepositório em busca genérica |

Colete a mensagem completa e o momento do erro. Antes de compartilhar logs, remova tokens, credenciais, dados de transações e identificadores pessoais.

## 8. Reverter sem perder configurações

Para reverter apenas a atualização do frontend, feche os processos e restaure a cópia do EXE anterior da mesma variante. Se houver mudança de DLLs, restaure o conjunto compatível preservado, não uma mistura de versões.

Para reverter o reparador, feche RetroArch e restaure o backup exato do arquivo correspondente. Não copie o backup do template sobre um arquivo diferente da instalação. A reversão de configuração não é reversão de saldo, nem este roteiro autoriza modificar o servidor.

Não use git reset --hard nem delete um workspace para fazer rollback de uma instalação. Código-fonte, executável e dados de operação têm ciclos de vida diferentes.

## 9. Como continuar o desenvolvimento

1. Identifique a branch-alvo e o último commit de fonte documentado.
2. Confira git status e preserve trabalho existente.
3. Leia o capítulo e o anexo do arquivo que pretende alterar.
4. Defina a menor mudança de comportamento e o teste correspondente.
5. Compare ambas as variantes quando tocar em código compartilhado.
6. Valide testes específicos e o workflow da variante.
7. Confira a tag real e os hashes da entrega.
8. Acrescente a evidência e as pendências ao próximo handoff.

Não faça merge integral PIX→cliente só para transportar uma correção de áudio; isso pode recolocar serviços. Portar uma mudança comum exige revisar seu diff e seus testes nas duas árvores.

Pendências conhecidas para decisão futura: portar e testar o handoff de áudio no cliente; ensaio de áudio em jogos reais; revisar o teste legado de identidade do daemon; manter homologação do servidor PIX separada do frontend; medir desempenho em hardware representativo. Nenhuma dessas pendências foi implementada pelo commit de documentação.

## 10. Escopo do commit deste handoff

Somente arquivos em docs/handoff-turborama. Não altera C++, CMake, workflows, bibliotecas, configurações do emulador, servidor, credenciais, saldo nem executáveis. Não dispara os workflows atuais pelos filtros de paths e não move as tags dos EXEs já publicados.

Os mesmos capítulos das duas variantes são disponibilizados nas duas branches para que um humano consiga comparar a separação sem depender desta conversa.
