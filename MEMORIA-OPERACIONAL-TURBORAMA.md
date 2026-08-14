# MemÃ³ria operacional do TurboRama

Atualizado em: 2026-08-13 — Windows R4 Admin: Arcade/Arkade cancelado para o gabinete atual

Esta Ã© a referÃªncia de continuidade do projeto. NÃ£o contÃ©m credenciais, senhas, chaves privadas ou
dados de clientes.

## REGRA PRINCIPAL DE TODO HANDOFF — SOMENTE DADOS REAIS

Esta regra tem prioridade sobre qualquer instrução anterior ou posterior:

- não mascarar erro, falha, ausência de dado, resultado incompleto ou etapa não executada;
- não inventar, completar, estimar nem supor usuário, SID, conta, caminho, arquivo, porta, serviço,
  configuração, versão, status, resultado, credencial ou comportamento;
- não tratar informação histórica como estado atual sem conferir novamente no ambiente real;
- não declarar `APROVADO`, `FUNCIONANDO`, `CONCLUÍDO`, `SAUDÁVEL` ou equivalente sem evidência real
  registrada na mesma rodada;
- diferenciar sempre: `COMPROVADO`, `NÃO COMPROVADO`, `NÃO EXECUTADO` e `BLOQUEADO`;
- quando faltar dado real, parar antes de alterar e devolver exatamente qual dado ou evidência falta;
- nunca criar valor padrão silencioso para contornar configuração ausente;
- toda alteração deve partir de inventário real, preservar backup verificável e ser seguida de teste
  real proporcional ao risco;
- o retorno do handoff deve registrar também o que falhou e o que permaneceu sem teste;
- sanitizar segredos significa remover apenas o valor secreto; não significa esconder erros, mudanças,
  dependências, resultados ou evidências técnicas necessárias;
- afirmações do proprietário são contexto autorizado, mas dados técnicos detectáveis devem ser
  conferidos no equipamento antes de serem usados como fato operacional;
- em caso de conflito entre memória antiga e evidência atual, prevalece a evidência atual, e a
  divergência deve ser registrada sem maquiagem.

Nenhum handoff pode avançar usando uma identidade de quiosque presumida. O usuário real, o SID, a
sessão, o AutoLogon e o conteúdo/configuração aplicável do `turborama.json` devem ser descobertos e
comparados no equipamento. Se não coincidirem ou não puderem ser lidos, a rodada deve parar sem
consumir código de ativação, sem gravar configuração e sem declarar sucesso.

## Regra autoritativa Windows atual — NÃO existe Arcade/Arkade para este gabinete

Atualização de 2026-08-13, definida pelo proprietário e confirmada por diagnóstico somente leitura:

- para o gabinete Windows atual, a identidade operacional válida é `Admin`;
- não usar `Arcade`, `Arkade`, `arkae`, `TurboRama Kiosk` ou qualquer variação como conta operacional
  deste gabinete, salvo se uma rodada futura fizer inventário real novo e registrar evidência contrária;
- todos os trechos históricos desta memória que mencionem Arcade/Arkade como identidade do gabinete
  atual estão cancelados para a execução corrente;
- `C:\TurboRama\Config\turborama.json` e o AutoLogon real apontam para `Admin`;
- o diagnóstico somente leitura do instalador atual retornou código `0` para a identidade instalada;
- o instalador PIX deve aceitar `Admin` quando JSON, Winlogon e SID coincidirem e a conta local estiver
  habilitada;
- o instalador PIX deve continuar bloqueando conta ausente, desabilitada, AutoLogon divergente,
  `turborama.json` inválido ou SID divergente;
- o kiosk base, Launcher, ROMs, temas, cache, créditos e referência funcional continuam imutáveis;
- a entrega em trabalho é somente sobreposição PIX: `emulationstation.exe`,
  `CONFIGURAR-USER-TOKEN-PIX.exe`, `CONFIGURAR-ACCESS-TOKEN-PIX.exe` e `pix-agent`;
- o sistema local não pode ficar preso ao servidor on-line: licenciamento on-line autoriza/gerencia a
  licença, mas o EmulationStation e os preços locais continuam sob controle local. Sem internet, apenas
  novas cobranças Mercado Pago podem ficar indisponíveis.

## IdentificaÃ§Ã£o dos ambientes

### Quando o usuÃ¡rio disser â€œestou no servidor Windowsâ€

Trabalhar no computador/gabinete Windows usado para desenvolvimento, compilaÃ§Ã£o e testes do
TurboRama/EmulationStation. Usar comandos e caminhos Windows. NÃ£o enviar comandos Linux.

ReferÃªncias locais:

- workspace: `C:\Users\Admin\Documents\Codex\2026-08-04\c-users-admin-documents-codex-2026`;
- fonte final em trabalho: `TurboramaEmulationStation-repo-QR-FINAL`;
- clone limpo do servidor PIX: `Servidor-pix-publicar`;
- instalaÃ§Ã£o de teste citada: `D:\emulationstation`;
- referÃªncia funcional que nÃ£o deve ser modificada sem ordem explÃ­cita:
  `D:\HANDOFF-TURBORAMA-COMPLETO\GROK-HANDOFF-TURBORAMA-COMPLETO`.

No Windows ficam fontes, compiladores, instaladores, testes e geraÃ§Ã£o dos pacotes.

### Quando o usuÃ¡rio disser â€œestou no servidor Linuxâ€

Trabalhar somente no servidor Linux que jÃ¡ mantÃ©m um site e um Cloudflare Tunnel funcionando 24
horas. Usar comandos Linux. NÃ£o enviar caminhos ou comandos Windows.

Regras obrigatÃ³rias:

- nÃ£o substituir nem apagar o site existente;
- nÃ£o substituir a configuraÃ§Ã£o atual do Cloudflare Tunnel;
- fazer inventÃ¡rio e backup antes de alteraÃ§Ãµes;
- instalar o PIX como serviÃ§o separado em `127.0.0.1:5187`;
- usar posteriormente um hostname Cloudflare separado;
- preferir baixar somente o pacote pronto, sem fontes ou compiladores;
- nunca pedir senha, token ou chave pelo chat.

Regra de operaÃ§Ã£o definida pelo proprietÃ¡rio em 2026-08-07:

- nÃ£o enviar comandos Linux diretamente ao usuÃ¡rio;
- cada ida ao servidor deve usar um Ãºnico arquivo de handoff completo e organizado;
- o handoff deve reunir todo o trabalho da rodada, validaÃ§Ãµes, backup, rollback, critÃ©rios de parada e
  nome do arquivo de retorno;
- o usuÃ¡rio somente transporta/anexa o handoff Ã  conversa aberta no Linux;
- depois, o retorno sanitizado volta para a conversa Windows;
- essa organizaÃ§Ã£o Ã© obrigatÃ³ria para evitar comandos fora de ordem e erros entre ambientes.

## Estado funcional aprovado no Windows

Preservar estes comportamentos antes de novas mudanÃ§as:

- EmulationStation lÃª o cadastro PIX escrito pelo configurador;
- os programas PIX usam o usuÃ¡rio correto do quiosque, sem depender de conta inexistente;
- QR Code automÃ¡tico na tela principal com posiÃ§Ã£o e tamanho aprovados;
- mensagem de preÃ§o/tempo junto ao QR Code;
- avisos de 15 e 5 minutos na camada superior, inclusive sobre jogos;
- `F10` adiciona somente crÃ©ditos avulsos;
- `F12` zera somente crÃ©ditos avulsos;
- `F10` e `F12` nÃ£o alteram linhas ou crÃ©ditos da locadora/PIX.

DiagnÃ³stico de continuidade realizado em 2026-08-07:

- o agente instalado em `D:\emulationstation\pix-agent\TurboRamaPixAgent.dll` Ã© o build anterior de
  2026-08-05, tamanho 359424 bytes, SHA-256
  `0fe3c83a1347a939513553cf7b6211a723570e2284c592b094d77e86e1275aae`;
- esse agente instalado Ã© anterior Ã  integraÃ§Ã£o com o servidor on-line;
- o agente novo foi recompilado somente no workspace, sem substituir a instalaÃ§Ã£o;
- build novo: 512000 bytes, SHA-256
  `06a9162eecca3f2b7cf5b69ae58df21eaf8b62dee35abc60933ff1aa8a5a6741`;
- compilaÃ§Ã£o nova: zero erros e zero avisos;
- `--self-test`: aprovado, incluindo contrato v2, identidade do daemon, QR, Mercado Pago, adaptador e
  servidor on-line;
- arquivos protegidos em `D:\emulationstation\.emulationstation\pix` recusaram leitura pelo usuÃ¡rio
  de manutenÃ§Ã£o/sandbox, comportamento esperado;
- nenhum binÃ¡rio da instalaÃ§Ã£o funcional foi substituÃ­do durante esse diagnÃ³stico.

## ProteÃ§Ã£o comercial construÃ­da

- assinatura Authenticode e manifesto SHA-256;
- licenÃ§a assinada e vinculada Ã  mÃ¡quina;
- TPM quando disponÃ­vel;
- `SOFTWARE_BOUND_ONLINE` para mÃ¡quinas sem TPM;
- DPAPI e AES-256-GCM para segredos locais;
- falha fechada quando integridade, licenÃ§a ou identidade falham;
- servidor on-line separado para autorizaÃ§Ã£o e cobranÃ§as;
- Access Token Mercado Pago permanece no servidor, nÃ£o no gabinete.

`USB_TOKEN_BOUND` estÃ¡ reservado e nÃ£o deve ser liberado sem token criptogrÃ¡fico real, KSP/SDK e
testes. Pendrive comum nÃ£o serve.

## RepositÃ³rios GitHub

Programa/EmulationStation:

- `https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama.git`;
- branch final: `PIX-FINAL+PROGRAMAS`.

Servidor PIX:

- repositÃ³rio privado: `https://github.com/luziellacerda/Servidor-pix`;
- branch: `main`;
- commits confirmados: `52c8847`, `7f0fcff`, `cee2127` e `3438b1d`;
- `cee2127` adicionou o pacote portÃ¡til;
- `3438b1d` corrigiu a leitura da resposta Mercado Pago, adicionou validaÃ§Ã£o segura do caixa e
  publicou o pacote da rodada 04;
- branch confirmada sincronizada com `origin/main`.

## Pacote inicial do servidor Linux â€” histÃ³rico

Arquivo no Windows:

`C:\Users\Admin\Documents\Codex\2026-08-04\c-users-admin-documents-codex-2026\Servidor-pix-publicar\outputs\TurboRamaPixOnlineServer-portable-52c8847.zip`

SHA-256:

`71af50e4d837d0e831277e4edd16fdedf1f242acb8f27b338b7ed17a275f994a`

ConteÃºdo: DLL, arquivos `.deps.json`, `.runtimeconfig.json`, endpoints estÃ¡ticos e checksums. NÃ£o
contÃ©m fonte, compilador, `.exe`, credencial, chave privada ou estado de cliente.

Esse pacote foi substituÃ­do no servidor pelo pacote da rodada 04 descrito ao final desta memÃ³ria.

ValidaÃ§Ãµes concluÃ­das:

- zero erros e zero avisos;
- autoteste aprovado;
- ativaÃ§Ã£o de uso Ãºnico e prova RSA-PSS;
- sessÃ£o exclusiva e tentativa de clone registrada;
- tabela de preÃ§os, idempotÃªncia, anti-replay e reautenticaÃ§Ã£o remota;
- nenhuma cobranÃ§a real durante o autoteste.

## Estado atual confirmado no Linux

Handoff completo arquivado em `STATUS-SERVIDOR-LINUX-2026-08-07.md`.

- servidor: Ubuntu 24.04.4 LTS, arquitetura `x86_64`;
- ASP.NET Core Runtime 8.0.29 instalado; SDK nÃ£o Ã© necessÃ¡rio em produÃ§Ã£o;
- pacote e checksums verificados;
- backup root-only criado antes da instalaÃ§Ã£o e manifesto validado;
- aplicaÃ§Ã£o instalada em `/opt/turborama-pix`;
- estado privado em `/var/lib/turborama-pix`;
- configuraÃ§Ã£o privada em `/etc/turborama-pix/server.env`, modo `0600`;
- usuÃ¡rio dedicado `turborama-pix` sem login;
- serviÃ§o `turborama-pix` ativo e habilitado no boot;
- escuta exclusiva em `127.0.0.1:5187`;
- endpoint local `/v1/health` respondeu `ready: true`;
- hostname pÃºblico `https://pix.lzgames.com.br/v1/health` respondeu `ready: true`;
- nginx e Cloudflare Tunnel existentes continuam ativos;
- sites anteriores foram revalidados e preservados;
- credencial Mercado Pago ainda nÃ£o configurada;
- nenhuma cobranÃ§a real executada.

## Retorno Linux â€” rodada 02

Arquivo completo arquivado em `RETORNO-LINUX-RODADA-02.md`.

- status: `BLOQUEADO_POR_FIREWALL`;
- `turborama-pix`, nginx, Cloudflare Tunnel e endpoints local/pÃºblico continuam saudÃ¡veis;
- nenhum serviÃ§o, firewall, configuraÃ§Ã£o, credencial ou dado de produÃ§Ã£o foi alterado;
- MariaDB escuta na porta `3306` em todas as interfaces IPv4;
- o UFW permite `3306` globalmente em IPv4 e IPv6;
- tambÃ©m existem permissÃµes globais redundantes para `13306`;
- `139`, `445` e `8083` aparecem escutando, mas sem permissÃ£o explÃ­cita no UFW e sujeitos Ã  polÃ­tica
  padrÃ£o de bloqueio;
- nenhum backup novo foi criado, pois a rodada parou antes de qualquer alteraÃ§Ã£o;
- credencial, cliente, licenÃ§a, preÃ§os, PDV e cÃ³digo de ativaÃ§Ã£o nÃ£o foram cadastrados.

Esclarecimento obrigatÃ³rio do proprietÃ¡rio: a porta `3302` pertence ao sistema da empresa. Ela deve
ser tratada como intocÃ¡vel: nÃ£o bloquear, remover, redirecionar, reutilizar ou modificar sem uma
autorizaÃ§Ã£o nova e explÃ­cita. O alerta da rodada 02 trata das portas `3306` e `13306`, nÃ£o da `3302`.
Antes de qualquer correÃ§Ã£o do UFW, identificar em modo somente leitura se o sistema da empresa ou
clientes externos legÃ­timos dependem de `3306` ou `13306`.

## Retorno Linux â€” rodada 03

Arquivo completo arquivado em `RETORNO-LINUX-RODADA-03.md`, SHA-256
`f7f8330cd51a603d56a3f9db068737185beb6525cfe27c9a08af3b68f33a50d9`.

- status: `AUDITORIA_SOMENTE_LEITURA_CONCLUIDA`;
- nenhum firewall, serviÃ§o, configuraÃ§Ã£o, credencial ou dado foi alterado;
- porta `3302`: nenhum listener, regra, processo ou referÃªncia local; a dependÃªncia empresarial pode
  estar no roteador, em outra mÃ¡quina ou temporariamente inativa e permanece intocÃ¡vel;
- porta `3306`: MariaDB em todas as interfaces IPv4, necessÃ¡ria para aplicaÃ§Ãµes locais LZGames;
- existe permissÃ£o especÃ­fica de `3306` com trÃ¡fego externo, cuja identidade funcional ainda precisa
  ser confirmada pelo proprietÃ¡rio;
- as permissÃµes globais de `3306` receberam trÃ¡fego de Internet que nÃ£o pÃ´de ser validado como
  legÃ­timo e representam exposiÃ§Ã£o desnecessÃ¡ria provÃ¡vel;
- porta `13306`: sem listener, NAT, proxy, referÃªncia, conexÃ£o ou contador de uso; permissÃµes globais
  parecem redundantes, mas ainda nÃ£o foram removidas;
- MariaDB, TurboRama PIX, nginx, Cloudflare Tunnel e endpoints local/pÃºblico continuam saudÃ¡veis;
- nenhuma credencial, licenÃ§a, preÃ§o, PDV, ativaÃ§Ã£o ou cobranÃ§a foi criada.

CorreÃ§Ã£o posterior do proprietÃ¡rio em 2026-08-07:

- a porta `3306` estÃ¡ em uso pelo sistema da empresa atravÃ©s do roteador da Claro;
- a porta informada pelo proprietÃ¡rio Ã© `23306`, tambÃ©m em uso pelo sistema da empresa no roteador;
- `23306` Ã© diferente de `13306`, que foi a porta encontrada nas regras do servidor;
- nÃ£o alterar, bloquear, remover, redirecionar ou reutilizar `3306` nem `23306`;
- nÃ£o assumir que `13306` Ã© erro ou duplicata de `23306`; manter `13306` inalterada atÃ© confirmaÃ§Ã£o
  separada da configuraÃ§Ã£o do roteador;
- a proposta anterior de restringir `3306` estÃ¡ cancelada;
- nenhuma mudanÃ§a no firewall ou no `bind-address` do MariaDB estÃ¡ autorizada.

## PrÃ³ximos passos de produÃ§Ã£o ainda pendentes

1. Preservar integralmente `3302`, `3306` e `23306`, pois foram declaradas como portas do sistema da
   empresa; manter tambÃ©m `13306` sem alteraÃ§Ã£o atÃ© confirmar separadamente a configuraÃ§Ã£o no roteador.
2. NÃ£o executar nenhuma correÃ§Ã£o de firewall ou `bind-address` como parte do projeto TurboRama.
3. Confirmar no Windows os cinco preÃ§os comerciais e o `external_id` alfanumÃ©rico do caixa Mercado
   Pago, com menos de 40 caracteres.
4. Confirmar apenas que existe um Access Token novo, nunca exposto em chat ou captura; nÃ£o informar o
   token na conversa.
5. Preparar o handoff completo da rodada 05 com esses dados nÃ£o secretos jÃ¡ preenchidos.
6. No Linux, criar o wrapper administrativo root-only, conferir/criar cliente, licenÃ§a e preÃ§os,
   inserir o token somente por entrada oculta e validar credencial e caixa sem criar cobranÃ§a.
7. Gerar o cÃ³digo de ativaÃ§Ã£o de uso Ãºnico de forma privada.
8. Atualizar somente um gabinete Windows de teste para o provider on-line e ativÃ¡-lo.
9. Confirmar sessÃ£o, identidade da mÃ¡quina e comunicaÃ§Ã£o com `pix.lzgames.com.br`.
10. Fazer uma cobranÃ§a real controlada de valor mÃ­nimo, somente apÃ³s nova autorizaÃ§Ã£o explÃ­cita.
11. Validar QR, pagamento, confirmaÃ§Ã£o, crÃ©dito e reconciliaÃ§Ã£o ponta a ponta.
12. Somente depois documentar o procedimento comercial definitivo e liberar apresentaÃ§Ã£o/venda.

Os scripts `ops/` e o handoff criado no Linux ficaram nÃ£o rastreados e nÃ£o foram enviados ao GitHub.
RevisÃ¡-los antes de decidir qualquer publicaÃ§Ã£o.

## Credenciais e testes financeiros

- Credenciais Mercado Pago que apareceram no chat devem ser consideradas expostas e substituÃ­das.
- Nunca repetir credenciais neste arquivo, GitHub, captura ou chat.
- Credenciais novas entram diretamente no servidor por entrada oculta.
- NÃ£o criar cobranÃ§a real durante compilaÃ§Ã£o, autoteste ou diagnÃ³stico.

## Regra de continuidade

Quando o usuÃ¡rio informar o servidor, continuar desta memÃ³ria e do Ãºltimo passo confirmado. NÃ£o
reiniciar o projeto, nÃ£o misturar Windows com Linux e nÃ£o modificar a referÃªncia que jÃ¡ funciona.

Para continuar em outra conversa aberta no Linux, usar o arquivo
`Servidor-pix-publicar\HANDOFF-CONTINUAR-NO-LINUX.md`. Ele deve ser enviado ao repositÃ³rio privado e
anexado ou colado integralmente na nova conversa antes de executar a instalaÃ§Ã£o.

Para a rodada de produÃ§Ã£o seguinte, usar
`HANDOFF-IDA-E-VOLTA-SERVIDOR-LINUX-RODADA-02.md`. O Linux deve devolver
`RETORNO-LINUX-RODADA-02.md` sem credenciais, cÃ³digo de ativaÃ§Ã£o ou conteÃºdo de arquivos privados.

A rodada 02 retornou bloqueada preventivamente. Para a prÃ³xima ida ao Linux, usar
`HANDOFF-LINUX-AUDITORIA-PORTAS-EMPRESA-RODADA-03.md`. Essa rodada Ã© exclusivamente de auditoria
somente leitura e deve devolver `RETORNO-LINUX-RODADA-03.md`; nenhuma regra do firewall poderÃ¡ ser
alterada.

A rodada 03 foi concluÃ­da e arquivada. NÃ£o reutilizar o handoff da rodada 03. O proprietÃ¡rio confirmou
que `3306` e `23306` pertencem ao sistema da empresa no roteador da Claro. NÃ£o preparar rodada de
alteraÃ§Ã£o do firewall.

A rodada 04 tambÃ©m foi concluÃ­da e arquivada. NÃ£o reutilizar seu handoff. A prÃ³xima ida ao Linux serÃ¡
a rodada 05 e somente deve ser preparada depois que o proprietÃ¡rio confirmar os cinco preÃ§os, o
`external_id` nÃ£o secreto do caixa e a existÃªncia de um Access Token novo. A rodada 05 deve continuar
sem tocar em `3302`, `3306`, `13306`, `23306`, MariaDB, site, nginx ou Cloudflare Tunnel.

## PreparaÃ§Ã£o Windows â€” rodada 04

Ao revisar a API oficial atual do Mercado Pago e adicionar uma validaÃ§Ã£o sem cobranÃ§a do caixa, um erro
real do adaptador foi identificado: o buffer da resposta JSON era zerado antes de o `JsonDocument`
terminar de usÃ¡-lo. Isso poderia fazer credenciais corretas falharem ao validar ou criar uma order.

CorreÃ§Ãµes locais concluÃ­das no repositÃ³rio `Servidor-pix-publicar`:

- leitura segura da resposta JSON do Mercado Pago;
- `--set-mercadopago` agora valida primeiro o Access Token e o `external_id` do caixa por
  `GET /pos?external_id=...` e sÃ³ entÃ£o grava a credencial cifrada;
- novo comando `--validate-mercadopago CLIENTE`;
- teste do adaptador real com transporte HTTP simulado, sem dinheiro e sem rede;
- compilaÃ§Ã£o: zero erros e zero avisos;
- autoteste completo: aprovado;
- nenhum token real foi usado ou incluÃ­do no pacote.

Pacote da rodada 04 publicado:

`Servidor-pix-publicar\outputs\TurboRamaPixOnlineServer-portable-RODADA04-20260807.zip`

- tamanho: `59753` bytes;
- SHA-256: `49bade5ee22a29d01b254d16141972249f0cee4c6c33eaf9e06624c885f7d63c`;
- handoff preparado: `HANDOFF-LINUX-ATUALIZAR-E-CONFIGURAR-PIX-RODADA-04.md`;
- commit publicado: `3438b1dea27e8a0f579aad8937f6ccbe80b849ad` (`3438b1d`);
- branch `main` sincronizada com `origin/main`;
- repositÃ³rio privado: `https://github.com/luziellacerda/Servidor-pix`;
- o pacote e o handoff foram incluÃ­dos no mesmo commit;
- o arquivo antigo `Servidor-pix-publicar\HANDOFF-CONTINUAR-NO-LINUX.md` permanece local e nÃ£o rastreado
  intencionalmente; nÃ£o usar nem publicar esse handoff antigo para a rodada 04;
- a atualizaÃ§Ã£o prevista nessa preparaÃ§Ã£o foi concluÃ­da no Linux e registrada no retorno da rodada
  04; nÃ£o repetir essa etapa.

## Retorno Linux â€” rodada 04

Arquivo completo arquivado em `RETORNO-LINUX-RODADA-04.md`, SHA-256
`4290d2b9c03dcc51403bedf093cd5a2d51975f39e0656c10a0a797a3a6e8cd77`.

- resultado: `ATUALIZADO_AGUARDANDO_DADOS`;
- clone privado atualizado por fast-forward para `3438b1d`;
- pacote instalado:
  `outputs/TurboRamaPixOnlineServer-portable-RODADA04-20260807.zip`;
- SHA-256 do pacote:
  `49bade5ee22a29d01b254d16141972249f0cee4c6c33eaf9e06624c885f7d63c`;
- SHA-256 do DLL instalado:
  `cad619ed6c04520af4fa5c90afa31adafcc958f874495da039d8cb38af962ed9`;
- checksums e autotestes aprovados;
- backup verificado em `/var/backups/turborama-pix/round04-20260807T133935Z`;
- versÃ£o anterior preservada em `/opt/turborama-pix.rollback-20260807T133935Z`;
- rollback nÃ£o foi necessÃ¡rio;
- `turborama-pix`, nginx, Cloudflare Tunnel e MariaDB permanecem ativos e habilitados;
- health local em `127.0.0.1:5187` e pÃºblico em `https://pix.lzgames.com.br/v1/health` responderam
  `ready: true`;
- `3302`, `3306`, `13306` e `23306`, firewall, MariaDB, nginx, Cloudflare e aplicaÃ§Ãµes LZGames nÃ£o
  foram alterados;
- nenhum token foi solicitado ou lido;
- nenhum cliente, licenÃ§a, preÃ§o, caixa, cÃ³digo de ativaÃ§Ã£o, order, QR ou pagamento foi criado;
- nenhum wrapper administrativo foi criado porque os dados comerciais ainda nÃ£o foram confirmados.

PrÃ³xima entrada necessÃ¡ria do proprietÃ¡rio, somente com dados nÃ£o secretos:

1. preÃ§o de 15 minutos;
2. preÃ§o de 30 minutos;
3. preÃ§o de 45 minutos;
4. preÃ§o de 60 minutos;
5. preÃ§o de 120 minutos;
6. `external_id` alfanumÃ©rico do caixa Mercado Pago, com menos de 40 caracteres;
7. confirmaÃ§Ã£o `SIM` de que existe um Access Token novo nunca exposto.

NÃ£o solicitar nem registrar Access Token, Public Key, Client Secret, senha ou cÃ³digo de ativaÃ§Ã£o no
chat. O token novo serÃ¡ inserido exclusivamente na entrada oculta do terminal Linux durante a rodada
05.

## PreparaÃ§Ã£o Windows â€” painel e sincronizaÃ§Ã£o â€” rodada 05

DecisÃ£o posterior do proprietÃ¡rio, que substitui a exigÃªncia anterior de redigitar preÃ§os:

- manter exatamente os valores que jÃ¡ existem no EmulationStation;
- permitir alteraÃ§Ã£o tanto no site quanto no menu do EmulationStation;
- o estado autenticado do servidor Ã© a fonte central versionada;
- `owner-settings.json` Ã© somente o cache protegido local;
- no primeiro cadastro, a primeira mÃ¡quina autorizada envia sua tabela existente ao servidor;
- em conflito simultÃ¢neo, a versÃ£o mais nova do servidor vence;
- bloquear PIX no painel nÃ£o apaga ou altera preÃ§os;
- credencial Mercado Pago nova serÃ¡ inserida somente no servidor durante teste real posterior;
- nenhum Access Token foi usado nesta preparaÃ§Ã£o.

ImplementaÃ§Ã£o local concluÃ­da no servidor:

- painel `/admin` com login PBKDF2, cookie cifrado e seguro, sessÃ£o de 30 minutos, antifalsificaÃ§Ã£o,
  limite de tentativas e cabeÃ§alhos defensivos;
- painel mostra licenÃ§as, mÃ¡quinas online/offline, recusas, versÃ£o, permissÃ£o de configuraÃ§Ã£o e
  auditoria;
- aÃ§Ãµes declarativas para PIX, licenÃ§a, mÃ¡quina, reautenticaÃ§Ã£o, preÃ§os e troca validada da credencial;
- endpoints autenticados por prova de mÃ¡quina para ler/gravar a configuraÃ§Ã£o;
- versÃ£o otimista e recusa de sobrescrita em conflito;
- migraÃ§Ã£o preserva a tabela existente e autoriza a primeira mÃ¡quina ativa a editar;
- provas invÃ¡lidas e mÃ¡quinas desconhecidas sÃ£o auditadas sem derrubar a original;
- `X-Forwarded-Proto` Ã© aceito somente do proxy local para funcionar atrÃ¡s do Cloudflare Tunnel;
- nenhum endpoint executa PowerShell, script, executÃ¡vel ou cÃ³digo arbitrÃ¡rio.

Testes Windows concluÃ­dos:

- servidor: compilaÃ§Ã£o com zero erros/avisos e autoteste aprovado;
- agente: compilaÃ§Ã£o com zero erros/avisos e autoteste aprovado;
- EmulationStation: compilaÃ§Ã£o C++ Release concluÃ­da em 100%; instalaÃ§Ã£o ativa em
  `D:\emulationstation` nÃ£o foi alterada;
- teste HTTP real do painel: login, cookie, CSRF, painel autenticado, redirecionamento, CSS e
  cabeÃ§alhos aprovados usando protocolo HTTPS encaminhado pelo proxy local;
- nenhuma rede Mercado Pago, cobranÃ§a ou credencial real foi usada.

Artefatos da rodada 05:

- pacote servidor:
  `Servidor-pix-publicar\outputs\TurboRamaPixOnlineServer-portable-RODADA05-20260807.zip`;
- tamanho: `88606` bytes;
- SHA-256:
  `d5dfdc6e7057c5fe2dfb1fa4ac3f0e2f963d77b7a9c493c89e7265c186a569d4`;
- SHA-256 do DLL do servidor:
  `38ad5837d7ce7e17b5a5fc4149a026ca32c711a52226e358278d0ee8d903d327`;
- handoff preparado:
  `Servidor-pix-publicar\HANDOFF-LINUX-PAINEL-E-SINCRONIZACAO-RODADA-05.md`;
- executÃ¡vel C++ compilado localmente:
  `TurboramaEmulationStation-repo-QR-FINAL\TurboramaEmulationStation\bin\emulationstation.exe`;
- DLL do agente recompilado:
  `TurboramaEmulationStation-repo-QR-FINAL\TurboramaEmulationStation\tools\TurboRamaPixAgent\bin\Release\net8.0-windows\TurboRamaPixAgent.dll`.

PublicaÃ§Ã£o confirmada pelo retorno do Git em 2026-08-07:

- commit: `6837917` â€” `Adicionar painel e sincronizacao PIX rodada 05`;
- branch: `main`;
- `HEAD`, `main` e `origin/main` alinhados em `6837917`;
- push concluÃ­do em `https://github.com/luziellacerda/Servidor-pix.git`;
- pacote, handoff novo e cÃ³digo do painel incluÃ­dos no commit;
- `HANDOFF-CONTINUAR-NO-LINUX.md` permaneceu local, nÃ£o rastreado e nÃ£o foi publicado, como previsto.

O repositÃ³rio do servidor estÃ¡ pronto para a ida ao Linux. NÃ£o reutilizar o handoff da rodada 04. A
rodada 05 nova deve proteger
`3302`, `3306`, `13306`, `23306`, MariaDB, site, nginx, regras existentes do tÃºnel e a API atual.
O painel pÃºblico deve usar hostname separado e Cloudflare Access; se isso nÃ£o puder ser configurado
sem risco, deixar apenas o painel local pronto e retornar o bloqueio seguro.

## Retorno Linux â€” rodada 05

Arquivo original recebido em `H:\RETORNO-LINUX-RODADA-05.md`; cÃ³pia operacional sanitizada arquivada
em `RETORNO-LINUX-RODADA-05.md`, SHA-256
`5477935d6e6286ec80c18d3aadf1deaef7be372f3b149f3f8227958736cba42f`.

- resultado: `ROLLBACK_EXECUTADO`;
- commit/pacote/checksums/autotestes da rodada 05: aprovados;
- backup verificado em `/var/backups/turborama-pix/round05-20260807T160546Z`;
- a versÃ£o 05 foi instalada, mas `/admin/login` apareceu com HTTP 200 no hostname existente
  `pix.lzgames.com.br`, antes de existir Cloudflare Access;
- o rollback foi executado imediatamente por seguranÃ§a;
- a versÃ£o 05 ficou isolada em `/opt/turborama-pix.failed-round05-20260807T160546Z`;
- a aplicaÃ§Ã£o final voltou Ã  rodada 04, DLL SHA-256
  `cad619ed6c04520af4fa5c90afa31adafcc958f874495da039d8cb38af962ed9`;
- estado e `server.env` voltaram byte a byte ao estado anterior;
- nenhuma licenÃ§a, mÃ¡quina, tabela, credencial Mercado Pago ou cobranÃ§a foi criada;
- nginx, cloudflared, site, MariaDB, API e portas protegidas permaneceram saudÃ¡veis/inalterados.

NÃ£o reutilizar o handoff da rodada 05.

## Regra de identidade e troca de computador

O proprietÃ¡rio informou troca de placa-mÃ£e no ambiente de testes, mas depois confirmou que isso deve
ser desconsiderado agora. Como ainda nÃ£o existe licenÃ§a nem mÃ¡quina cadastrada no servidor, nÃ£o hÃ¡
identidade TPM antiga para migrar ou revogar.

O servidor pode guardar a identidade pÃºblica da mÃ¡quina, `DeviceId`, tipo e status, alÃ©m da credencial
Mercado Pago cifrada do cliente. Ele nunca deve guardar uma cÃ³pia da chave privada da mÃ¡quina. Para uma
troca legÃ­tima futura: revogar a mÃ¡quina antiga, emitir um cÃ³digo de ativaÃ§Ã£o de uso Ãºnico e ativar a
nova chave criada no novo computador. Isso permite liberar outro computador sem transformar uma
cÃ³pia de HD em instalaÃ§Ã£o vÃ¡lida.

## PreparaÃ§Ã£o Windows â€” isolamento do painel â€” rodada 06

CorreÃ§Ã£o concluÃ­da no repositÃ³rio `Servidor-pix-publicar`:

- nova variÃ¡vel `TURBORAMA_ADMIN_PUBLIC_HOST`, vazia por padrÃ£o; vazia desativa todas as rotas do
  painel, inclusive locais;
- no hostname da API, toda rota `/admin*` responde `404` sem redirecionar para login;
- painel pÃºblico somente no hostname DNS exato autorizado e somente com HTTPS encaminhado pelo proxy
  loopback confiÃ¡vel;
- HTTP, prefixos, sufixos, curingas e hostnames parecidos sÃ£o recusados;
- API `/v1/*` permanece separada e funcional;
- Cloudflare Access deve ser criado antes da rota administrativa e validar o token no conector;
- falha apenas na publicaÃ§Ã£o administrativa nÃ£o exige rollback da aplicaÃ§Ã£o: manter a rodada 06 com
  o hostname administrativo vazio.

Testes Windows concluÃ­dos:

- compilaÃ§Ã£o: zero erros e zero avisos;
- autoteste completo: aprovado;
- teste HTTP real inicial endurecido depois da revisÃ£o: painel desativado com variÃ¡vel vazia; painel
  no hostname da API `404`, hostname administrativo exato configurado com HTTPS `200`, mesmo hostname em HTTP `404`, hostname
  parecido `404` e health da API no hostname atual `200`;
- nenhuma credencial real, rede Mercado Pago, licenÃ§a, mÃ¡quina, preÃ§o ou cobranÃ§a foi usada.

Artefatos preparados:

- pacote:
  `Servidor-pix-publicar\outputs\TurboRamaPixOnlineServer-portable-RODADA06-20260808.zip`;
- tamanho: `89509` bytes;
- SHA-256:
  `ff02ebe9472e86b62edd3bc4e4b31fe48627f56ab16a425f3837fc3c85ccf545`;
- SHA-256 do DLL:
  `014182be32bfa540a0cde9edbc988519728c79249db9402e9ee6cbf3076f92ee`;
- handoff:
  `Servidor-pix-publicar\HANDOFF-LINUX-ISOLAMENTO-PAINEL-RODADA-06.md`;
- SHA-256 do handoff:
  `b79f4cdd787374daddc7302f4da8b8082541475dc98aba839c4f9927afd1f3b3`;
- retorno esperado:
  `RETORNO-LINUX-RODADA-06.md`.

Antes da ida ao Linux, publicar em um Ãºnico commit o cÃ³digo, documentaÃ§Ã£o, handoff e ZIP exato da
rodada 06. O arquivo antigo `HANDOFF-CONTINUAR-NO-LINUX.md` permanece local e nÃ£o deve ser incluÃ­do.

## PublicaÃ§Ã£o e retorno Linux â€” rodada 06

- commit publicado e instalado:
  `102b8f0b6a436a999885188d0683a63d57755180` â€”
  `Isolar painel e preparar servidor PIX rodada 06`;
- `HEAD`, `main` e `origin/main` confirmados em `102b8f0` no Windows;
- retorno original arquivado em `RETORNO-LINUX-RODADA-06.md`, SHA-256
  `ce61ed5a5058d60e7e55fcf51d050e423d5193455c6c3d6b3c16ca149b4767e5`;
- resultado final: `INSTALADO_SEGURO_PAINEL_EXTERNO_DESATIVADO`;
- pacote, ZIP, checksums internos e autotestes: aprovados;
- DLL instalada SHA-256:
  `014182be32bfa540a0cde9edbc988519728c79249db9402e9ee6cbf3076f92ee`;
- backup verificado:
  `/var/backups/turborama-pix/round06-20260807T173720Z`;
- versÃ£o anterior preservada em
  `/opt/turborama-pix.rollback-round06-20260807T173720Z`;
- rollback: nÃ£o necessÃ¡rio;
- estado e `server.env`: hashes idÃªnticos antes/depois;
- nenhuma migraÃ§Ã£o, licenÃ§a, cliente, mÃ¡quina, preÃ§o ou credencial financeira criada;
- `/admin`, `/admin/`, `/admin/login`, `/admin/assets/admin.css` e `/admin/actions/pix` retornaram
  `404` pela URL pÃºblica real da API;
- `/v1/health` permaneceu `200`;
- painel local tambÃ©m permanece `404` com `TURBORAMA_ADMIN_PUBLIC_HOST` ausente;
- `PAINEL_PUBLICO: NAO` e `CLOUDFLARE_ACCESS: AGUARDANDO`;
- site, nginx, cloudflared, MariaDB, `3302`, `3306`, `13306`, `23306` e porta loopback `5187`
  permaneceram saudÃ¡veis/inalterados;
- `CREDENCIAL_MERCADOPAGO_ALTERADA: NAO` e `COBRANCA_CRIADA: NAO`.

NÃ£o reutilizar o handoff da rodada 06. A aplicaÃ§Ã£o instalada estÃ¡ segura e pode permanecer como estÃ¡.

## PrÃ³ximo marco â€” painel administrativo com Cloudflare Access

Antes de qualquer nova ida ao Linux, preparar no Cloudflare uma aplicaÃ§Ã£o Access para todo o hostname
`painelpix.lzgames.com.br`, permitindo somente a identidade do proprietÃ¡rio e com validaÃ§Ã£o do token
Access no conector/origem. A rota administrativa sÃ³ pode ser acrescentada ao tÃºnel depois dessa
barreira existir e ser comprovada.

Se o acesso autorizado ao painel Cloudflare ainda nÃ£o estiver disponÃ­vel, nÃ£o gerar uma rodada Linux
de alteraÃ§Ã£o e nÃ£o preencher `TURBORAMA_ADMIN_PUBLIC_HOST`. A API atual deve continuar exatamente no
estado seguro confirmado pela rodada 06.

Depois do Access estar pronto, a prÃ³xima rodada serÃ¡ exclusivamente aditiva e reversÃ­vel: publicar o
hostname administrativo, gerar a senha em entrada oculta no Linux, configurar apenas o hash e o
hostname exato, testar Access + login/logout e reconfirmar `404` para `/admin*` no hostname da API.

## Cloudflare Access confirmado â€” preparaÃ§Ã£o da rodada 07

Em 2026-08-08, a aplicaÃ§Ã£o Cloudflare Access foi criada e apareceu na lista de aplicativos:

- nome: `painelpix`;
- destino: `painelpix.lzgames.com.br`;
- tipo: auto-hospedado;
- polÃ­tica: `Turborama`;
- polÃ­tica verificada durante a criaÃ§Ã£o: `Allow`, e-mail especÃ­fico do proprietÃ¡rio, exigÃªncia de
  membro da conta Cloudflare selecionada e sessÃ£o de 30 minutos;
- autenticaÃ§Ã£o: somente provedor Cloudflare, autenticaÃ§Ã£o instantÃ¢nea ativa e Cloudflare One Client
  desativado;
- App Launcher e acesso sem cliente/Browser Isolation foram desativados para o aplicativo pÃºblico.

O hostname ainda nÃ£o foi ligado ao tÃºnel e `TURBORAMA_ADMIN_PUBLIC_HOST` continua vazio no Linux. NÃ£o
alterar o tÃºnel manualmente na conversa Windows.

PrÃ³ximo handoff Ãºnico:

`Servidor-pix-publicar\HANDOFF-LINUX-PUBLICAR-PAINEL-CLOUDFLARE-RODADA-07.md`

A rodada 07 deve auditar novamente a aplicaÃ§Ã£o Access, identificar sem conversÃ£o se o tÃºnel Ã© local
ou remotamente gerenciado, criar backup verificÃ¡vel, adicionar somente a rota administrativa com
**Protect with Access/JWT obrigatÃ³rio**, provar o bloqueio antes de ativar o hostname no aplicativo,
gerar a senha por entrada oculta e testar as duas barreiras. NÃ£o atualizar binÃ¡rios, criar dados
comerciais, alterar preÃ§os nem usar Mercado Pago nessa rodada.

## Retorno Linux â€” rodada 07

Arquivo original recebido em `H:\RETORNO-LINUX-RODADA-07.md`; cÃ³pia operacional sanitizada arquivada
em `RETORNO-LINUX-RODADA-07.md`, SHA-256
`de0b36d26a5876e422e3600edb00746c224cfc5b916dd81acf1fac44d7f549e8`.

- resultado: `BLOQUEADO_POR_VALIDACAO_ACCESS`;
- o tÃºnel existente foi confirmado como gerenciado remotamente pelo Cloudflare;
- o conector recebe configuraÃ§Ã£o remota com mais hostnames que o arquivo local;
- a sessÃ£o Linux nÃ£o tinha autenticaÃ§Ã£o no painel/API Cloudflare para auditar a aplicaÃ§Ã£o, obter o
  AUD/teamName e habilitar `Protect with Access`;
- por falha fechada, nenhuma rota, DNS, variÃ¡vel administrativa, senha, backup, pacote ou binÃ¡rio foi
  criado/alterado;
- rodada 06 permanece instalada, saudÃ¡vel e com painel desativado;
- `pix.lzgames.com.br/admin*` continua `404` e `/v1/health` continua `200`;
- site, nginx, cloudflared, MariaDB, firewall, estado e portas protegidas permaneceram inalterados;
- clientes/licenÃ§as/mÃ¡quinas continuam `0/0/0`;
- nenhuma credencial financeira, preÃ§o ou cobranÃ§a foi criada.

NÃ£o reutilizar o handoff da rodada 07. O prÃ³ximo passo ocorre no Windows autenticado no Cloudflare:
abrir o tÃºnel remoto existente e adicionar a rota publicada `painelpix.lzgames.com.br` para o serviÃ§o
loopback 5187 com **Protect with Access** e validaÃ§Ã£o JWT obrigatÃ³ria, sem criar outro tÃºnel nem alterar
rotas existentes. Depois disso, preparar um handoff novo para o Linux verificar a barreira e ativar o
hostname/senha do painel.

## Cloudflare publicado e testado no Windows â€” preparaÃ§Ã£o da rodada 08

Em 2026-08-08, a configuraÃ§Ã£o remota foi concluÃ­da sem modificar as seis rotas preexistentes:

- tÃºnel remoto existente `lz-fix`: saudÃ¡vel, conector conectado;
- nova rota publicada `painelpix.lzgames.com.br` apontando para `http://127.0.0.1:5187`;
- validaÃ§Ã£o Access/JWT da rota: ativada e associada ao aplicativo `painelpix`;
- aplicaÃ§Ã£o Access protegendo o hostname inteiro;
- polÃ­tica anexada: uma Ãºnica regra `Allow` para o e-mail exato do proprietÃ¡rio, sessÃ£o de 30
  minutos;
- autenticaÃ§Ã£o final testada: cÃ³digo de uso Ãºnico por e-mail;
- visitante nÃ£o autorizado: recusado pelo Cloudflare Access;
- proprietÃ¡rio autorizado: cÃ³digo confirmado e solicitaÃ§Ã£o encaminhada Ã  origem;
- resposta apÃ³s autorizaÃ§Ã£o: `404`, comportamento correto porque
  `TURBORAMA_ADMIN_PUBLIC_HOST` ainda estÃ¡ vazio no Linux;
- nenhuma variÃ¡vel Linux, senha administrativa, dado comercial ou credencial financeira foi
  alterada nessa etapa;
- existem duas polÃ­ticas reutilizÃ¡veis chamadas `Turborama`; somente uma estÃ¡ anexada ao
  aplicativo. A duplicata nÃ£o anexada deve permanecer intocada nesta rodada.

O estado anterior da memÃ³ria que mencionava requisito de membro da conta e provedor Cloudflare nÃ£o
representa mais a configuraÃ§Ã£o final. A configuraÃ§Ã£o comprovada usa somente e-mail exato + cÃ³digo
de uso Ãºnico. Nunca registrar o e-mail, cÃ³digo, cookie, AUD ou URL temporÃ¡ria de autenticaÃ§Ã£o.

PrÃ³ximo handoff Ãºnico:

`Servidor-pix-publicar\HANDOFF-LINUX-ATIVAR-PAINEL-RODADA-08.md`

A rodada 08 nÃ£o deve tocar no Cloudflare. Ela deve auditar o estado, criar backup verificÃ¡vel, gerar
a senha exclusivamente por entrada fÃ­sica/privada, adicionar apenas as quatro variÃ¡veis
administrativas ao `server.env`, reiniciar somente `turborama-pix`, validar as duas barreiras e
retornar `RETORNO-LINUX-RODADA-08.md` sanitizado.

## Retorno Linux â€” rodada 08

Arquivo original recebido em `H:\RETORNO-LINUX-RODADA-08.md`; cÃ³pia operacional sanitizada arquivada
em `RETORNO-LINUX-RODADA-08.md`. SHA-256 do arquivo original:
`f0c08f0cc9f5fa9c90d86124f0306c4489bca4aab942303f523797ce3854174f`.

- resultado final: `ATIVADO_E_VALIDADO`;
- painel ativado exclusivamente em `painelpix.lzgames.com.br`;
- Cloudflare Access por cÃ³digo de e-mail e login prÃ³prio TurboRama: ambos comprovados;
- login, painel vazio, logout TurboRama e encerramento Access: aprovados;
- commit/DLL permaneceram na rodada 06, sem atualizaÃ§Ã£o de binÃ¡rio;
- backup verificado:
  `/var/backups/turborama-pix/round08-20260808T113741Z`;
- somente as quatro variÃ¡veis `TURBORAMA_ADMIN_*` autorizadas foram adicionadas ao ambiente;
- `server.env` continua `root:root 0600` e Data Protection continua restrito ao usuÃ¡rio do serviÃ§o;
- health local/pÃºblico e site: HTTP 200;
- `pix.lzgames.com.br/admin*`: continua 404;
- painel anÃ´nimo: 302 para Access; origem administrativa: login TurboRama funcional;
- porta 5187 continua somente em loopback;
- nginx, cloudflared, MariaDB, firewall, NAT, DNS, rota, site e portas protegidas: inalterados;
- clientes/licenÃ§as/mÃ¡quinas continuam `0/0/0`;
- preÃ§os, credencial Mercado Pago e cobranÃ§as: inalterados;
- rollback: nÃ£o necessÃ¡rio.

O `state.json` mudou legitimamente de hash porque o login bem-sucedido e o logout gravam os eventos
`ADMIN_LOGIN_SUCCEEDED` e `ADMIN_LOGOUT`. A inspeÃ§Ã£o do cÃ³digo em `AdminPanel.cs` confirmou essas duas
gravaÃ§Ãµes. Clientes, licenÃ§as, pagamentos e credenciais permaneceram logicamente idÃªnticos. NÃ£o
restaurar o estado, pois isso apagaria a trilha de seguranÃ§a correta.

Regra para handoffs futuros: nÃ£o exigir SHA-256 idÃªntico do estado quando a prÃ³pria validaÃ§Ã£o executa
operaÃ§Ãµes auditadas. Exigir, em vez disso, comparaÃ§Ã£o estrutural sanitizada: somente eventos de
auditoria previstos podem mudar, enquanto clientes, licenÃ§as, mÃ¡quinas, pagamentos, preÃ§os e
credenciais devem permanecer iguais salvo autorizaÃ§Ã£o explÃ­cita.

NÃ£o reutilizar o handoff da rodada 08. O painel administrativo estÃ¡ operacional e protegido por duas
barreiras. A prÃ³xima rodada comercial deve ser preparada separadamente e somente apÃ³s autorizaÃ§Ã£o do
proprietÃ¡rio.

## Windows â€” integraÃ§Ã£o TurboRama Online rodada 09

Em 2026-08-08, a continuidade voltou ao Windows apÃ³s a aprovaÃ§Ã£o integral da rodada Linux 08. A
instalaÃ§Ã£o ativa em `D:\emulationstation` permaneceu intocada. O trabalho ocorreu somente na Ã¡rvore de
compilaÃ§Ã£o:

`TurboramaEmulationStation-repo-QR-FINAL\TurboramaEmulationStation`

Foi concluÃ­da a interface do EmulationStation para o provedor `TurboRama Online`, incluindo seleÃ§Ã£o
do provedor, perfil `SOFTWARE_BOUND_ONLINE` ou `TPM_BOUND`, ediÃ§Ã£o do identificador da licenÃ§a e
ativaÃ§Ã£o por cÃ³digo de uso Ãºnico. O cÃ³digo de ativaÃ§Ã£o nÃ£o entra em argumento de processo nem arquivo:
Ã© enviado ao agente somente por pipe anÃ´nimo e a memÃ³ria intermediÃ¡ria Ã© apagada.

TambÃ©m foi corrigida a ambiguidade de ativaÃ§Ã£o quando o servidor aceita a prova criptogrÃ¡fica, mas a
resposta final se perde. Nessa situaÃ§Ã£o, o EmulationStation tenta iniciar o agente candidato e sÃ³
confirma a ativaÃ§Ã£o se aparecer uma sessÃ£o on-line autenticada. Sem reconciliaÃ§Ã£o comprovada, ele
interrompe o candidato e restaura atomicamente a configuraÃ§Ã£o anterior. Se nÃ£o puder provar a parada,
preserva o estado candidato e recusa novo cÃ³digo para evitar consumo duplo.

Artefato de teste local, sem instalaÃ§Ã£o:

`TurboramaEmulationStation-repo-QR-FINAL\TurboramaEmulationStation\bin\emulationstation.exe`

- SHA-256 do EmulationStation de teste:
  `80B37B585038E92CFB1E962D0051314FC302453282C13FE5C9654E8C75B91A37`;
- agente copiado para `bin\pix-agent`, total de `2026994` bytes;
- SHA-256 de `TurboRamaPixAgent.dll`:
  `5CA6669CC350806C08191028BF03B461722CC016F375D1154813877F8FA4AB39`;
- dependÃªncias nativas de execuÃ§Ã£o foram reunidas em `bin` e os plugins VLC continuam referenciados
  por junction local para nÃ£o duplicar cerca de 60 MB;
- compilaÃ§Ã£o C++ Release: aprovada;
- compilaÃ§Ã£o .NET Release: zero erros;
- autoteste completo do agente, incluindo o caso de resposta perdida do servidor: aprovado;
- autotestes `--pix-agent-manager-self-test`, `--credit-warning-overlay-self-test` e
  `--protected-decorations-self-test`: aprovados com cÃ³digo 0 diretamente da pasta `bin`;
- nenhum processo de teste permaneceu aberto; somente o EmulationStation ativo do quiosque continuou
  em execuÃ§Ã£o;
- nenhuma licenÃ§a, cliente, mÃ¡quina, pagamento, preÃ§o ou credencial foi criado/alterado no servidor;
- nÃ£o chamar esta compilaÃ§Ã£o de versÃ£o comercial final: ela Ã© um candidato de teste sem assinatura.

PrÃ³ximo passo confirmado: entrar novamente no painel protegido, criar uma licenÃ§a/mÃ¡quina de teste e
um cÃ³digo de ativaÃ§Ã£o de uso Ãºnico, fechar somente o EmulationStation ativo e executar o candidato da
pasta `bin`. ComeÃ§ar por `SOFTWARE_BOUND_ONLINE`, pois o shell atual nÃ£o tem permissÃ£o para confirmar
o TPM desta placa. SÃ³ selecionar `TPM_BOUND` depois de a disponibilidade do TPM ser comprovada no
usuÃ¡rio real do quiosque. Nunca registrar em memÃ³ria, Git ou chat o cÃ³digo de ativaÃ§Ã£o, senha do
painel, cookie, token Access ou credencial Mercado Pago.

## Windows â€” correÃ§Ã£o da criaÃ§Ã£o de licenÃ§a no painel â€” servidor rodada 09

Em 2026-08-09, o proprietÃ¡rio tentou criar a primeira licenÃ§a no painel protegido. O formulÃ¡rio
estava vÃ¡lido, a sessÃ£o administrativa estava ativa e o estado continuou com zero licenÃ§as, mas o
botÃ£o nÃ£o produziu navegaÃ§Ã£o, mensagem ou cadastro. Nenhum cÃ³digo de ativaÃ§Ã£o foi criado.

A correÃ§Ã£o foi feita somente no repositÃ³rio `Servidor-pix-publicar`:

- botÃ£o de criaÃ§Ã£o explicitamente `type=submit`;
- envio assistido por JavaScript prÃ³prio e servido pela mesma origem;
- CSP ampliada apenas com `script-src 'self'`;
- bloqueio de clique duplicado, texto de progresso e aviso visÃ­vel quando nÃ£o houver confirmaÃ§Ã£o;
- autoteste de regressÃ£o do caminho de envio.

ValidaÃ§Ãµes Windows concluÃ­das:

- compilaÃ§Ã£o Release: zero erros e zero avisos;
- autoteste integral do servidor: aprovado;
- teste HTTP isolado real: login, painel, JavaScript, POST da licenÃ§a descartÃ¡vel, pÃ¡gina de cÃ³digo
  Ãºnico e listagem da licenÃ§a aprovados;
- o teste usou estado e chaves descartÃ¡veis; todos os arquivos temporÃ¡rios e segredos de teste foram
  removidos;
- nenhuma licenÃ§a, preÃ§o, credencial ou cobranÃ§a de produÃ§Ã£o foi criada.

Pacote preparado, ainda nÃ£o instalado no Linux:

`Servidor-pix-publicar\outputs\TurboRamaPixOnlineServer-portable-RODADA09-20260809.zip`

- tamanho: `90815` bytes;
- SHA-256 do ZIP:
  `633306889e0781ad3338474b587f919f56c9603f6532503d4bd9554f7a5147a2`;
- SHA-256 do DLL:
  `4c49b67a7ae719def39554b1064d71d0239f9b9bf5eb1c96bcff95b3644749a2`.

Handoff Ãºnico preparado:

`Servidor-pix-publicar\HANDOFF-LINUX-CORRIGIR-CRIACAO-LICENCA-RODADA-09.md`

PublicaÃ§Ã£o Git confirmada em 2026-08-09:

- commit `19d9695` (`Corrigir criacao de licenca no painel`);
- branch `main` sincronizada com `origin/main`;
- somente os quatro arquivos previstos da rodada 09 entraram no commit;
- os handoffs antigos `HANDOFF-CONTINUAR-NO-LINUX.md`,
  `HANDOFF-LINUX-ATIVAR-PAINEL-RODADA-08.md` e
  `HANDOFF-LINUX-PUBLICAR-PAINEL-CLOUDFLARE-RODADA-07.md` permaneceram locais, nÃ£o rastreados e nÃ£o
  devem ser usados nesta rodada.

PrÃ³ximo passo obrigatÃ³rio: levar somente o handoff da rodada 09 ao servidor Linux e aguardar
`RETORNO-LINUX-RODADA-09.md`. NÃ£o repetir a criaÃ§Ã£o da licenÃ§a no painel antes da nova versÃ£o ser
instalada. Depois do retorno aprovado, criar uma Ãºnica licenÃ§a real, guardar o cÃ³digo diretamente no
gabinete autorizado e nÃ£o registrÃ¡-lo no chat, memÃ³ria ou Git.

## Retorno Linux â€” rodada 09

Arquivo original recebido em `H:\RETORNO-LINUX-RODADA-09.md`; cÃ³pia operacional sanitizada arquivada
em `RETORNO-LINUX-RODADA-09.md`. SHA-256 do arquivo original:
`27b5c27e605929e76dd4eeec6b20fc6b5e31dd8f6a49cbf2ed022d8fd5a7b540`.

- resultado tÃ©cnico: `ATUALIZADO_E_VALIDADO`;
- commit instalado por fast-forward: `19d96952dbfec88a4199625732796744e5914d00`;
- pacote e DLL conferiram exatamente com os hashes da rodada 09;
- teste descartÃ¡vel, backup, promoÃ§Ã£o atÃ´mica, health, painel e isolamento da API: aprovados;
- rollback preparado e nÃ£o executado;
- `turborama-pix`, nginx, cloudflared, MariaDB, site, Cloudflare, firewall, roteador e portas
  protegidas permaneceram preservados;
- apÃ³s a validaÃ§Ã£o, o proprietÃ¡rio criou manualmente uma Ãºnica licenÃ§a;
- estado final: `1` cliente, `1` licenÃ§a, `0` mÃ¡quinas, `0` pagamentos, `0` credenciais e `0`
  tabelas;
- o cÃ³digo de uso Ãºnico foi exibido ao proprietÃ¡rio e nÃ£o foi incluÃ­do na cÃ³pia sanitizada;
- preÃ§os, Mercado Pago, cobranÃ§a, pagamento, QR e order permaneceram inalterados;
- a fonte original contÃ©m um segredo mantido deliberadamente pelo proprietÃ¡rio. NÃ£o copiÃ¡-lo,
  repeti-lo, versionÃ¡-lo ou transportÃ¡-lo para outros registros.

PrÃ³ximo passo confirmado: no Windows, preservar `D:\emulationstation`, fechar somente o processo
ativo do EmulationStation e abrir o candidato validado em
`TurboramaEmulationStation-repo-QR-FINAL\TurboramaEmulationStation\bin`. Configurar `TurboRama
Online`, a licenÃ§a jÃ¡ criada e `SOFTWARE_BOUND_ONLINE`, depois digitar o cÃ³digo diretamente na
interface. NÃ£o criar segunda licenÃ§a/cÃ³digo e nÃ£o registrar o cÃ³digo em chat, captura, memÃ³ria ou
Git.

## Windows â€” reconhecimento externo da mÃ¡quina PIX

Em 2026-08-09, o fluxo de reconhecimento foi concentrado no programa externo historicamente chamado
`CONFIGURAR-ACCESS-TOKEN-PIX.exe`. Nenhum arquivo do EmulationStation e nenhuma instalaÃ§Ã£o em
`D:\emulationstation` foi alterada nesta rodada.

O configurador candidato agora:

- recebe o identificador permanente da licenÃ§a e o cÃ³digo Ãºnico de ativaÃ§Ã£o;
- oferece `SOFTWARE_BOUND_ONLINE` como padrÃ£o para mÃ¡quinas sem TPM e `TPM_BOUND` como opÃ§Ã£o;
- preserva os cinco preÃ§os que jÃ¡ existirem no cadastro;
- entrega o cÃ³digo Ãºnico ao agente por pipe anÃ´nimo, sem argumento ou arquivo;
- apaga o cÃ³digo do campo e da memÃ³ria depois da tentativa;
- restaura o cadastro anterior quando o servidor rejeita a ativaÃ§Ã£o;
- preserva o candidato e orienta conferÃªncia quando a resposta final Ã© indeterminada;
- grava somente o cadastro permanente que o EmulationStation jÃ¡ sabe ler. Ao ser aberto depois da
  ativaÃ§Ã£o, o EmulationStation apenas aceita esse cadastro e nÃ£o pede novamente o cÃ³digo Ãºnico.

Candidato compilado somente na Ã¡rvore de fontes:

`TurboramaEmulationStation-repo-QR-FINAL\TurboramaEmulationStation\bin\CONFIGURAR-ACCESS-TOKEN-PIX.exe`

- SHA-256: `5786EB3666C6548A9F20769110FF0DA3A9985178EF182C6267A92E562C90FB99`;
- autoteste do configurador: cÃ³digo `0`;
- autoteste completo do agente PIX existente: cÃ³digo `0`;
- assinatura Authenticode: ausente; portanto continua sendo candidato de teste, nÃ£o release comercial.

PrÃ³ximo passo: com o EmulationStation fechado, abrir o candidato na conta automÃ¡tica do quiosque,
sem elevar como administrador, usar a Ãºnica licenÃ§a jÃ¡ criada e o cÃ³digo diretamente na interface.
NÃ£o registrar o cÃ³digo em chat, memÃ³ria, captura ou Git. Depois da confirmaÃ§Ã£o do servidor, abrir o
EmulationStation normalmente e conferir no painel que a mÃ¡quina passou de zero para uma mÃ¡quina
autorizada.

## Windows â€” organizaÃ§Ã£o visual do reconhecimento externo

Em 2026-08-09, somente a interface do candidato `CONFIGURAR-ACCESS-TOKEN-PIX.exe` foi reorganizada.
A lÃ³gica de ativaÃ§Ã£o, o EmulationStation e a instalaÃ§Ã£o em `D:\emulationstation` permaneceram
intocados.

- removida a etiqueta redundante que cobria o tÃ­tulo do perfil de proteÃ§Ã£o;
- o botÃ£o de colar licenÃ§a deixou de cobrir o tÃ­tulo do campo;
- licenÃ§a, perfil e cÃ³digo passaram a ocupar trÃªs linhas separadas e alinhadas;
- botÃµes de colar/exibir receberam espaÃ§amentos fixos e uniformes;
- cartÃ£o de seguranÃ§a e faixa de status foram ampliados e separados dos botÃµes;
- mensagens extensas de status agora terminam visualmente em reticÃªncias; o aviso modal continua
  exibindo o motivo completo;
- janela aumentada para acomodar o conteÃºdo sem sobreposiÃ§Ã£o.

Candidato atualizado:

`TurboramaEmulationStation-repo-QR-FINAL\TurboramaEmulationStation\bin\CONFIGURAR-ACCESS-TOKEN-PIX.exe`

- SHA-256: `637B5AE8B48DDA575E62596BD098AE23BE2D672A9667D89EFFD83E7C55BA22A8`;
- autoteste do configurador: cÃ³digo `0`;
- SHA-256 do EmulationStation permaneceu
  `80B37B585038E92CFB1E962D0051314FC302453282C13FE5C9654E8C75B91A37`;
- continua sem assinatura Authenticode e, portanto, Ã© candidato de teste.

## Windows â€” TRECHO HISTÃ“RICO CANCELADO: ativaÃ§Ã£o com Arcade

Este trecho de 2026-08-09 estÃ¡ cancelado para o gabinete atual. A informaÃ§Ã£o posterior de
2026-08-13 tem prioridade: a identidade operacional vÃ¡lida agora Ã© `Admin`, e nÃ£o existe
Arcade/Arkade aplicÃ¡vel ao trabalho corrente. NÃ£o usar este trecho para decidir usuÃ¡rio, SID,
AutoLogon, permissÃ£o, ativaÃ§Ã£o ou instalaÃ§Ã£o.

Registro histÃ³rico mantido apenas para auditoria: houve uma tentativa anterior de tratar uma conta
`Arcade` como usuÃ¡rio do quiosque. Essa orientaÃ§Ã£o foi invalidada por evidÃªncia posterior do
gabinete real.

O `CONFIGURAR-ACCESS-TOKEN-PIX.exe` foi corrigido para:

- permanecer visÃ­vel na conta Admin;
- solicitar apenas o consentimento administrativo normal do Windows quando necessÃ¡rio;
- nÃ£o localizar nem exigir sessÃ£o `Arcade`;
- nÃ£o recusar ativaÃ§Ã£o por ausÃªncia de `Arcade`;
- operar no modelo atual `Admin`, validado por `turborama.json` + Winlogon + SID.

O erro `Mercado Pago HTTP 404: PDV LZPIXCOMP nao foi encontrado na conta` pertence ao cadastro
antigo `provider=mercadopago`. NÃ£o criar esse PDV para contornar o erro. Depois da correção definitiva de 10/08/2026, esta orientação antiga foi cancelada: ativação on-line não muda o provedor de pagamento. `owner-settings.json` deve preservar `provider=mercadopago` ou `adapter`; o erro `LZPIXCOMP` deve ser resolvido regravando o PDV real no cadastro local Mercado Pago.

Artefato compilado e instalado:

- fonte: `TurboramaEmulationStation-repo-QR-FINAL\TurboramaEmulationStation\tools\TurboRamaPixCredentialEditor`;
- candidato: `TurboramaEmulationStation-repo-QR-FINAL\TurboramaEmulationStation\bin\CONFIGURAR-ACCESS-TOKEN-PIX.exe`;
- instalado: `D:\emulationstation\CONFIGURAR-ACCESS-TOKEN-PIX.exe`;
- SHA-256: `6A86C5A3810C9F996E8F74D508CC8DC91BAE16EB9BA8887FC16871467AFB2A3B`;
- tamanho: `340480` bytes;
- compilaÃ§Ã£o `/W4`: sem avisos;
- autoteste executado a partir de `D:\emulationstation`: cÃ³digo `0`;
- versÃ£o anterior recuperÃ¡vel em
  `release-backups\CONFIGURADOR-PIX-ANTES-SESSAO-KIOSK-20260809\CONFIGURAR-ACCESS-TOKEN-PIX.exe`,
  SHA-256 `5786EB3666C6548A9F20769110FF0DA3A9985178EF182C6267A92E562C90FB99`.

Para o teste real: fechar a janela preta/processo antigo do agente PIX antes de ativar, pois ele
mantÃ©m o bloqueio exclusivo da pasta; nÃ£o criar outra licenÃ§a nem outro cÃ³digo. O cÃ³digo que falhou
no preflight de identidade (cÃ³digo 19) nÃ£o chegou ao servidor e pode ser reutilizado. Abrir somente
`D:\emulationstation\CONFIGURAR-ACCESS-TOKEN-PIX.exe`, confirmar o consentimento do Windows e usar a
licenÃ§a/cÃ³digo jÃ¡ existentes diretamente na tela. Após a confirmação, reiniciar o gabinete e validar que o agente continua com o provedor local correto (`mercadopago` ou `adapter`) e que não tenta mais usar o PDV legado `LZPIXCOMP`.

## Windows â€” correÃ§Ã£o do provedor online e instalador Ãºnico de 10/08/2026

Foi identificado o motivo exato de o reconhecimento on-line ser aceito e, em seguida, o
EmulationStation informar `ConfiguraÃ§Ã£o pÃºblica PIX invÃ¡lida`: o agente gravava corretamente
`provider=online`, mas a validaÃ§Ã£o local do `PixBridge.cpp` ainda permitia somente
`mercadopago|mock|adapter`. A fonte foi corrigida para aceitar tambÃ©m `online` em todas as leituras
do cadastro pÃºblico e o autoteste passou a cobrir explicitamente esse provedor e rejeitar nomes
desconhecidos.

ATENCAO: esta rodada virou historico. A regra definitiva posterior cancelou provider=online como provedor de pagamento; manter apenas como legado migravel, nunca como caminho normal de PIX.

ValidaÃ§Ãµes concluÃ­das:

- EmulationStation `--protected-decorations-self-test`: cÃ³digo `0`;
- EmulationStation `--pix-agent-manager-self-test`: cÃ³digo `0`;
- EmulationStation `--pix-test-qr-cache`: cÃ³digo `0`, com `QR_CACHE_TEST=OK`;
- autoteste completo do agente PIX: cÃ³digo `0`;
- autotestes dos dois configuradores: cÃ³digo `0`;
- autotestes do instalador nativo e do bootstrapper: cÃ³digo `0`;
- teste de integridade do payload 7-Zip: aprovado, `204` arquivos;
- instalaÃ§Ã£o completa em destino isolado: aprovada;
- hashes instalados do EmulationStation, dos dois configuradores e dos `197` arquivos do agente
  coincidiram com o payload;
- ROM sentinela, tema, Launcher, configuraÃ§Ã£o, manutenÃ§Ã£o e dados persistentes foram preservados;
- arquivos `.cmd` e `.bat` no payload e na instalaÃ§Ã£o isolada: `0`.

EmulationStation homologado nesta rodada:

- SHA-256: `D3173567602531FCC7FD09661A6400B2DE734813F7F1D3FB61C6D0AAE92CF572`;
- tamanho: `789620224` bytes.

Entrega Ãºnica preservada em:

`outputs\TURBORAMA-PIX-v25-CANDIDATO-QUIOSQUE-2026-08-10\INSTALAR-TURBORAMA-PIX-COMERCIAL-v25-ULTRA-FINAL.exe`

- tamanho: `808596425` bytes;
- SHA-256: `79E17324679B2003525BB5A1546D996B74BE55BD6095E524F65AC3AB126EB867`;
- autoteste do arquivo final copiado: cÃ³digo `0`;
- assinatura Authenticode: ausente;
- contÃ©m EmulationStation, agente PIX com runtime privado e os dois configuradores alinhados;
- nÃ£o contÃ©m `.cmd`, `.bat`, fontes, PDB, certificados privados, chaves ou credenciais.

O nome histÃ³rico do arquivo contÃ©m `ULTRA-FINAL`, mas o status tÃ©cnico continua sendo **candidato
interno de quiosque**, nÃ£o pacote liberado para venda. A liberaÃ§Ã£o comercial ainda exige certificado
privado Authenticode e a chave emissora de licenÃ§as protegida em hardware. NÃ£o chamar esse arquivo de
release comercial assinada enquanto esses requisitos nÃ£o forem atendidos.

A instalaÃ§Ã£o real em `D:\emulationstation` nÃ£o foi alterada durante a validaÃ§Ã£o final; todo o teste de
instalaÃ§Ã£o ocorreu em destino isolado.

Limpeza de 10/08/2026: caches de compilaÃ§Ã£o/teste, staging temporÃ¡rio e o candidato grande de
05/08/2026 jÃ¡ substituÃ­do foram removidos. O Junction que apontava para a fonte foi desassociado antes
da limpeza e a fonte verdadeira foi conferida depois. A referÃªncia funcional, o repositÃ³rio, a
instalaÃ§Ã£o em `D:` e a entrega de 10/08 foram preservados. O espaÃ§o livre em `C:` passou de
aproximadamente `4,98 GiB` para `20,56 GiB`.

## REGRA DEFINITIVA â€” separaÃ§Ã£o entre licenciamento, preÃ§os e pagamento (10/08/2026)

Esta seÃ§Ã£o substitui qualquer orientaÃ§Ã£o anterior desta memÃ³ria que diga `provider=online`, que o
servidor controla preÃ§os ou que a cobranÃ§a Mercado Pago deve passar pelo servidor TurboRama.

- O TurboRama/EmulationStation continua sendo a autoridade dos preÃ§os. Os valores sÃ£o configurados
  localmente no prÃ³prio menu e nÃ£o dependem do painel on-line.
- O agente PIX local continua sendo responsÃ¡vel pelo pagamento. O provedor local Ã© `mercadopago` ou
  `adapter`; `mock` existe somente para teste. `online` nÃ£o Ã© provedor de pagamento.
- O servidor TurboRama Online tem como funÃ§Ã£o primÃ¡ria reconhecer LicenÃ§a + MÃ¡quina + prova de posse
  da chave privada e permitir suspensÃ£o/revogaÃ§Ã£o/transferÃªncia.
- Ativar uma licenÃ§a nÃ£o pode alterar provedor, PDV, Access Token protegido ou preÃ§os locais.
- Timeout, perda de internet, DNS, tÃºnel indisponÃ­vel ou erro `5xx` do servidor de licenÃ§a preservam
  a Ãºltima autorizaÃ§Ã£o local e nÃ£o param o quiosque.
- Somente recusa explÃ­cita e autenticada da licenÃ§a bloqueia novas cobranÃ§as PIX. Jogos, F10, F12,
  crÃ©ditos jÃ¡ existentes e operaÃ§Ã£o normal do EmulationStation continuam locais.
- Sem internet, uma nova cobranÃ§a/baixa PIX do Mercado Pago ou banco naturalmente nÃ£o funciona, mas
  isso nÃ£o pode colocar o sistema inteiro em estado off-line nem retirar crÃ©ditos jÃ¡ concedidos.
- O instalador PIX Ã© uma sobreposiÃ§Ã£o para um quiosque jÃ¡ instalado: atualiza somente o
  EmulationStation com integraÃ§Ã£o PIX, agente e configuradores. Launcher, Factory Pack, ROMs, temas,
  cache e configuraÃ§Ã£o-base ficam fora do escopo e nÃ£o podem ser alterados.
- NÃ£o entregar `.cmd` ou `.bat`; o consumidor recebe o instalador Ãºnico e os executÃ¡veis necessÃ¡rios.
- A referÃªncia funcional em `D:\HANDOFF-TURBORAMA-COMPLETO\GROK-HANDOFF-TURBORAMA-COMPLETO`
  permanece somente para leitura e nunca Ã© modificada.

ImplementaÃ§Ã£o alinhada na fonte: `OnlineLicensingEnabled` Ã© separado de `Provider`; cadastros legados
`provider=online` sÃ£o migrados para `mercadopago` com licenciamento habilitado; falhas temporÃ¡rias do
licenciamento preservam a autorizaÃ§Ã£o local; preÃ§os sÃ£o preservados na ativaÃ§Ã£o.

### Regra reforÃ§ada em 11/08/2026 â€” erro Mercado Pago/PDV nÃ£o derruba o quiosque

O erro `Mercado Pago HTTP 404: PDV LZPIXCOMP nao foi encontrado na conta` Ã© erro de cadastro local do
Mercado Pago, nÃ£o erro do servidor de licenÃ§a. A correÃ§Ã£o deve ser feita no sistema PIX/local,
regravando o PDV real pelo configurador Mercado Pago; nÃ£o transformar o servidor on-line em provedor
de pagamento e nÃ£o voltar para `provider=online`.

- O kiosk, jogos, crÃ©ditos existentes, F10 e F12 continuam locais.
- Sem internet ou com Mercado Pago indisponÃ­vel, somente novas cobranÃ§as PIX ficam bloqueadas.
- O servidor on-line serve para licenÃ§a, reconhecimento, suspensÃ£o e transferÃªncia da mÃ¡quina.
- PreÃ§os continuam no EmulationStation e no cadastro local que ele jÃ¡ lÃª.
- `LZPIXCOMP` Ã© identificador antigo de teste; nÃ£o usar como padrÃ£o de produÃ§Ã£o.
- O instalador final continua sendo sobreposiÃ§Ã£o do PIX/EmulationStation em cima do kiosk existente,
  sem `.cmd`/`.bat` e sem alterar Launcher, ROMs, temas, cache ou instalaÃ§Ã£o-base.

### Candidato interno gerado depois da separaÃ§Ã£o

Entrega:

`outputs\TURBORAMA-PIX-v25-CANDIDATO-LICENCA-SEPARADA-2026-08-10-v2\INSTALAR-TURBORAMA-PIX-COMERCIAL-v25-ULTRA-FINAL.exe`

- tamanho: `808566970` bytes;
- SHA-256: `FC210BF26414878DB736B46D07491C28FB932DDA0A6FC6DE1C1A73B5C1C0C5FC`;
- Authenticode: ausente, portanto candidato interno e nÃ£o release de venda;
- payload: `203` arquivos e `0` arquivos `.cmd`/`.bat`;
- EmulationStation: quatro autotestes aprovados;
- agente PIX: autoteste completo aprovado;
- dois configuradores, instalador, bootstrapper e instalador Ãºnico: autotestes aprovados;
- escopo: somente sobreposiÃ§Ã£o EmulationStation/PIX sobre quiosque existente.

### Windows â€” correÃ§Ã£o do PDV legado Mercado Pago em 11/08/2026

O erro visto ao iniciar o agente:

`Mercado Pago HTTP 404: PDV LZPIXCOMP nao foi encontrado na conta`

foi tratado como cadastro antigo de teste do Mercado Pago. A correÃ§Ã£o nÃ£o muda o quiosque para
`provider=online` e nÃ£o torna o sistema dependente da internet para funcionar.

Regras fixadas nesta correÃ§Ã£o:

- `LZPIXCOMP` Ã© recusado como PDV vÃ¡lido de produÃ§Ã£o;
- quando o cadastro base ainda aponta para `LZPIXCOMP`, o agente publica estado pendente e informa que
  o PDV real deve ser regravado pelo configurador Mercado Pago;
- o provedor de pagamento continua `mercadopago`;
- o servidor TurboRama Online continua restrito a licenÃ§a/reconhecimento/suspensÃ£o/transferÃªncia;
- falha de Mercado Pago, internet ou licenciamento nÃ£o derruba jogos, crÃ©ditos locais, F10 ou F12;
- somente novas cobranÃ§as PIX ficam bloqueadas atÃ© o cadastro Mercado Pago local estar correto.

Artefato interno remontado sem tocar em `D:\emulationstation`:

`outputs\TURBORAMA-PIX-v25-CANDIDATO-PDV-LEGADO-CORRIGIDO-2026-08-11\INSTALAR-TURBORAMA-PIX-COMERCIAL-v25-ULTRA-FINAL.exe`

- tamanho: `808742724` bytes;
- SHA-256: `D6D17DDAB952E8886DF0B553D35E90BACBC768063967B201C1D9D241FF463DEB`;
- SHA-256 de `TurboRamaPixAgent.dll` no payload: `0D0DF6A2C3221A3A02204A503D9CC2D6B5A38E9C32915C69C131381D9D7CDD61`;
- payload testado com 7-Zip: `203` arquivos;
- arquivos `.cmd`/`.bat` no payload: `0`;
- status: candidato interno sem Authenticode, nÃ£o release comercial assinada;
- escopo: sobreposiÃ§Ã£o EmulationStation/PIX sobre kiosk existente; nÃ£o alterar Launcher, ROMs,
  temas, cache nem instalaÃ§Ã£o-base.

## Estado de continuidade — rodada Linux 10 (11/08/2026)

O handoff ativo Ã© somente:

`Servidor-pix-publicar\HANDOFF-LINUX-ACEITAR-PIX-RODADA-10.md`

Ele substitui as instruÃ§Ãµes operacionais das rodadas Linux anteriores. Os arquivos anteriores nÃ£o
sÃ£o apagados porque registram auditoria, mas nÃ£o devem ser combinados nem executados como rodada
atual.

Regra factual desta rodada:

- `CONFIGURAR-USER-TOKEN-PIX.exe`, `CONFIGURAR-ACCESS-TOKEN-PIX.exe`, `TurboRamaPixAgent.exe` e
  `emulationstation.exe` sÃ£o binÃ¡rios Windows para teste no gabinete; nÃ£o sÃ£o pacote Linux e nÃ£o
  devem ser copiados para o servidor;
- os autotestes locais dos configuradores e do agente retornaram cÃ³digo `0`; o EmulationStation
  estÃ¡ compilado e presente, sem ser iniciado no servidor Linux;
- ainda nÃ£o Ã© permitido declarar o servidor pronto sem o retorno real da rodada 10;
- a rodada 10 nÃ£o troca arquivos, nÃ£o reinicia serviÃ§os, nÃ£o altera Cloudflare, site, banco,
  portas, preÃ§os, licenÃ§as, credenciais ou cobranÃ§as;
- sem internet, o sistema local permanece operacional; somente novas cobranÃ§as que dependam do
  Mercado Pago podem ficar indisponÃ­veis, conforme a regra definitiva acima.

## Windows — correção comprovada da identidade `kioskUser=Admin` em 12/08/2026

O erro `Não foi possível resolver kioskUser a partir do turborama.json` foi reproduzido com o
instalador antigo e rastreado sem suposição.

Evidência real do gabinete:

- o arquivo canônico `C:\TurboRama\Config\turborama.json` existe e informa `kioskUser=Admin`;
- o AutoLogon do Windows também aponta para `Admin`;
- JSON e Winlogon resolvem para o mesmo SID local;
- a conta está habilitada e pertence ao grupo local Administrators;
- não existe conta `Arcade` aplicável ao estado atual do gabinete.

Causa exata: o instalador anterior lia o JSON corretamente, mas rejeitava depois qualquer conta que
pertencesse a Administrators. O smoke antigo retornava sucesso antes dessa regra de produção e, por
isso, gerou um falso positivo.

Correção atual:

- conta local administrativa é aceita quando existe, está habilitada e JSON/Winlogon possuem o mesmo
  SID;
- conta ausente, desabilitada, bloqueada, AutoLogon desligado ou SID divergente continua bloqueada;
- foi criado diagnóstico somente leitura da identidade real instalada;
- o instalador permanece uma sobreposição PIX limitada a `emulationstation.exe`, dois configuradores
  e `pix-agent`;
- Launcher, ROMs, temas, cache, créditos, configuração-base e disco `D:` não foram alterados nos
  testes desta rodada.

Entrega atual para teste humano:

`outputs\TURBORAMA-PIX-TESTE-INTERNO-NAO-ASSINADO-20260812-R2\INSTALAR-TURBORAMA-PIX-TESTE-INTERNO-NAO-ASSINADO-R2.exe`

- tamanho: `808566322` bytes;
- SHA-256: `F319D4FC3BEF4B4F8777DCF72F307D658786514BF21DFD4CD63A6E08F0E8D3F1`;
- Authenticode: ausente;
- autoteste do arquivo final: código `0`;
- diagnóstico somente leitura do usuário real `Admin`: código `0`;
- instalação isolada do próprio arquivo entregue: código `0`;
- USER, ACCESS, agente PIX e EmulationStation: autotestes código `0`;
- vinte sentinelas de Launcher, ROM, tema, cache, créditos, configuração e dados PIX preservadas byte
  a byte;
- hash do arquivo testado igual ao hash do arquivo entregue.

O pacote antigo SHA-256
`751170F99A8E41BC5FC5AA181675F52925E1DE4D088D04B9221E523F53D4DAF0` está invalidado e não deve ser
executado.

Ainda não comprovado: instalação humana do R2 no gabinete, DPAPI no contexto operacional real,
cadastro Mercado Pago real e pagamento ponta a ponta. Portanto o R2 continua sendo candidato interno
não assinado, não versão comercial liberada para venda.

## Windows — atualização autoritativa após o teste humano R2 (12/08/2026)

Esta seção é posterior e substitui a afirmação acima de que o teste humano R2 ainda não havia sido
feito.

Handoff mestre obrigatório para continuar:

`HANDOFF-TURBORAMA-PIX-STATUS-COMPLETO-2026-08-12.md`

SHA-256 do handoff no momento desta atualização:

`79440B3AC6218E3C87D7A3F796E35093573641C079981499A11F51301C4E5263`

Estado real:

- o R2 foi executado no gabinete e falhou na proteção de permissões de
  `D:\emulationstation\emulationstation.exe`, com código Windows `32` e indicação de rollback
  incompleto;
- o R2, SHA-256
  `F319D4FC3BEF4B4F8777DCF72F307D658786514BF21DFD4CD63A6E08F0E8D3F1`, está invalidado e não deve
  ser executado novamente, vendido ou chamado de final;
- a causa comprovada foi autobloqueio do próprio instalador: o handle transacional compartilhava
  somente leitura e a etapa de DACL reabria o mesmo objeto com `MAXIMUM_ALLOWED`, causando
  `ERROR_SHARING_VIOLATION=32`;
- a causa foi reproduzida em fixture local: `MAXIMUM_ALLOWED` falhou com 32 e direitos somente de
  segurança permitiram aplicar e restaurar a DACL exatamente;
- o smoke R2 não exercitava aplicação/restauração real de DACL e, portanto, seu resultado aprovado
  não era prova da instalação de produção;
- o inventário posterior é compatível com rollback binário/cleanup e não deixou tombstones
  observados, mas não existe journal ou log independente legível nesta auditoria para provar a
  proveniência exata da árvore atual; a restauração de ACL também não foi comprovada e as DACLs
  verificadas continuam não protegidas e com owner `Admin`;
- existe uma conta local `Arcade`, habilitada e membro de `Users`, mas ela não é a identidade
  configurada do gabinete atual: JSON e Winlogon apontam para `Admin`;
- a fonte atual do instalador está em
  `TurboramaEmulationStation-repo-QR-FINAL\TurboramaEmulationStation\tools\TurboRamaCommercialInstaller\TurboRamaInstaller.cpp`,
  SHA-256 `DE8732E3C948A7C7AF1B75EA99C8E157483D13FF538A94E8743648AE8D4DF533`;
- a fonte possui correções de direitos mínimos, `FileId`, ordem de árvore e regressões de ACL, mas a
  última edição diagnóstica ainda não foi compilada/testada como fonte exata;
- não gerar R3 ainda.

Bloqueador principal atual:

- falta journal transacional durável com recuperação após queda de energia ou encerramento entre
  fases;
- backups, staging, journal e diagnóstico não podem ser apagados quando rollback/quiescência forem
  incompletos;
- os quatro alvos PIX são publicados individualmente, portanto é obrigatório provar recuperação de
  qualquer interrupção intermediária;
- `applyAdminOnlySecurity` ainda deve deixar de usar `MAXIMUM_ALLOWED`;
- direitos de rollback e privilégios do token devem ser comprovados e restritos antes da primeira
  publicação.

Próxima ação única: implementar journal/recuperação, compilar a fonte exata, executar análise
estática e matriz completa de falhas/interrupções em ambiente isolado. Só depois gerar um candidato
R3 em pasta nova e pedir novo teste humano. O quiosque base e as referências em `D:` permanecem
imutáveis.

## Windows — R4 Admin PIX em 13/08/2026

Esta seção é posterior e substitui a orientação antiga de “não gerar R3”. O trabalho avançou para R4
com a regra atual do gabinete: `Admin`, sem Arcade/Arkade.

Pacote atual preservado:

`C:\Users\Admin\Documents\Codex\pix-comercialnmnn\gerado-v25\INSTALAR-TURBORAMA-PIX-COMERCIAL-v25-ULTRA-FINAL.exe`

Evidência do arquivo:

- tamanho: `808575095` bytes;
- SHA-256: `9F1B20C485BABB6E83DD4C00C33166F0300694B633C94DD539C1E250715FB43A`;
- gerado a partir do resumo:
  `outputs\TURBORAMA-PIX-TESTE-INTERNO-ADMIN-R4-20260813\PACKAGE-R4-SUMMARY.json`;
- `installerSha256` interno: `16FBFCC9A55E90CCF8F75F9C8A430B4FB8BEFCB35A52D878C430BABC356038D5`;
- `payloadArchiveSha256`: `7222B76D7F1A065C1891A0ED07B674F47EF9A32722DD3A0F639F785B37FFDEA4`;
- Authenticode: não assinado; candidato interno de teste, não release comercial assinado.

Testes executados nesta rodada Windows, em 13/08/2026:

- `INSTALAR-TURBORAMA-PIX-COMERCIAL-v25-ULTRA-FINAL.exe --self-test`: código `0`;
- `TurboRamaInstaller.exe --self-test`: código `0`;
- `TurboRamaInstaller.exe --validate-installed-kiosk-identity`: código `0`;
- inspeção do fonte atual confirmou que `applyKioskSecurity` e `restoreSecurityBackup` não usam mais
  `MAXIMUM_ALLOWED` nas rotinas reais de aplicação/restauração de permissão; usam direitos mínimos de
  segurança e pedem `WRITE_OWNER` apenas quando necessário;
- o uso de `MAXIMUM_ALLOWED` remanescente no fonte está dentro do autoteste regressivo que reproduz o
  bug antigo de compartilhamento e confirma a correção.

Teste não executado nesta sessão:

- a matriz escrita de rollback/falhas em `%LOCALAPPDATA%\Temp\TurboRama-v25-smoke` não foi executada
  porque a sandbox da sessão bloqueou escrita/remoção nessa área fora do workspace;
- portanto, não declarar que rollback escrito/falha injetada foi comprovado nesta rodada, mesmo que o
  autoteste interno do binário tenha retornado `0`.

Regra operacional atual:

- não usar Arcade/Arkade;
- não criar `.cmd` como solução;
- não mexer em `D:\INSTALADOR KIOSK\TURBORAMA-KIOSK`;
- não mexer em `D:\HANDOFF-TURBORAMA-COMPLETO\GROK-HANDOFF-TURBORAMA-COMPLETO`;
- não alterar Launcher, ROMs, temas, cache, créditos, kiosk base ou lógica antiga do instalador do
  kiosk;
- tratar apenas a sobreposição PIX por cima do que já existe.

Pendências reais antes de chamar de final comercial:

- teste humano do instalador R4 no gabinete;
- validação real do `CONFIGURAR-USER-TOKEN-PIX.exe` com conta Mercado Pago única por máquina;
- confirmação de que o erro `Mercado Pago HTTP 404: PDV LZPIXCOMP nao foi encontrado na conta` não
  aparece após regravar o PDV real correto;
- confirmação no EmulationStation de que PIX local fica disponível após cadastro;
- teste real de falta de internet: o sistema local deve continuar navegável; somente novas cobranças
  Mercado Pago podem falhar enquanto a rede estiver indisponível;
- credenciais reais nunca devem ser gravadas nesta memória.

## Windows — R5 Admin PIX em 13/08/2026

Esta seção é posterior e substitui o R4 como candidato atual para teste humano.

Handoff completo da rodada:

`HANDOFF-TURBORAMA-PIX-ADMIN-R5-20260813.md`

Regra mantida:

- no gabinete atual, a identidade operacional válida é `Admin`;
- não usar Arcade/Arkade/arkae/TurboRama Kiosk como conta operacional deste gabinete;
- o escopo continua sendo somente a sobreposição PIX: `emulationstation.exe`,
  `CONFIGURAR-USER-TOKEN-PIX.exe`, `CONFIGURAR-ACCESS-TOKEN-PIX.exe` e `pix-agent`;
- não alterar Launcher, ROMs, temas, cache, créditos, kiosk base nem instalador base do kiosk.

Pacote atual preservado:

`C:\Users\Admin\Documents\Codex\pix-comercialnmnn\gerado-v25\INSTALAR-TURBORAMA-PIX-COMERCIAL-v25-ULTRA-FINAL.exe`

Evidência do arquivo R5:

- tamanho: `819393234` bytes;
- SHA-256: `05608F4356949B08CD37FBA3CE0FBEEFCF20E682DB57FABAEB77F41C07734909`;
- Authenticode: ausente; candidato interno de teste, não release comercial assinado;
- o arquivo R4 antigo SHA-256 `9F1B20C485BABB6E83DD4C00C33166F0300694B633C94DD539C1E250715FB43A`
  foi sobrescrito por falta de espaço livre no `C:`.

Resumo e manifesto:

- `outputs\TURBORAMA-PIX-TESTE-INTERNO-ADMIN-R5-20260813\PACKAGE-R5-SUMMARY.json`;
- `outputs\TURBORAMA-PIX-TESTE-INTERNO-ADMIN-R5-20260813\PAYLOAD-R5-SHA256.txt`;
- payload: `203` arquivos;
- arquivos `.cmd`, `.bat` e `.pdb` no payload: `0`.

Testes executados nesta rodada Windows, em 13/08/2026:

- pacote final `--self-test`: código `0`;
- `TurboRamaInstaller.exe --self-test`: código `0`;
- `TurboRamaInstaller.exe --validate-installed-kiosk-identity`: código `0`;
- `CONFIGURAR-USER-TOKEN-PIX.exe --self-test`: código `0`;
- `CONFIGURAR-ACCESS-TOKEN-PIX.exe --self-test`: código `0`;
- `TurboRamaPixAgent.dll --self-test` via runtime privado: código `0`;
- EmulationStation `--protected-decorations-self-test`: código `0`;
- EmulationStation `--pix-agent-manager-self-test`: código `0`;
- EmulationStation `--pix-agent-trust-self-test`: código `0`;
- EmulationStation `--pix-test-qr-cache`: código `0`, com `QR_CACHE_TEST=OK`.

Observação de teste:

- o teste direto do `emulationstation.exe` dentro do payload isolado retornou `-1073741515`, que é
  erro de DLL ausente ao iniciar fora da pasta completa de build/instalação;
- por isso, os autotestes do EmulationStation foram executados na pasta completa
  `C:\Users\Admin\Documents\Codex\t25\bin\x64\Release`;
- o EXE testado e o EXE do payload possuem o mesmo SHA-256:
  `85FC45E722575E97DEDDCA1C0930C6008D49B228E756ABB082329783B7568BF4`;
- portanto, o código do EXE foi testado, mas o payload isolado não é ambiente válido para executar o
  EmulationStation sozinho.

Ainda não executado nesta rodada:

- instalação humana do R5 no gabinete real;
- teste real de cadastro Mercado Pago;
- teste real de pagamento ponta a ponta;
- validação humana do EmulationStation após instalar o R5;
- assinatura Authenticode.

Não chamar o R5 de release comercial final antes desses testes.

## Windows — continuação R5 em 14/08/2026 — builds e testes somente em H:

Esta seção é posterior ao R5 de 13/08 e registra a continuação exata sem substituir evidências
históricas. Handoff detalhado:

`HANDOFF-TURBORAMA-PIX-ADMIN-R5-20260814.md`

Regra de armazenamento definida pelo proprietário:

- a fonte versionada permanece no repositório em `C:`;
- todo temporário, cache, restauração NuGet, objeto, CMake, compilação, extração, smoke, validação e
  resultado de teste deve ficar sob `H:\TurboRamaTemp`;
- não gerar saídas de build dentro do repositório em `C:`;
- não tocar na instalação, referência funcional ou instalador base em `D:`.

Correções comprovadas nesta continuação:

- o compilador comercial ganhou `-DiretorioTemporarioBuild`, aceita também
  `TURBORAMA_BUILD_TEMP_ROOT` e usa o `TEMP` atual somente como fallback;
- raiz de unidade é recusada como temporário; lock, smoke, CMake, NuGet e intermediários ficam dentro
  da fronteira informada;
- CMake ganhou `TURBORAMA_OUTPUT_DIRECTORY` e o EmulationStation foi produzido diretamente em `H:`;
- teste real de fronteira criou somente caminhos em `H:\TurboRamaTemp\compiler-boundary-test` e
  parou corretamente no Git sujo antes de assinar;
- parser PowerShell do compilador: aprovado;
- uma segunda inspeção de arquivos ignorados pelo Git removeu somente saídas não rastreadas de build:
  `bin`, `obj`, caches CMake antigos, PDBs, objetos nativos e duas entregas de teste já substituídas;
  o junction `bin\plugins` foi desassociado sem seguir ou apagar o destino;
- espaço lógico removido de `C:`: aproximadamente `1,0 GiB`; depois da limpeza, a busca por `.obj`,
  `.pdb`, `.ilk`, `.ipdb`, `.iobj` e `CMakeCache.txt` no repositório retornou zero;
- `PixBinaryTrust.h`, bootstrapper e configurador retiraram buffers grandes de caminho/hash da pilha;
- instalador corrigiu fechamento potencial de handle nulo apontado pela análise estática;
- a correção anterior de DACL com direitos mínimos permanece ativa.

Estado do cadastro Mercado Pago:

- as quatro entradas mostradas nas capturas são quatro pares Loja/PDV da mesma conta Mercado Pago,
  não quatro contas financeiras diferentes;
- a máquina aceita somente um User ID Mercado Pago; tentativa de associar outra conta é recusada
  antes de trocar segredo, cadastro ou criar recurso remoto;
- cadastro existente malformado, ambíguo, grande demais, incompatível ou sem vínculo inequívoco falha
  fechado;
- o daemon recusa divergência de conta mesmo quando o cadastro já está `ready`;
- `VER CADASTROS` permite escolher um par Loja/PDV e marca como `[ATUAL NESTE PC]` somente a
  coincidência exata com o cadastro local;
- sem coincidência exata, o programa não adivinha qual par deve permanecer;
- a limpeza opcional exige confirmação, preserva o par selecionado e recursos não TurboRama, exclui
  primeiro os PDVs `LZPIX*` antigos e exclui somente lojas `LZLOJA*` vazias;
- a revisão final encontrou e corrigiu um gap: PDVs inativos e o legado `LZPIXCOMP` não apareciam em
  `compatiblePairs`; agora também entram no plano seguro de remoção quando associados a loja
  `LZLOJA*`, sem se tornarem opções utilizáveis;
- inventário e titular são verificados novamente durante e depois da limpeza;
- nenhuma exclusão remota foi executada nesta continuação.

Binários internos atuais em `H:`:

- USER:
  `H:\TurboRamaTemp\patched-20260814-r5\owner\CONFIGURAR-USER-TOKEN-PIX.exe`,
  `600576` bytes, SHA-256
  `BE2BF62A012D141659F376DEB1D28C41E3A0B9BC00C457095A3C0E37336C0EFC`,
  `/analyze /W4 /WX` sem erro/aviso, relatório `<DEFECTS></DEFECTS>` e autoteste `0`;
- ACCESS:
  `H:\TurboRamaTemp\validation-20260814-r2\access\CONFIGURAR-ACCESS-TOKEN-PIX.exe`,
  `308736` bytes, SHA-256
  `AEB4FB27543A893D7BA6496765156352635E5473352AAF037F37D995A7982F8E`,
  `/W4 /WX` e autoteste `0`; lógica do ACCESS não foi alterada nesta continuação;
- agente DLL:
  `H:\TurboRamaTemp\validation-20260814-r2\agent-out\TurboRamaPixAgent.dll`,
  `542208` bytes, SHA-256
  `EAC27978DE8230ACA93071A4335C374FF2E9D0173C76C3EEA7DB97AB7908C3F9`,
  build com `warnaserror`, zero erro/aviso e autoteste `0`;
- EmulationStation:
  `H:\TurboRamaTemp\es-build-20260814-r2\output\emulationstation.exe`,
  `789630976` bytes, SHA-256
  `7AD674DBBC30EE19538065F9420EACA66546B0B4A94184D4D925923FD718D623`,
  build Release completo em 327 etapas; o build global ainda emite avisos legados/upstream e não deve
  ser descrito como livre de avisos;
- instalador interno:
  `H:\TurboRamaTemp\validation-20260814-r2\installer\TurboRamaInstaller.exe`,
  `569344` bytes, SHA-256
  `D86DCF96187E5FDB40CBA7FED607301C47D235EF97E14BE2291FBB2A4791646D`,
  `/analyze /W4 /WX` sem erro/aviso próprio e autoteste `0`;
- bootstrapper interno:
  `H:\TurboRamaTemp\validation-20260814-r2\installer\TurboRamaBootstrapper.exe`,
  `243712` bytes, SHA-256
  `050E6EE680E5DE6E6F49EBFEE840C367BF3FC38CDC549A9A8EECA3902C994A19`,
  `/analyze /W4 /WX` sem erro/aviso próprio e autoteste `0`.

Testes do EmulationStation exato executados a partir da pasta completa
`H:\TurboRamaTemp\es-validation-20260814-r3`:

- decorações protegidas: `0`;
- gerenciador PIX: `0`;
- confiança do agente: `0`;
- aviso de créditos sobre as telas: `0`;
- cache QR: `0`, `QR_CACHE_TEST=OK`.

A primeira execução da confiança falhou explicitamente com `Agente PIX nao foi instalado`, pois a
pasta de validação ainda não possuía `pix-agent`. Depois de copiar para `H:` o agente novo exato, o
teste foi repetido e passou. O registro da primeira falha deve ser preservado.

Depois da correção final que incluiu PDVs inativos e `LZPIXCOMP`, a matriz completa foi repetida:
USER, ACCESS, agente, instalador, identidade instalada `Admin`, bootstrapper e os cinco autotestes do
EmulationStation retornaram `0`; QR confirmou `QR_CACHE_TEST=OK`.

Verificação de layout:

- tela real do gabinete: `1360x768`;
- área útil real: `1360x728`;
- o layout novo calcula janela dentro de `1360x728` e o autoteste retornou `0`;
- uma tentativa de captura automatizada falhou porque a sessão não possuía handle válido do desktop;
  nenhuma captura foi produzida e a aparência final ainda exige inspeção humana.

Ainda não comprovado:

- nenhuma credencial exposta no chat foi reutilizada, copiada ou gravada;
- nenhuma chamada autenticada ao Mercado Pago, exclusão remota, cobrança ou pagamento foi executado
  nesta continuação;
- a documentação oficial atual confirmou as duas rotas DELETE usadas; o DNS público do servidor
  TurboRama resolveu para a Cloudflare, mas TCP 443 ficou inacessível a partir da sandbox. O health
  atual do servidor permanece `NÃO COMPROVADO` nesta sessão, sem concluir que o servidor está fora;
- DPAPI e cadastro protegido precisam de teste na sessão interativa real `Admin`;
- o binário novo ainda não foi aplicado em `D:`;
- não existe instalador único novo desta continuação: o Git está sujo e não há certificado privado de
  assinatura disponível;
- todos os binários acima estão sem Authenticode e são somente candidatos internos.

Estado correto: fontes corrigidos e binários internos testados; limpeza real dos quatro pares,
pagamento ponta a ponta e release comercial ainda não comprovados. Não formatar antes de commitar e
enviar essas alterações quando o proprietário autorizar a publicação Git.



