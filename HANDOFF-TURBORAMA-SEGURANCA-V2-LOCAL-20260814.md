# HANDOFF TURBORAMA PIX - SEGURANCA V2 LOCAL - 2026-08-14

Este handoff registra somente fatos comprovados. Ele nao contem Access Token, Public Key, Client Secret,
senha, codigo unico, chave privada ou outro segredo.

## 1. Limites imutaveis

- A conta real do gabinete nesta rodada e a conta local `Admin`. Nao existe conta Arcade, Arkade ou arkae.
- O instalador e a logica do Kiosk base sao imutaveis.
- O escopo continua sendo apenas a sobreposicao PIX: `emulationstation.exe`,
  `CONFIGURAR-USER-TOKEN-PIX.exe`, `CONFIGURAR-ACCESS-TOKEN-PIX.exe` e `pix-agent`.
- Nao alterar Launcher, Watchdog, ROMs, temas, cache, creditos ou instalacao base.
- Nao modificar a referencia funcional nem a instalacao em `D:`.
- Temporarios, caches, builds, extracoes e validacoes devem permanecer em `H:\TurboRamaTemp`.
- Nao entregar `.cmd` ou `.bat`.
- Precos e funcionamento do Kiosk permanecem locais. O licenciamento on-line nao controla precos.
- Sem Internet, Kiosk e creditos locais continuam. Nova cobranca PIX, baixa de pagamento e renovacao que
  realmente exigem rede podem ficar indisponiveis.

## 2. Estado Git

- Repositorio cliente: `TurboramaEmulationStation-repo-QR-FINAL`.
- Base publica verificada: branch `PIX-SEGURANCA-V1-BASE-20260814`, commit
  `5b4a4388a78a503a2e871705b5b2d929d7e0d524`.
- Trabalho V2: branch local `PIX-SEGURANCA-V2-LOCAL-20260814`.
- Commit funcional V2: `70e235c` (`Fechar autorizacao PIX e validar TPM em hardware`).
- Repositorio servidor: `Servidor-pix-publicar`.
- Base publica verificada: branch `SEGURANCA-V1-BASE-20260814`, commit
  `4e8ce7e7bd670acc7c737f117f27497aabdcc92a`.
- Trabalho servidor V2: branch local `SEGURANCA-V2-LOCAL-20260814`.
- Commit funcional servidor V2: `92dba87` (`Endurecer transporte e perfis de licenca`).
- Os ramos V2 foram enviados por ordem expressa do responsavel, que decidiu tornar os repositorios
  privados somente ao final do trabalho. Em 2026-08-15 ambos ainda estavam publicos.

## 3. Correcao do cliente PIX

- Se o licenciamento on-line estiver habilitado, o agente inicia sem autorizacao para criar nova
  cobranca ate o servidor confirmar a maquina naquele processo.
- A primeira indisponibilidade de rede nao libera PIX por engano.
- Depois de uma confirmacao valida, falha transitoria de rede pode preservar a autorizacao somente
  durante o mesmo processo.
- Recusa explicita do servidor continua recusada, mesmo que depois ocorra timeout ou erro 5xx.
- Nova confirmacao valida pode reabrir o PIX.
- HTTP 408, 425, 429 e 5xx sao classificados como transitorios; 400, 401, 403, 404 e 409 sao recusas
  controladas, sem encerrar indevidamente o agente.
- A falta de autorizacao bloqueia somente nova operacao PIX. Nao bloqueia o Kiosk nem creditos locais.
- A chave CNG declarada como TPM agora precisa comprovar `NCRYPT_IMPL_HARDWARE_FLAG`; uma chave de
  software apresentada como TPM e recusada.

Arquivos alterados:

- `TurboramaEmulationStation/tools/TurboRamaPixAgent/OnlinePixProvider.cs`;
- `TurboramaEmulationStation/tools/TurboRamaPixAgent/OnlineProtocolSelfTest.cs`;
- `TurboramaEmulationStation/tools/TurboRamaPixAgent/Program.cs`;
- `TurboramaEmulationStation/tools/TurboRamaPixAgent/TpmMachineBinding.cs`.

## 4. Testes do cliente

- Build Release com avisos tratados como erro: zero avisos e zero erros.
- `--self-test`: codigo de saida `0`.
- Restore NuGet com auditoria habilitada para todas as dependencias e avisos tratados como erro:
  codigo `0`, sem alerta de vulnerabilidade.
- DLL testada:
  `H:\TurboRamaTemp\security-v2-20260814\agent-artifacts-r2\bin\TurboRamaPixAgent\release\TurboRamaPixAgent.dll`.
- SHA-256: `072A5719451C27E4159149EE3B6A690ED309182328F1DFA9CAB836459B0F96FC`.
- Uma tentativa anterior de `dotnet list package --vulnerable` nao encontrou o arquivo de assets por
  causa da saida isolada. A verificacao correta foi repetida com restore auditado e aprovada.
- Nenhuma cobranca real Mercado Pago foi criada nesta rodada V2.
- DPAPI, chave do usuario, ativacao no gabinete e instalacao completa nao foram repetidos nesta rodada.

## 5. Correcao do servidor

- `USB_TOKEN_BOUND` e recusado ate existir implementacao real com token criptografico homologado.
- HTTPS e aceito.
- HTTP somente pode ser usado no loopback quando o endereco remoto e o proprio `Host` sao loopback.
- Um hostname publico encaminhado pelo tunnel com protocolo HTTP e recusado, mesmo chegando de loopback.
- Respostas HTTPS recebem HSTS com `max-age=31536000`.
- Testes cobrem host loopback, host publico, origem nao loopback, HTTP desabilitado e perfil USB recusado.

## 6. Testes e pacote servidor R13

- Build Release com avisos tratados como erro: zero avisos e zero erros.
- Self-test do servidor: codigo `0`.
- Restore NuGet auditado: codigo `0`, sem avisos.
- Teste HTTP real local do binario exato:
  - loopback HTTP: `200`;
  - hostname publico enviado em HTTP: `400`;
  - hostname publico com `X-Forwarded-Proto: https`: `200`;
  - HSTS em HTTPS: presente uma vez;
  - `/admin` no hostname da API: `404`.
- O primeiro inicio direto do script foi bloqueado pela politica local do PowerShell. Ele foi executado
  novamente com `-ExecutionPolicy Bypass`. Um erro de parser do primeiro rascunho foi corrigido antes
  do teste aprovado.
- ZIP portatil:
  `H:\TurboRamaTemp\security-v2-20260814\TurboRamaPixOnlineServer-portable-SEGURANCA-R13-20260814.zip`.
- Tamanho do ZIP: `104839` bytes.
- SHA-256 do ZIP: `1E1B49EE28CD9AE20BBDD24E5B53A6751E105E23859B794D108DAF4FA63C8745`.
- DLL do pacote: `310784` bytes.
- SHA-256 da DLL: `FDED6FBC4488254BF53542DDBDEEB17C9C2AA651D67BFFADF697E0AC7BF0F6D9`.
- O ZIP foi extraido em pasta nova, contem exatamente cinco arquivos, passou verificacao de checksums,
  nao contem fonte, script ou arquivo proibido, e seu binario extraido passou o self-test.

## 7. Estado do servico publicado apos a Rodada 13

- O Linux implantou o commit `82ecef33c883b38824582a63aeb60aee718ad606` e o pacote R13 exato.
- O retorno Linux registrou servico ativo, zero reinicios, listener somente em `127.0.0.1:5187`,
  self-test `0` e preservacao de estado, banco, Nginx, Cloudflare e site da empresa.
- A verificacao externa independente posterior confirmou:
  - `GET http://pix.lzgames.com.br/v1/health`: `400`;
  - `GET https://pix.lzgames.com.br/v1/health`: `200`;
  - HSTS `max-age=31536000`: presente;
  - `https://pix.lzgames.com.br/admin`: `404`;
  - painel protegido pelo Cloudflare Access: `302`;
  - site da empresa: `200`.
- O retorno integral esta em `RETORNO-LINUX-RODADA-13.md` na midia de transferencia do operador.

## 8. Segredos e verificacoes

- Busca por token continuo `APP_USR-`, cabecalho de chave privada e atribuicoes longas de Access Token
  ou Client Secret retornou zero nos dois repositorios.
- Nenhuma credencial fornecida em conversa deve ser copiada para Git, handoff, log ou pacote.
- O repositorio continua publico. Tornar privado depois reduz exposicao futura, mas nao apaga clones ou
  historico que terceiros possam ter obtido enquanto esteve publico.

## 9. Bloqueios comerciais reais

- Nao existe certificado privado Authenticode de assinatura de codigo instalado nesta maquina;
  consulta local encontrou zero certificados de Code Signing.
- O TPM fisico atual foi comprovado por `tpmtool getdeviceinformation`: TPM 2.0 AMD, inicializado,
  pronto para armazenamento e atestado, com capacidade de atestado, sem firmware vulneravel e nao
  bloqueado. A consulta WMI continuou negada por o processo nao estar elevado.
- Nao ha atestacao remota de TPM.
- `SOFTWARE_BOUND_ONLINE` continua sendo o modo mais fraco e pode ser atacado por administrador local.
- Um usuario administrador da propria maquina pode inspecionar ou alterar software local. Nao existe
  protecao local absoluta contra esse controle.
- A protecao de temas/decoracoes ainda usa material embarcado recuperavel; corrigir isso de verdade exige
  chave por licenca entregue pelo servidor e alteracao maior de protocolo e cliente.
- Ainda faltam assinatura comercial, teste de instalacao completo em maquina limpa, ativacao real,
  DPAPI/TPM e uma cobranca Mercado Pago seguida de conciliacao e expiracao controladas.

## 10. Correcao da fronteira de compilacao em H

- Foi encontrado um desvio real: o compilador principal aceitava `DiretorioTemporarioBuild`, mas
  `Build-TurboRamaPackage.ps1` ainda fixava a saida em `%LOCALAPPDATA%\Temp`.
- O empacotador agora exige explicitamente `DiretorioTemporarioBuild` e calcula o unico destino permitido
  dentro dessa fronteira.
- O compilador principal passa a mesma fronteira validada ao empacotador.
- Parser dos dois scripts: zero erros.
- Teste valido em `H:\TurboRamaTemp\pack-boundary-selftest-20260814`: codigo `0`, pacote criado em H.
- Teste com destino fora da fronteira: codigo `1`, mensagem de recusa e nenhum arquivo criado.
- O preflight seguinte encontrou Visual Studio instalado, mas `Import-VsEnvironment` descartava a
  variavel retornada como `Path=` porque aceitava apenas `PATH=` com caixa exata.
- A importacao agora reconhece `Path` sem diferenciar maiusculas e minusculas, preservando o valor
  devolvido pelo ambiente oficial do Visual Studio.

## 11. Regra de conclusao

Este estado e uma V2 local endurecida e testada no que foi possivel nesta maquina. Nao chamar de release
final para venda e nao gerar instalador comercial sem assinatura. A base funcional recuperavel foi
preservada; nenhuma parte do Kiosk base ou de `D:` foi modificada nesta rodada.
