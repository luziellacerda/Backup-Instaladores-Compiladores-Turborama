# 03 — Cliente sem serviços: construção e preservação

[Início](README.md) · [Histórico](01-HISTORICO-E-ESTADO.md) · [Código linha a linha](anexos/linhas/README.md)

## 1. Identidade e objetivo

Branch CLIENTE-SEM-SERVICOS-20260904-1818. Fonte documentada: 947dad45f5e9cd556cce6f15045a5dd6119bdf95. Base comum: 76b214874973fe24017823401216896f3d7a6f40. Todos os números e links deste capítulo apontam para a revisão cliente congelada, não para uma branch móvel.

O cliente recebe o frontend normal sem o ecossistema comercial de locadora. A separação inclui PIX/compra/pagamento, crédito/jogadores vinculados ao saldo, contabilidade, débito por tempo, HUD/aviso de crédito, corte de jogo por saldo, inicialização/supervisão do agente e atalhos comerciais.

“Sem serviços” aqui não significa apagar qualquer função cujo nome contenha serviço. Scraper, atualização, rede, API HTTP, áudio, tema, jogos, metadados, controles e salvamentos continuam. Os componentes HTTP permanecem na [lista comum de fontes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/CMakeLists.txt#L50-L61).

## 2. As três camadas da separação

1. O CMake não adiciona as implementações comerciais ao alvo quando a opção está OFF.
2. A macro privada TURBORAMA_NO_COMMERCIAL_SERVICES retira referências comerciais dos arquivos compartilhados do aplicativo.
3. O workflow verifica o projeto gerado e o executável antes de publicar.

Não é apenas esconder botões na tela. Também não é apagar fisicamente todo o código comercial do repositório: ele continua disponível para a variante que o utiliza.

| Área | Resultado no cliente | Limite importante |
|---|---|---|
| PIX inicial e compra via SELECT | Removidos | SELECT volta aos mapeamentos normais |
| Créditos/jogadores/contabilidade | Sem esses fluxos compilados | Não equivale a apagar dados de uma instalação antiga |
| Cronômetro comercial | Sem tick, débito, HUD, aviso ou corte por crédito | Temporização normal de animação/jogos não foi removida |
| Agente PIX | Sem inicialização/watchdog no cliente | Não desinstala automaticamente um serviço externo já instalado |
| F10/F12 comerciais | Não alteram saldo | Não são teclas globalmente bloqueadas no Windows |
| F11 | Ações gerais autenticadas | Não abre mais crédito/locadora |
| START | Menu normal autenticado | Usa MainMenuAuth, não CreditManager |
| Jogos e otimizações | Preservados | Ver testes e limites do capítulo de memória |
| Nova barreira de áudio PIX | Ausente | Não confundir ciclo de áudio herdado com o novo AudioHandoff |

## 3. Evolução que precisa ser mantida

bc51e72 criou o recorte sem serviços, com opção de build e guardas. 12b8e75 separou os workflows. 947dad4 corrigiu o excesso de remoção do primeiro recorte, conservou ajustes não comerciais, desacoplou hardening, restringiu a macro e criou autenticação administrativa independente. O resultado documentado é 947dad4, não o primeiro recorte isolado.

No estado final, o workflow PIX herdado no checkout cliente é igual ao da base comum; a receita cliente está em outro arquivo. Os filtros estão nas [linhas 1–10 do workflow cliente](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/.github/workflows/compilar-cliente-sem-servicos-windows.yml#L1-L10). Documentação em docs/handoff-turborama não dispara esse build.

## 4. CMake: arquivo por arquivo

### CMakeLists.txt principal

Nas [linhas 19–29](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/CMakeLists.txt#L19-L29), TURBORAMA_ENABLE_COMMERCIAL_SERVICES é declarada com padrão OFF e o perfil é anunciado.

Nas [linhas 31–40](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/CMakeLists.txt#L31-L40), o hardening geral fica separado do comercial. Retirar PIX não deve retirar otimização, proteção de pilha e opções de ligação. As [linhas 44–50](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/CMakeLists.txt#L44-L50) rejeitam configurações contraditórias que desligam serviços e ainda solicitam opções de assinatura/pacote PIX.

Nas [linhas 294–306](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/CMakeLists.txt#L294-L306), o perfil Release geral seleciona otimização, LTCG e flags de proteção. São flags de build, não uma assinatura digital e não uma prova de invulnerabilidade.

### es-app/CMakeLists.txt

MainMenuAuth entra na lista comum, nas [linhas 144–174](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/CMakeLists.txt#L144-L174). Só com serviços ON entram as fontes/cabeçalhos comerciais: CreditManager, CreditWarningOverlay, PixBridge, PixAgentManager, PixBinaryTrust, GuiCreditPlayerSelect, GuiCreditOperatorPanel, GuiPixPurchase e GuiPixOwnerSettings. A [lista condicional está nas linhas 248–271](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/CMakeLists.txt#L248-L271); nem todos os nomes correspondem a uma implementação .cpp independente.

Nas [linhas 345–352](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/CMakeLists.txt#L345-L352), a macro TURBORAMA_NO_COMMERCIAL_SERVICES é PRIVATE no alvo emulationstation. Ela não deve se espalhar para es-core e desligar comportamento genérico de renderização/vídeo/recursos.

### Scripts locais

COMPILAR-WINDOWS.bat configura x64, serviços OFF e hardening ON nas [linhas 96–119](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/COMPILAR-WINDOWS.bat#L96-L119), com explicação do perfil nas linhas 134–136. tools/compilar.ps1 encaminha as opções nas [linhas 389–402](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/tools/compilar.ps1#L389-L402).

O usuário do pacote não precisa de compilador. Precisa do ambiente Windows x64 e das dependências da entrega; esse EXE não é independente do sistema operacional. O tutorial de construção está no capítulo 05.

## 5. main.cpp: entrada, comandos e encerramento

Paths::setExePath ocorre antes dos retornos dos autotestes nas [linhas 487–493](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/main.cpp#L487-L493). No cliente, a checagem comercial de confiança fica fora desse caminho; permanecem decorações, --no-commercial-services-self-test e --main-menu-auth-self-test nas [linhas 493–542](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/main.cpp#L493-L542).

Oito comandos legados PIX/crédito são rejeitados com código 34 antes do parser normal, nas [linhas 620–645](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/main.cpp#L620-L645). Isso evita que uma chamada comercial antiga seja tratada silenciosamente como inicialização normal.

Pasta de usuário, logger, locale e arranque normal permanecem. PixAgentManager só inicia no bloco comercial; o cliente registra seu perfil nas [linhas 701–727](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/main.cpp#L701-L727).

No encerramento, apenas o flush comercial é retirado. WatchersManager, ThreadedHasher, ThreadedScraper, ApiSystem, save state, coleções, sistemas e scripts continuam nas [linhas 1105–1129](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/main.cpp#L1105-L1129). Remover cobrança não é licença para saltar a limpeza normal.

## 6. Menus e atalhos, ação por ação

### START e troca de senha

ViewController intercepta START e pede acesso autenticado nas [linhas 914–919](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/ViewController.cpp#L914-L919).

GuiMenu encaminha verificação/gravação ao CreditManager no comercial e a MainMenuAuth no cliente, nas [linhas 338–363](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L338-L363). A troca exige pelo menos oito caracteres, confirmação igual e gravação bem-sucedida, nas [linhas 365–403](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L365-L403).

Quando a credencial ainda é a inicial padrão do código, a interface exige troca antes de liberar, nas [linhas 405–420](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L405-L420). O fluxo evita GuiMenu duplicado e só o empilha depois da autenticação/troca, nas [linhas 424–455](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L424-L455). A opção de trocar a senha START permanece no menu de desenvolvedor, nas [linhas 1367–1372](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L1367-L1372).

### F11 e TURBO SISTEMA

F11 chama requestTurboSystemMenuAccess_static nas [linhas 1018–1024 de main.cpp](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/main.cpp#L1018-L1024). A fachada pede senha e força a troca da credencial inicial nas [linhas 457–489 de GuiMenu.cpp](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L457-L489). O abridor direto é privado; só a fachada autenticada é pública, conforme [GuiMenu.h, linhas 43–61](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/guis/GuiMenu.h#L43-L61).

Depois da senha, TURBO SISTEMA oferece Explorer, troca de usuário por tsdiscon.exe com fallback para logoff e encerramento do frontend mantendo o PC ligado. Ações e confirmações estão nas [linhas 491–561](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L491-L561). São ações normais do sistema, não crédito.

### O que desaparece e o que fica

PIX do proprietário/contabilidade são condicionais; scraper, updates, sistema e desligamento ficam nas [linhas 221–249](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L221-L249). Crédito/operador/contabilidade ficam guardados nas [linhas 564–887](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L564-L887).

No acesso rápido, a contabilidade é condicional e música permanece nas [linhas 5312–5335](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L5312-L5335). SELECT deixa de ser capturado pela compra PIX e segue os mapeamentos normais nas [linhas 921–955 de ViewController.cpp](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/ViewController.cpp#L921-L955). F10/F12 só têm tratamento comercial no [bloco de main.cpp, linhas 969–1017](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/main.cpp#L969-L1017).

## 7. MainMenuAuth: autenticação sem locadora

A interface independente está em [MainMenuAuth.h, linhas 7–17](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/MainMenuAuth.h#L7-L17).

No Windows, novas senhas usam PBKDF2-HMAC-SHA-256 via BCrypt: 210.000 iterações, salt aleatório de 16 bytes e digest de 32 bytes. Há comparação de percurso constante no código. Constantes/validação/comparação estão nas [linhas 32–125](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/MainMenuAuth.cpp#L32-L125); geração e derivação nas [linhas 127–165](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/MainMenuAuth.cpp#L127-L165).

O parser aceita iterações entre 100.000 e 2.000.000, salt entre 16 e 64 bytes e digest de 32 bytes, nas [linhas 175–215](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/MainMenuAuth.cpp#L175-L215). O formato ilustrativo, sem valores de produção, é:

~~~text
schemaVersion=1
passwordHash=pbkdf2-sha256$210000$<salt-hex>$<digest-hex>
~~~

O arquivo fica no perfil .emulationstation/main_menu_auth.cfg. A leitura usa caminho largo no MSVC para nomes Unicode e limita tamanho a 64 KiB, 512 linhas e 4.096 bytes por linha, nas [linhas 281–340](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/MainMenuAuth.cpp#L281-L340).

A gravação usa temporário exclusivo, flush e substituição. No Windows, o caminho usa CreateFileW, verificação de reparse, FlushFileBuffers e MoveFileExW; no POSIX, O_EXCL/O_NOFOLLOW, permissões 0600, fsync e rename. Fonte: [linhas 349–458](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/MainMenuAuth.cpp#L349-L458). Isso descreve a implementação, não uma certificação de resistência a todos os cenários de concorrência.

O parser do arquivo novo rejeita versão/chave desconhecida, duplicata e hash inválido nas [linhas 460–510](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/MainMenuAuth.cpp#L460-L510). Não use documentação para redefinir credenciais reais nem confunda esse arquivo com saldo/contabilidade.

### Migração e limites

Para conservar acesso administrativo em uma instalação existente, o cliente lê somente adminPasswordHash ou adminPassword do antigo arcade_credit.cfg. Não carrega saldo, PIX, tempo, jogadores nem daemon. Caminhos/intenção: [linhas 337–347](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/MainMenuAuth.cpp#L337-L347); parser legado: [linhas 512–560](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/MainMenuAuth.cpp#L512-L560).

A precedência é: arquivo novo válido; credencial administrativa legada válida; credencial inicial apenas quando ambos os arquivos não existem. Arquivo presente e malformado produz estado inválido, sem retornar silenciosamente ao padrão, nas [linhas 563–595](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/MainMenuAuth.cpp#L563-L595).

Após autenticar uma credencial legada, o código tenta gravar o formato novo. Se a migração falhar, registra erro mas mantém aquela autenticação válida e tenta novamente no próximo acesso, nas [linhas 598–614](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/MainMenuAuth.cpp#L598-L614). Isso não é migração de crédito.

Limitações que devem ficar explícitas:

- Espaços externos da senha são removidos; o mínimo de oito caracteres vale para senha nova.
- Alvos não Windows criam legacy-md5 sem salt, inferior ao PBKDF2 Windows. Veja [linhas 218–254](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/MainMenuAuth.cpp#L218-L254). A entrega deste handoff é Windows, não homologação dessa alternativa POSIX.
- O autoteste das [linhas 638–652](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/MainMenuAuth.cpp#L638-L652) cobre hash, senha errada e hash inválido; não cobre sozinho arquivos, migração, Unicode ou telas.
- Depois da migração, a senha cliente vive em main_menu_auth.cfg e não é sincronizada automaticamente entre perfis.

## 8. FileData: lançamento e retorno sem crédito

O cliente não compila o bloqueio por falta de crédito nas [linhas 1219–1231](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/FileData.cpp#L1219-L1231), a sessão/débito/avisos/corte nas [linhas 1249–1333](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/FileData.cpp#L1249-L1333), nem a mensagem de tempo esgotado nas [linhas 1389–1396](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/FileData.cpp#L1389-L1396). Os overlays comerciais também são condicionados nas [linhas 64–361](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/FileData.cpp#L64-L361).

O caminho comum conserva o comando, AudioManager/VolumeControl deinit, janela e evento game-start nas [linhas 1233–1247](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/FileData.cpp#L1233-L1247). O processo externo permanece nas [linhas 1275–1328](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/FileData.cpp#L1275-L1328).

No retorno, continuam cache, save state, p2k, evento game-end, restauração da janela/áudio, metadados e música, nas [linhas 1335–1401](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/FileData.cpp#L1335-L1401). Não remover esse trecho junto do guard comercial.

## 9. Memória e vídeos preservados

As implementações vieram da base comum; 947dad4 preserva o comportamento não comercial e os controles correspondentes. Evidências na fonte cliente:

| Mecanismo | Referência |
|---|---|
| Cache/TTL de caminho de vídeo | [FileData.cpp 48–56](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/FileData.cpp#L48-L56), [719–805](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/FileData.cpp#L719-L805) |
| Sincronização do carrossel | [SystemView.cpp 2174–2306](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L2174-L2306) |
| Pool de componentes de vídeo | [CarouselComponent.cpp 893–963](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-core/src/components/CarouselComponent.cpp#L893-L963) |
| Fila de liberação VLC limitada | [VideoVlcComponent.cpp 42–135](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L42-L135) |
| Orçamento e poda de buffers | [VideoVlcComponent.cpp 248–331](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.cpp#L248-L331) |
| Padrões Windows de configuração | [Settings.cpp 227–261](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-core/src/Settings.cpp#L227-L261) |
| Controles mantidos no menu | [GuiMenu.cpp 1374–1400](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp#L1374-L1400) |

Os padrões incluem MaxVideoRAM 768, MaxAsyncQueue 12, MaxConcurrentVideos 3 e EnforceVideoLimit true no Windows. São padrões, não promessa de uso total de RAM limitado a esse número nem três players universais em todos os contextos. Leia os buckets, buffers e diferenças entre RAM/VRAM no [capítulo 02A](02A-MEMORIA-E-VIDEOS.md).

## 10. Diferença comprovada de áudio

O cliente conserva a fila de liberação VLC herdada, mas depois de AudioManager::deinit, VolumeControl::deinit e window->deinit segue para game-start sem chamar a nova barreira. Veja [FileData.cpp 1233–1247](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/FileData.cpp#L1233-L1247). A interface do player não tem waitForAudioRelease nas [linhas 89–104](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-core/src/components/VideoVlcComponent.h#L89-L104).

O commit PIX 7de017c, posterior à separação das branches, acrescentou a operação de espera, a condição de fila drenada e a contagem do release síncrono de overflow. Também trouxe o reparador e dois testes de áudio. Nada disso entra automaticamente no cliente 947dad4.

Assim, não é correto declarar paridade: o cliente não tem essa barreira, o reparador distribuído nem os dois testes. Seu deinit ajuda a encerrar os subsistemas, mas não comprova resolução do silêncio em todos os emuladores.

Uma eventual portabilidade futura deve transportar somente a mudança comum de áudio e adaptar os testes, preservando a exclusão de CreditManager/PixBridge. Não fazer merge integral da PIX sobre o cliente. Essa pendência não foi implementada pelo handoff; a explicação detalhada do código PIX está no [capítulo 04](04-VERSAO-PIX-E-AUDIO.md).

## 11. SystemView e ViewController: estado, update e render

### SystemView

As referências a PixBridge ficam sob condição de compilação no [header, linhas 15–18](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/SystemView.h#L15-L18). Métodos/estado de oferta/QR ficam na [região 119–158](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/SystemView.h#L119-L158). Dados e sincronização do carrossel permanecem nas [linhas 31–39](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/SystemView.h#L31-L39) e [160–170](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/SystemView.h#L160-L170).

Na implementação, os blocos condicionais alcançam:

| Parte | Linhas |
|---|---|
| Componentes/inicialização PIX | [91–155](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L91-L155) |
| Update comercial | [817–820](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L817-L820) |
| Oferta/QR | [832–1134](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L832-L1134) |
| Render comercial final | [1458–1462](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L1458-L1462) |
| Nova tentativa de oferta | [2031–2039](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/SystemView.cpp#L2031-L2039) |

O restante da tela continua no cliente. Deixar uma declaração fora do guarda enquanto seu tipo comercial é excluído do alvo pode quebrar a compilação; deixar um update fora do guarda pode recolocar comportamento indesejado.

### ViewController

O controlador condiciona includes comerciais nas [linhas 35–41](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/ViewController.cpp#L35-L41), estado do HUD no [header 141–146](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/ViewController.h#L141-L146) e inicialização nas [linhas 89–104](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/ViewController.cpp#L89-L104).

Tick de crédito, poll PIX, watchdog, alerta e atualização do HUD ficam fora do cliente nas [linhas 969–1017](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/ViewController.cpp#L969-L1017); desenho comercial nas [linhas 1066–1138](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/views/ViewController.cpp#L1066-L1138). Transições, fade e views normais permanecem.

## 12. Workflow, pacote e release cliente

O arquivo dedicado fixa branch, bibliotecas, nome de pacote e tag. A [configuração/construção nas linhas 84–132](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/.github/workflows/compilar-cliente-sem-servicos-windows.yml#L84-L132) usa serviços OFF e hardening ON. A [validação específica 134–145](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/.github/workflows/compilar-cliente-sem-servicos-windows.yml#L134-L145) ocorre antes da montagem.

O pacote contém EXE, bibliotecas de imagem/rede/VLC/SDL, plugins, recursos, vídeos e runtimes. Confere x64 e plugins nas [linhas 227–263](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/.github/workflows/compilar-cliente-sem-servicos-windows.yml#L227-L263), gera BUILD-INFO e TESTAR-ISOLADO nas [linhas 265–289](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/.github/workflows/compilar-cliente-sem-servicos-windows.yml#L265-L289), roda os smokes nas [linhas 291–309](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/.github/workflows/compilar-cliente-sem-servicos-windows.yml#L291-L309) e gera manifesto/hashes nas [linhas 310–339](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/.github/workflows/compilar-cliente-sem-servicos-windows.yml#L310-L339).

A publicação das [linhas 341–406](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/.github/workflows/compilar-cliente-sem-servicos-windows.yml#L341-L406) usa tag/assets próprios e latest=false. Assim, não tenta tomar a indicação Latest da PIX. A tag e os assets são atualizáveis: guarde SHA do código, ID do run e SHA-256 do arquivo.

O [run 33928366809](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/actions/runs/33928366809) aprovou a entrega 947dad4. A release registra 340 arquivos no pacote, sem assinatura digital. Datas e hashes exatos estão no capítulo 01; não usar o run anterior 33922740461 como prova de teste do código posterior.

## 13. Test-NoCommercialServicesBuild.ps1, bloco por bloco

| Linhas do teste | O que verifica |
|---|---|
| [1–78](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/tools/tests/Test-NoCommercialServicesBuild.ps1#L1-L78) | CMakeCache com serviços OFF/hardening ON, macro no app e não no core, fontes comerciais ausentes e comuns presentes |
| [80–142](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/tools/tests/Test-NoCommercialServicesBuild.ps1#L80-L142) | Assinaturas previstas das otimizações e propriedades de Release |
| [144–192](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/tools/tests/Test-NoCommercialServicesBuild.ps1#L144-L192) | Execução de autotestes do perfil/autenticação e rejeição de oito comandos comerciais com código 34 |
| [194–277](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/tools/tests/Test-NoCommercialServicesBuild.ps1#L194-L277) | Leitura do EXE em blocos de 4 MiB para examinar conteúdo |
| [279–291](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/tools/tests/Test-NoCommercialServicesBuild.ps1#L279-L291) | Rejeição dos marcadores comerciais especificados |
| [293–328](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/tools/tests/Test-NoCommercialServicesBuild.ps1#L293-L328) | Exigência dos marcadores de otimização e autenticação previstos |

Texto encontrado no binário não é prova formal de todo comportamento. A confiança vem da combinação de seleção de fontes, guardas, testes executáveis e exame do pacote. Esses critérios também não medem FPS, RAM de todos os cenários ou som de jogo real.

## 14. Inventário dos 16 arquivos alterados

Cada link abaixo abre o diff literal numerado da base 76b2148 até 947dad4. A interpretação está nas seções anteriores.

| Arquivo e anexo | Responsabilidade |
|---|---|
| [.github/workflows/compilar-cliente-sem-servicos-windows.yml](anexos/linhas/02-cliente-01-compilar-cliente-sem-servicos-windows-yml.md) | Workflow dedicado do cliente: construção, validação, pacote e release. |
| [TurboramaEmulationStation/CMakeLists.txt](anexos/linhas/02-cliente-02-CMakeLists-txt.md) | Seleção de fontes, dependências, recursos e opções de compilação. Atenção ao CMake da raiz do projeto versus es-app. |
| [TurboramaEmulationStation/COMPILAR-WINDOWS.bat](anexos/linhas/02-cliente-03-COMPILAR-WINDOWS-bat.md) | Atalho local de construção: encaminha os parâmetros da variante. Não é o workflow GitHub. |
| [TurboramaEmulationStation/es-app/CMakeLists.txt](anexos/linhas/02-cliente-04-CMakeLists-txt.md) | Seleção de fontes, dependências, recursos e opções de compilação. Atenção ao CMake da raiz do projeto versus es-app. |
| [TurboramaEmulationStation/es-app/src/FileData.cpp](anexos/linhas/02-cliente-05-FileData-cpp.md) | Dados e metadados do jogo; cache de mídia e sequência de preparação, execução e retorno do emulador. Leia os capítulos de memória e da variante correspondente. |
| [TurboramaEmulationStation/es-app/src/MainMenuAuth.cpp](anexos/linhas/02-cliente-06-MainMenuAuth-cpp.md) | Autenticação administrativa do menu separada do gerenciador de crédito. Não é pagamento nem cronômetro. |
| [TurboramaEmulationStation/es-app/src/MainMenuAuth.h](anexos/linhas/02-cliente-07-MainMenuAuth-h.md) | Contrato mínimo da autenticação não comercial do menu. |
| [TurboramaEmulationStation/es-app/src/guis/GuiMenu.cpp](anexos/linhas/02-cliente-08-GuiMenu-cpp.md) | Montagem e ações dos menus; filtros de compilação retiram entradas comerciais no cliente sem apagar opções de aparência/desempenho. |
| [TurboramaEmulationStation/es-app/src/guis/GuiMenu.h](anexos/linhas/02-cliente-09-GuiMenu-h.md) | Declarações dos menus acompanhando a presença ou ausência de serviços. |
| [TurboramaEmulationStation/es-app/src/main.cpp](anexos/linhas/02-cliente-10-main-cpp.md) | Ponto de entrada, criação da janela, inicialização do tema, loop e encerramento. Serviços comerciais ficam condicionados na versão cliente. |
| [TurboramaEmulationStation/es-app/src/views/SystemView.cpp](anexos/linhas/02-cliente-11-SystemView-cpp.md) | Tela de sistemas: seleção, carrossel, ciclo de vida dos vídeos, atualização visual e, na PIX, integrações de serviços. |
| [TurboramaEmulationStation/es-app/src/views/SystemView.h](anexos/linhas/02-cliente-12-SystemView-h.md) | Campos e métodos da tela de sistemas. As condições de compilação precisam acompanhar os respectivos usos no .cpp. |
| [TurboramaEmulationStation/es-app/src/views/ViewController.cpp](anexos/linhas/02-cliente-13-ViewController-cpp.md) | Navegação central e acesso ao menu; encaminha autenticação à implementação da variante. |
| [TurboramaEmulationStation/es-app/src/views/ViewController.h](anexos/linhas/02-cliente-14-ViewController-h.md) | Declarações do controlador que acompanham os caminhos de autenticação. |
| [TurboramaEmulationStation/tools/compilar.ps1](anexos/linhas/02-cliente-15-compilar-ps1.md) | Orquestração local de compilação; os valores da variante devem coincidir com o workflow. |
| [TurboramaEmulationStation/tools/tests/Test-NoCommercialServicesBuild.ps1](anexos/linhas/02-cliente-16-Test-NoCommercialServicesBuild-ps1.md) | Teste de ausência comercial e preservação dos invariantes. |

## 15. Evidência, riscos e próximos testes

O run da fonte 947dad4 concluiu com sucesso, incluindo a validação do perfil, construção, verificação x64/plugins, smokes, hashes e publicação. O handoff não repetiu esse build pesado.

Ainda exigem ensaio específico: GUI START/F11; matriz de arquivos administrativos novos/legados/malformados; perfil Windows Unicode; RetroArch e emuladores standalone reais; restauração de janela/áudio/metadados; corrida de áudio com VLC; máquina limpa sem Visual Studio; nova barreira de áudio caso seja portada. A presença de um autoteste não deve ser apresentada como execução de toda essa matriz.

Os limites importantes são: ausência do AudioHandoff PIX; EXE sem assinatura; tag atualizável; troca obrigatória da credencial inicial; alternativa MD5 não Windows inferior; migração administrativa sem sincronização automática entre perfis; autoteste de hash sem cobertura completa de arquivo/UI; serviços gerais preservados por escopo.

Em resumo: crédito, pagamento, PIX, contabilidade e tempo comercial não participam do executável cliente desse perfil. Jogos, configurações, áudio básico, limpeza normal e otimizações permanecem. START/F11 usam autenticação independente. Só se deve declarar paridade de áudio com PIX depois de uma alteração deliberada e dos respectivos testes.
