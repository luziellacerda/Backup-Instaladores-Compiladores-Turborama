# HANDOFF TurboRama — servidor como autoridade PIX — rodada 14

Data: 15/08/2026

## Regras confirmadas pelo proprietário

- A única conta Windows usada neste gabinete é `Admin`. Não existe conta Arcade.
- O instalador/base do kiosk, Launcher, Watchdog, serviços, ROMs, temas, créditos
  e lógica do Factory Pack são imutáveis e ficam fora deste projeto.
- Este projeto é somente o overlay PIX: `emulationstation.exe` e `pix-agent`.
- `CONFIGURAR-ACCESS-TOKEN-PIX.exe` e `CONFIGURAR-USER-TOKEN-PIX.exe` são
  ferramentas portáteis exclusivas do administrador. Não entram no payload e
  devem ser removidas do gabinete depois da manutenção.
- Access Token e Client Secret Mercado Pago não ficam no kiosk. O token é
  enviado uma única vez ao servidor, cifrado lá e nunca devolvido ao Windows.
- Cada nova cobrança exige autorização do servidor. Não existe fallback local
  com credencial financeira.
- Sem internet/servidor, somente novas cobranças PIX ficam indisponíveis. Jogos,
  preços locais, créditos concedidos, F10/F12 e EmulationStation continuam.
- Authenticode é opcional e não participa da autorização funcional.
- Build/testes temporários ficam em `H:\TurboRamaTemp`; D: não é alterado pelos
  testes.
- Não declarar pagamento real aprovado sem QR criado, pago, conciliado e
  conferido por uma pessoa na conta Mercado Pago.

## Branches publicadas

### Windows/cliente

- Repositório: `luziellacerda/Backup-Instaladores-Compiladores-Turborama`
- Branch: `PIX-SERVIDOR-AUTORIDADE-20260815`
- HEAD publicado: `35e5f20499f2f2da81137ff5662ca8f860cc2d24`
- Commit que gerou o instalador validado: `053ffd28d49ac3ac8ab1a63f066bb74b710bcead`

### Linux/servidor

- Repositório: `luziellacerda/Servidor-pix`
- Branch: `SERVIDOR-AUTORIDADE-PIX-20260815`
- HEAD publicado: `9f0f5b512cfed241445dbb95d279c8a2054b0f69`
- Handoff Linux: `HANDOFF-LINUX-SERVIDOR-AUTORIDADE-RODADA-14.md`
- Pacote: `outputs/TurboRamaPixOnlineServer-portable-RODADA14-20260815.zip`
- SHA-256 do pacote Linux:
  `07F3154D04CB64CA945315602D89B1AA2E03023C10A4536388C7B9EFF9CBFEA0`

Os repositórios precisam ser privados antes de qualquer operação comercial.

## Arquitetura implementada

1. O EmulationStation pede uma nova cobrança ao agente local.
2. O agente usa identidade criptográfica da máquina e sessão online.
3. O servidor valida licença, máquina, prova, sessão, status e preço.
4. Somente o servidor usa a credencial Mercado Pago para criar/consultar PIX.
5. O agente recebe somente os dados necessários ao QR/status.
6. O painel pode bloquear licença, máquina ou novas compras sem executar script
   remoto no gabinete.

O modo preferencial é `TPM_BOUND`. Em máquina sem TPM, o modo disponível é
`SOFTWARE_BOUND_ONLINE`, com chave DPAPI, fingerprint e sessão exclusiva. O
perfil `USB_TOKEN_BOUND` continua recusado até homologação de token
criptográfico real; pendrive comum não serve.

## Instalador Windows validado

Caminho local (não versionado por ser maior que o limite normal do GitHub):

`H:\TurboRamaTemp\2026-08-04\c-users-admin-documents-codex-2026\TurboramaEmulationStation-repo-QR-FINAL\TurboramaEmulationStation\PIX-COMERCIAL\GERADO-v25\INSTALAR-TURBORAMA-PIX-COMERCIAL-v25-ULTRA-FINAL.exe`

- tamanho: 808.496.352 bytes;
- SHA-256:
  `4FCD9D36681979659DB064DD7B8740842DAACCB1888C14FD0B3E6CD4D42C0872`;
- status: candidato interno tecnicamente validado, ainda não liberado para venda;
- assinatura Authenticode: não aplicada, sem impedir a autorização online.

A mesma pasta contém os dois programas administrativos portáteis e
`CHECKSUMS-SHA256.txt`. Eles não estão dentro do instalador.

## Conteúdo do payload

O arquivo interno contém 201 arquivos em 16 pastas, 866.361.997 bytes antes da
compressão. O conjunto fechado é:

- `emulationstation.exe`;
- árvore completa e selada de `pix-agent`, inclusive runtime .NET privado;
- quatro avisos/binários legais do 7-Zip em `THIRD-PARTY-NOTICES`.

Não entram no payload:

- os dois configuradores administrativos;
- Launcher/Watchdog/Maintenance;
- kiosk base, wrapper, `turborama.json`, ROMs ou temas;
- logs, sessões, credenciais, chaves, fontes, PDB, `.cmd` ou `.bat`.

## Testes concluídos no Windows

- Agente Release: build com zero erros/avisos.
- Autoteste completo do agente: aprovado.
- EmulationStation: build Release e `PIX_AGENT_MANAGER_TEST=OK`.
- Configurador bancário e ativador: compilação/autoteste aprovados.
- Instalador interno e bootstrapper: autotestes aprovados.
- 7-Zip do payload: integridade aprovada.
- Guard sem `maintenance.lock`: retorno esperado aprovado.
- Recusa determinística de processo: retorno esperado aprovado.
- Rollback após extração: conteúdo, atributos, owner e SDDL restaurados.
- Rollback após mudança do estado PIX: 11 estados restaurados; credenciais fora
  do snapshot permaneceram intocadas.
- Instalação isolada válida: aprovada.
- Agente instalado: autoteste aprovado.
- EmulationStation instalado: `PIX_AGENT_TRUST_TEST=OK`.
- Manifesto fechado recusa arquivo extra, ausente ou alterado.
- Scanner da entrega: nenhum fonte, PDB, chave privada, `.cmd` ou `.bat`.

Falhas reais encontradas e corrigidas durante a validação:

1. Escopo local incorreto do template `appsettings.json` no compilador.
2. Smoke apontava para `%LOCALAPPDATA%` e podia escapar de H:.
3. Smoke preservava 1,6 GB após cada falha intencional e esgotava H:; agora
   limpa somente os quatro retornos esperados e preserva qualquer falha real.
4. Teste do EXE instalado não oferecia as DLLs versionadas já pertencentes ao
   kiosk; agora usa o mesmo conjunto de runtime da compilação sem incluí-lo no
   overlay.

## Prova de que kiosk instalado não foi alterado

Após o build/smoke, os arquivos reais continuavam com os mesmos hashes e datas:

- `D:\emulationstation\emulationstation.exe` —
  `7AD674DBBC30EE19538065F9420EACA66546B0B4A94184D4D925923FD718D623`;
- `D:\emulationstation\CONFIGURAR-USER-TOKEN-PIX.exe` —
  `BE2BF62A012D141659F376DEB1D28C41E3A0B9BC00C457095A3C0E37336C0EFC`;
- `D:\emulationstation\CONFIGURAR-ACCESS-TOKEN-PIX.exe` —
  `C30863E5CF44578583DBB31B92BA3575B1E2EF36A979EA8532FEDF4EA6EF26D9`;
- `D:\emulationstation\pix-agent\TurboRamaPixAgent.dll` —
  `AA138548DB00149F21DC97818CD88971D986EABBF46EFEB318F8E5D6A4DA269E`;
- `C:\TurboRama\Config\turborama.json` —
  `55996C6358226D0EE307C9085B51E63E61C578A49EA67AD96318973CB8CE9985`.

## Bloqueio real atual

Em 15/08/2026, uma consulta externa nova a
`https://painelpix.lzgames.com.br/v1/health` retornou HTTP 302 e HTML do
Cloudflare Access. O agente não consegue usar uma tela de login humana.

Antes de instalar/ativar este cliente, executar no Linux o handoff da rodada 14:

- implantar o pacote preservando estado/chaves atuais;
- manter `/admin*` protegido pelo Cloudflare Access;
- liberar `/v1/*` da autenticação humana, mantendo as provas do protocolo;
- comprovar `/v1/health` externo com HTTP 200 JSON;
- comprovar que o site da empresa permanece intacto.

## Sequência restante para liberação

1. Executar o handoff Linux e devolver `RETORNO-LINUX-RODADA-14.md`.
2. Auditar o retorno no Windows; não avançar se `/v1/health` ainda for 302/HTML.
3. Fazer backup do gabinete e entrar no modo de manutenção do kiosk já existente.
4. Instalar somente o overlay PIX validado.
5. Criar/selecionar cliente e licença no painel.
6. Ativar a máquina com o programa administrativo portátil.
7. Gerar código bancário de 15 minutos e cadastrar Mercado Pago com o programa
   portátil; não salvar token no Windows.
8. Fazer um PIX real de valor controlado e conferir criação, pagamento,
   conciliação e crédito.
9. Revogar todas as credenciais que apareceram em conversa/testes e substituir
   por credenciais novas nunca publicadas.
10. Tornar ambos os repositórios privados e executar a auditoria final de venda.

Até concluir essas etapas, o resultado é candidato interno validado localmente,
não produto liberado para consumidor.
