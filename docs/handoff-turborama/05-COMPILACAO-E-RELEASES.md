# Construção, testes e publicação: tutorial do começo ao fim

[Início](README.md) · [Histórico e estado verificado](01-HISTORICO-E-ESTADO.md)

## 1. Escolha primeiro a variante

| Item | Cliente sem serviços | PIX |
|---|---|---|
| Branch | CLIENTE-SEM-SERVICOS-20260904-1818 | PIX-SERVIDOR-CONTADOR-20260904-1605 |
| YAML em .github/workflows | compilar-cliente-sem-servicos-windows.yml | compilar-emulationstation-windows.yml |
| Flag de serviços no comando CMake | TURBORAMA_ENABLE_COMMERCIAL_SERVICES=OFF | Não usar essa flag: esta branch mantém a arquitetura PIX original |
| Otimizações/proteções gerais | TURBORAMA_RELEASE_HARDENING=ON | TURBORAMA_RELEASE_HARDENING=ON |
| Tag de entrega | build-CLIENTE-SEM-SERVICOS-20260904-1818 | build-PIX-SERVIDOR-CONTADOR-20260904-1605 |
| Pacote | TurboramaEmulationStation-Cliente-Sem-Servicos-Windows-x64.zip | TurboramaEmulationStation-Windows-x64.zip |
| EXE avulso nesta fotografia | Não publicado como asset separado | emulationstation.exe e .sha256 |

As flags de serviços da branch cliente não são uma receita genérica para transformar qualquer checkout PIX em cliente. A seleção de fontes e os blocos condicionais precisam existir na fonte da variante.

## 2. Compilar sem instalar compilador no seu computador

A opção principal é GitHub Actions. O seu computador só acessa a página. O runner remoto continua usando Windows Server 2022 e Visual Studio 2022; “não precisar do Windows local” não significa que o binário Windows é construído sem um ambiente Windows.

1. Abra o repositório no GitHub e selecione a branch correta.
2. Abra Actions e escolha o workflow da tabela.
3. Quando “Run workflow” estiver disponível, escolha explicitamente a branch correta e execute.
4. Caso a interface não ofereça disparo manual, não modifique a branch padrão nem contorne as proteções: os YAMLs já têm disparo por push nos caminhos de fonte/receita daquela branch.
5. Confira o commit mostrado na execução. Branch correta com commit antigo continua sendo entrega antiga.
6. Aguarde os testes, a compilação, os autotestes do pacote e a publicação. Um job verde intermediário não é a release completa.
7. Abra a release daquela variante e confira notas, nome dos assets e hashes.

O disparo manual usa workflow_dispatch e permite selecionar branch. Sua disponibilidade também depende de o workflow estar registrado na branch padrão do repositório. Fonte: [documentação do GitHub para execução manual](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow).

Se já houver GitHub CLI instalado e autenticado com acesso autorizado, o equivalente é:

~~~powershell
gh workflow run compilar-emulationstation-windows.yml --repo luziellacerda/Backup-Instaladores-Compiladores-Turborama --ref PIX-SERVIDOR-CONTADOR-20260904-1605
~~~

Para cliente, troque tanto o arquivo quanto a branch pela linha correspondente da tabela. Não cole token em scripts, commits ou documentos. Referência: [gh workflow run](https://cli.github.com/manual/gh_workflow_run).

## 3. YAML lido por blocos e linhas

Fontes congeladas: [workflow cliente 947dad4](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/.github/workflows/compilar-cliente-sem-servicos-windows.yml), [workflow PIX 476e061](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/.github/workflows/compilar-emulationstation-windows.yml).

### Cabeçalho: linhas 1–33 das duas receitas

- name é a identificação humana mostrada em Actions.
- on.push.branches restringe a branch elegível.
- paths limita o gatilho ao projeto e ao YAML correspondente; editar este manual em docs/handoff-turborama não recompila o programa.
- workflow_dispatch permite o disparo manual nas condições descritas acima.
- permissions.contents: write é usado para publicar a release, não para alterar a configuração do servidor.
- concurrency.group inclui github.ref; as branches têm grupos distintos.
- cancel-in-progress substitui uma execução anterior da mesma referência quando chega outra. Não confundir cancelamento por novo commit com erro do compilador.
- WIN32_LIBS_COMMIT fixa a revisão de dependências. RELEASE_TAG e PACKAGE_BASENAME separam as entregas.
- _CL_=/Zm300 ajusta o limite de memória interna do compilador; não é um limite da RAM do programa em execução.
- jobs.build.if repete a proteção da branch. Um dispatch na branch errada não deve gerar aquele pacote.
- runs-on escolhe windows-2022; timeout-minutes limita o job a 120 minutos.

Essa separação reduz colisões acidentais entre variantes. Ela não torna impossível um erro futuro de edição do YAML; por isso os nomes e guardas entram na revisão.

### Checkout: linhas 37–51

O primeiro checkout traz o projeto com sparse-checkout e desativa persistência de credenciais no checkout. O segundo traz as bibliotecas para win32-libs no commit 468eaba48c028921a4bf2abdfa3f3a00ce8d4c0d. A ação checkout também está fixada por SHA.

Sparse-checkout reduz o escopo do monorepositório, mas o projeto ainda inclui mídia pesada necessária ao tema. Não execute buscas Git de conteúdo em toda a árvore de um partial clone: isso pode baixar blobs que não estavam locais.

### Validação: linhas 53–93

O script confirma a existência de CMake, theme.xml, DLLs fundamentais e plugins VLC. Compara o HEAD das bibliotecas ao SHA esperado. Depois executa Test-EmbeddedThemeBuild.ps1 em Windows PowerShell 5.1 e falha se o código de saída não for zero.

Presença de DLL não basta para concluir que a aplicação inicia; essa checagem é uma primeira barreira. A arquitetura dos binários e os autotestes são conferidos depois.

### Configuração: cliente 95–115; PIX 95–114

O pipeline remove variáveis de toolchain/vcpkg herdadas do ambiente para evitar selecionar outra instalação sem querer. Invoca CMake com gerador Visual Studio 17 2022, plataforma x64, toolset v143 com host x64, RETROBAT=OFF, BATOCERA=OFF, hardening geral ON e SDK 10.0.26100.0. Só o cliente passa serviços OFF.

Configurar gera os projetos: não produz o EXE ainda. A opção --config Release é aplicada na fase seguinte porque Visual Studio é um gerador de múltiplas configurações. Referência: [linha de comando do CMake](https://cmake.org/cmake/help/latest/manual/cmake.1.html).

### Testes antes/depois da construção

Na PIX, linhas 116–129 executam os testes de handoff de áudio, reparador RetroArch, compatibilidade do lançamento e bateria de crédito antes de compilar o programa completo.

No cliente, linhas 134–145 executam Test-NoCommercialServicesBuild.ps1 após a construção, com BuildDirectory e Executable explícitos. Esse teste verifica tanto o projeto gerado quanto aspectos do executável. Não o substitua por uma procura visual de palavras no menu.

### Construção: cliente 117–132; PIX 131–146

cmake --build recebe o diretório gerado, Release, o alvo emulationstation e --parallel 1. Essa opção limita o paralelismo solicitado ao backend, sem garantir compilação inteiramente serial: o CMake também mantém /MP nas flags C/C++, permitindo paralelismo interno do compilador. Aumentar paralelismo muda a demanda de memória e precisa de teste. Uma falha de compilação interrompe o job, e a existência do EXE no destino esperado é conferida.

As flags /GL e /LTCG habilitam otimização entre unidades de compilação e na ligação. Isso pode tornar o link mais demorado; não interprete demora como travamento sem ver os logs. Elas não garantem igualdade byte a byte entre builds em runners diferentes. Referências: [MSVC /GL](https://learn.microsoft.com/en-us/cpp/build/reference/gl-whole-program-optimization?view=msvc-170) e [MSVC /LTCG](https://learn.microsoft.com/en-us/cpp/build/reference/ltcg-link-time-code-generation?view=msvc-170).

## 4. O que entra no pacote e por quê

A montagem começa na linha 147 do cliente e 148 da PIX. Um stage temporário recebe:

| Grupo | Necessidade |
|---|---|
| emulationstation.exe | Aplicativo/tema incorporado |
| SDL2 e SDL2_mixer | Janela/entrada e áudio do frontend |
| FreeImage | Imagens |
| libcurl | Requisições usadas pelo frontend |
| libvlc e libvlccore | Vídeos |
| DLLs opcionais de codecs | Formatos usados por bibliotecas de áudio |
| plugins/ | Plugins VLC; o pipeline exige pelo menos 100 DLLs |
| resources/ | Recursos externos do aplicativo |
| screensaver_videos/ | Vídeos externos previstos pelo projeto |
| CRT do MSVC e vcomp140 | Runtimes C++/OpenMP x64 |
| BUILD-INFO.txt | Origem, commit, bibliotecas e runner |
| TESTAR-ISOLADO.cmd | Inicialização com perfil separado |
| SHA256SUMS.txt | Hashes individuais dos arquivos já montados |
| Reparador e AUDIO-LEIA-ME, só PIX nesta fotografia | Ajuste explícito de configurações RetroArch |

Assert-X64PortableExecutable abre cada EXE/DLL, confere MZ, offset/assinatura PE e machine 0x8664. Isso detecta mistura x86/x64 nessa lista, mas não atesta todos os aspectos de compatibilidade de uma DLL.

Invoke-SmokeTest inicia o EXE montado, espera até 30 segundos e verifica o código de saída. No cliente são help, decorações e ausência de serviços. Na PIX são help, decorações, overlay, gerenciador PIX e a rejeição de agente ausente com código 32.

O manifesto interno é produzido antes do ZIP e não inclui hash de si mesmo. O hash externo do ZIP cobre o ZIP final. Na PIX, o EXE avulso é o mesmo caminho que foi testado no stage; não é uma segunda compilação com flags diferentes.

O ZIP usa NoCompression no workflow atual. Não suponha que esse nome de pacote signifique um download fortemente comprimido.

## 5. Publicação separada e limites da “release”

Cliente: linhas 341–406. PIX: linhas 359–431.

1. Escreve notas com commit, bibliotecas, runner e hashes.
2. Consulta se a tag já possui release.
3. Se existe, atualiza os assets com --clobber, move a referência Git da tag ao commit compilado e atualiza as notas.
4. Caso contrário, cria a release apontando ao commit.
5. Confere erros e registra a URL no resumo da execução.

A release PIX recebe ZIP, hash do ZIP, EXE e hash do EXE. A cliente recebe seu ZIP e hash. O token usado é o github.token temporário do job.

As tags atuais são móveis e os uploads substituem os assets anteriores. Portanto, nome de tag igual não prova arquivo igual. Para auditoria, guarde SHA completo do commit, ID da execução, BUILD-INFO e SHA-256 do arquivo. O campo target_commitish da API de release pode refletir um valor antigo; confira a referência Git real da tag.

Essa publicação não é atômica entre todos os assets. Durante um upload, arquivos e notas podem estar em transição. Considere a entrega pronta somente após o job terminar e a verificação de hashes/origem. Uma falha de publicação requer inspeção, não redistribuição automática de qualquer arquivo encontrado.

Os EXEs deste fluxo não têm assinatura digital comercial. TURBORAMA_RELEASE_HARDENING não habilita, por si só, a exigência de componentes PIX assinados nem fornece certificado. Não use esta receita como substituta do orquestrador comercial já existente.

## 6. Reprodução local, somente para quem já tem ambiente

Este trecho é opcional e não foi executado durante o handoff. Para o usuário sem compilador ou com internet lenta, prefira Actions. São necessários Visual Studio 2022 C++, toolset v143, SDK compatível, CMake, PowerShell 5.1, fonte completa do projeto, tema e bibliotecas fixadas. Haverá uso relevante de disco/RAM.

Use uma pasta de trabalho por branch e uma pasta de build por variante. Confirme a origem antes:

~~~powershell
git status --short
git branch --show-current
git rev-parse HEAD
git -C TurboramaEmulationStation/win32-libs rev-parse HEAD
~~~

Pare se houver alterações de outra pessoa ou bibliotecas diferentes. O CMake dá preferência a ../batocera-emulationstation-win32-dependencies quando essa pasta existe; por isso, conferir apenas win32-libs não basta no ambiente local. Confira o caminho anunciado por “Default libraries path set to” e o SHA Git desse diretório efetivamente escolhido. Não execute reset --hard, checkout de descarte nem limpeza recursiva como parte deste roteiro. Não “corrija” diretório seguro do Git desabilitando segurança globalmente.

Dentro de TurboramaEmulationStation da PIX, configure em uma linha:

~~~powershell
cmake -S . -B build-handoff-pix -G "Visual Studio 17 2022" -A x64 -T "v143,host=x64" -DRETROBAT=OFF -DBATOCERA=OFF -DTURBORAMA_RELEASE_HARDENING=ON -DCMAKE_FIND_PACKAGE_PREFER_CONFIG=FALSE -DCMAKE_SYSTEM_VERSION=10.0.26100.0
cmake --build build-handoff-pix --config Release --target emulationstation --parallel 1
~~~

No checkout cliente, use:

~~~powershell
cmake -S . -B build-handoff-cliente -G "Visual Studio 17 2022" -A x64 -T "v143,host=x64" -DRETROBAT=OFF -DBATOCERA=OFF -DTURBORAMA_ENABLE_COMMERCIAL_SERVICES=OFF -DTURBORAMA_RELEASE_HARDENING=ON -DCMAKE_FIND_PACKAGE_PREFER_CONFIG=FALSE -DCMAKE_SYSTEM_VERSION=10.0.26100.0
cmake --build build-handoff-cliente --config Release --target emulationstation --parallel 1
~~~

Após cada comando, confira $LASTEXITCODE antes de prosseguir. A saída esperada neste projeto é bin/x64/Release/emulationstation.exe. Construir não monta todas as DLLs automaticamente: reproduza a montagem/testes do workflow ou use o pacote publicado.

Mesmo com diretórios -B diferentes, o destino bin/x64/Release é compartilhado dentro de um mesmo checkout. Por isso é importante ter checkouts separados, não apenas dois nomes de build.

## 7. Critério de entrega

Uma entrega está tecnicamente rastreável quando a branch e o commit estão registrados, todos os gates obrigatórios passaram, a tag real corresponde ao commit, o asset está concluído, o hash coincide e a instalação mantém as dependências corretas. Isso ainda não dispensa o teste manual em hardware real descrito no próximo capítulo.
