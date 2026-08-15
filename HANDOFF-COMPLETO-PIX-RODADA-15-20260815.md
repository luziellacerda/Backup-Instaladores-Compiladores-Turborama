# HANDOFF completo TurboRama PIX - rodada 15

Data da validação: 15/08/2026

Ambiente desta rodada: Windows, conta `Admin`

Branch Windows obrigatória: `PIX-SERVIDOR-AUTORIDADE-20260815`

Repositório: `Backup-Instaladores-Compiladores-Turborama`

Este documento é a referência atual. Quando houver conflito com handoffs antigos,
esta rodada prevalece. Ele não contém senha, Access Token, Client Secret, código
de ativação, chave privada, cookie ou material criptográfico secreto.

## 1. Regras imutáveis

- Não existe conta `Arcade` neste gabinete. A conta real usada agora é `Admin`.
- O instalador-base do kiosk já está pronto e é imutável.
- Não alterar launcher, watchdog, manutenção, ROMs, temas, jogos ou a lógica do
  kiosk-base.
- O trabalho desta linha é somente o overlay PIX: EmulationStation, agente PIX,
  dois configuradores administrativos e o instalador PIX que os entrega.
- A pasta `D:\HANDOFF-TURBORAMA-COMPLETO\GROK-HANDOFF-TURBORAMA-COMPLETO` é
  referência de leitura, nunca local de modificação.
- A pasta `D:\INSTALADOR KIOSK\TURBORAMA-KIOSK` é referência do instalador-base,
  nunca local de modificação desta linha.
- Compilações, restores, caches e testes temporários devem usar `H:\TurboRamaTemp`.
- Não inventar caminho, usuário, conta bancária, Loja, PDV, resultado ou hipótese.
  Toda conclusão precisa de código, arquivo, log, resposta HTTP ou teste real.

## 2. Arquitetura final confirmada

### Servidor LZ Games

O servidor Linux é a autoridade de:

- licença;
- máquina autorizada;
- prova de posse da chave privada;
- sessão exclusiva;
- permissão para criar uma nova cobrança;
- suspensão, revogação, transferência e reautenticação.

O servidor não cria a order Mercado Pago no fluxo final do agente desta rodada.
Ele não substitui preços, Loja, PDV ou o banco configurado no gabinete.

O código do servidor da branch atual ainda contém rotas bancárias legadas
(`/v1/orders`, `/v1/orders/status` e `/v1/enrollment/mercadopago`). O agente
final não chama essas rotas. A remoção/desativação delas e a eliminação segura de
qualquer credencial bancária antiga do estado Linux são pendências bloqueantes
antes da venda.

### Gabinete Windows

O gabinete mantém:

- configuração de preços;
- credencial Mercado Pago protegida localmente;
- Loja e PDV selecionados;
- criação da order Mercado Pago;
- geração do QR Code;
- consulta do pagamento e concessão dos créditos.

Antes de cada nova cobrança, o agente exige autorização on-line válida. Depois
que uma cobrança já foi criada, a conciliação com o Mercado Pago continua local,
inclusive durante indisponibilidade temporária do servidor de licenças. Isso não
autoriza criar nova cobrança off-line e não concede crédito sem confirmação do
banco.

### Endereços externos reais

- API de máquinas: `https://pix.lzgames.com.br/`
- Painel humano: `https://painelpix.lzgames.com.br/admin`
- O agente usa o primeiro endereço. Não apontar o agente para o hostname do painel.
- O painel humano permanece protegido pelo Cloudflare Access e pelo login próprio.
- O host da API não publica o painel: `/admin` responde 404.

### Programas administrativos

- `CONFIGURAR-USER-TOKEN-PIX.exe`: cadastra/valida uma única conta Mercado Pago,
  uma Loja e um PDV por máquina. É usado somente pelo administrador.
- `CONFIGURAR-ACCESS-TOKEN-PIX.exe`: ativa ou transfere a licença para a máquina.
  É usado somente pelo administrador.
- Os dois programas podem ser removidos do gabinete depois da instalação e da
  manutenção. O EmulationStation e o agente não devem exibir segredos.

## 3. Correção de código desta rodada

Arquivos alterados:

- `TurboramaEmulationStation/tools/TurboRamaPixAgent/OnlinePixProvider.cs`
- `TurboramaEmulationStation/tools/TurboRamaPixAgent/OnlineProtocolSelfTest.cs`
- `TurboramaEmulationStation/tools/TurboRamaPixAgent/Program.cs`

Mudança efetiva:

1. `OnlineLicenseClient` continua provando licença, máquina e sessão.
2. `OnlineAuthorizedLocalPixProvider` exige essa prova antes de criar uma cobrança.
3. A order e o QR são criados pelo provedor local já validado.
4. A consulta de uma cobrança existente é encaminhada diretamente ao provedor
   local, preservando a conciliação quando o servidor de licença cai.
5. Configuração on-line não dispensa token, produção, Loja e PDV locais válidos.
6. O heartbeat anuncia o provedor real `mercadopago`, não um provedor bancário
   fictício chamado `turborama-online`.
7. Os testes impedem que uma falha do servidor permita fallback para criar uma
   cobrança sem autorização.

## 4. Estado real do gabinete validado

- Licença: `TR-TURBORAMA-TESTE-001`.
- Perfil: `SOFTWARE_BOUND_ONLINE`.
- Transferência de hardware preparada no painel e consumida uma única vez.
- Evento do servidor: `DEVICE_ACTIVATED`, detalhe
  `transfer:SOFTWARE_BOUND_ONLINE`.
- Agente instalado: `provider=mercadopago`, `ready=true`, `state=online`.
- Cadastro local: `ready`.
- Conta Mercado Pago confirmada pelo serviço: ID público `167425399`.
- Loja ativa: `LZLOJAAEC49249316C`.
- PDV ativo: `LZPIXF50555198F64`.
- O painel mostrou uma licença ativa, uma máquina autorizada on-line e PIX
  liberado.

O código de ativação não está neste documento e não deve ser reutilizado. A
transferência já o consumiu.

## 5. Evidências de compilação e testes

Tudo abaixo foi executado usando espaço temporário em `H:`.

### Agente Windows

- Build Release: 0 erros e 0 avisos.
- `TurboRamaPixAgent.dll --self-test`: retorno 0.
- Resultado: `SELF-TEST PIX: OK`.
- SHA-256 do DLL compilado e instalado:
  `B6B3CABE9A2DA03B69DB4A12E6478420973A0B54C9FBD5BA0DA228B99DBC7201`.
- O DLL compilado e o instalado em
  `D:\emulationstation\pix-agent\TurboRamaPixAgent.dll` são idênticos.

### Configuradores instalados

- `CONFIGURAR-USER-TOKEN-PIX.exe --self-test`: retorno 0.
- SHA-256: `BE2BF62A012D141659F376DEB1D28C41E3A0B9BC00C457095A3C0E37336C0EFC`.
- `CONFIGURAR-ACCESS-TOKEN-PIX.exe --self-test`: retorno 0.
- SHA-256: `C30863E5CF44578583DBB31B92BA3575B1E2EF36A979EA8532FEDF4EA6EF26D9`.

### Servidor compilado no Windows

- Branch de origem: `SERVIDOR-AUTORIDADE-PIX-20260815`.
- Build Release: 0 erros e 0 avisos.
- `TurboRamaPixOnlineServer.dll --self-test`: retorno 0.
- SHA-256 do DLL compilado:
  `83DF4874D9D3343C18AA148DF603B4D84EEA7AF4BC3411DF47158F8B14844992`.

### Serviços externos em 15/08/2026

- `GET https://pix.lzgames.com.br/v1/health`: HTTP 200, JSON,
  `ready=true`, HSTS presente.
- `HEAD https://pix.lzgames.com.br/admin`: HTTP 404.
- `HEAD https://painelpix.lzgames.com.br/admin` sem sessão: HTTP 302 para
  Cloudflare Access.
- `HEAD https://lzgames.com.br/`: HTTP 200; site da empresa permaneceu ativo.
- O hostname `painelpix` também protege `/v1/health` com Access. Isso é esperado,
  porque a API real dos gabinetes está no hostname separado `pix`.

### Verificação de segredos

- Busca nas duas árvores Git pelos identificadores reais conhecidos desta rodada:
  zero ocorrência.
- Nenhum código de ativação real foi gravado nos repositórios.
- Fixtures sintéticas de autoteste podem conter prefixos parecidos com token;
  elas não são credenciais reais.

## 6. O que foi comprovado e o que não foi

Comprovado:

- ativação/transferência real da licença;
- sessão on-line da máquina;
- leitura real da conta, Loja e PDV Mercado Pago;
- agente on-line com provedor local Mercado Pago;
- builds e autotestes do agente, servidor e configuradores;
- separação externa entre API de máquina e painel humano.

Não foi criado nem pago um novo PIX depois do último DLL desta rodada. Houve um
pagamento real aprovado em uma etapa anterior, confirmado pelo operador, mas ele
não substitui o teste financeiro final do binário agora validado.

## 7. Mensagens antigas e sessões pendentes

Após a ativação, sessões antigas registraram
`Pacote ou valor da sessao foi adulterado`. O agente recusou esses arquivos em
modo fail-closed. Não reutilizar QR ou arquivos de sessão anteriores à ativação.
O teste seguinte deve gerar uma sessão e um QR novos.

Não apagar sessões antigas sem backup. Se for necessário limpar a fila, mover os
arquivos antigos para uma quarentena recuperável em `H:` com o agente parado e
registrar hashes antes/depois.

## 8. Pendências obrigatórias antes da venda

1. Regenerar o instalador PIX único a partir desta branch depois do commit.
2. Conferir que o instalador contém exatamente o agente com SHA-256 acima ou o
   hash novo produzido de modo reproduzível pelo mesmo commit.
3. Executar o smoke test do instalador em máquina limpa, sem tocar no
   instalador-base do kiosk.
4. Ativar a licença nessa instalação, gerar um QR novo de valor controlado,
   pagar e confirmar crédito uma única vez.
5. Reiniciar Windows, EmulationStation e agente; repetir health/readiness sem
   criar cobrança adicional.
6. Revogar as credenciais Mercado Pago expostas durante desenvolvimento e
   cadastrar credenciais novas que nunca sejam colocadas em chat, Git ou log.
7. Tornar os repositórios comerciais privados antes de tratá-los como material
   de venda.
8. Remover os dois configuradores administrativos do gabinete entregue ao
   consumidor.
9. No servidor Linux, desativar/remover as três rotas bancárias legadas, remover
   os comandos administrativos bancários antigos e purgar a credencial Mercado
   Pago antiga do estado somente depois de backup cifrado e validação de que o
   agente final continua on-line.

Não declarar versão apta à venda antes desses nove itens.

## 9. Procedimento de retomada após formatação

1. Clonar o repositório Windows.
2. Trocar para `PIX-SERVIDOR-AUTORIDADE-20260815`.
3. Ler este handoff inteiro antes de compilar.
4. Configurar `TEMP`, `TMP`, `DOTNET_CLI_HOME` e `NUGET_PACKAGES` em
   `H:\TurboRamaTemp` ou outro disco de trabalho com espaço.
5. Validar que o Git está limpo e que nenhum segredo entrou na árvore.
6. Compilar primeiro apenas o agente e executar `--self-test`.
7. Somente depois regenerar o instalador PIX, sem incorporar o kiosk-base.
8. No Linux, usar a branch `SERVIDOR-AUTORIDADE-PIX-20260815` e o handoff da
   rodada 15 do repositório `Servidor-pix`.

## 10. Regra de decisão

Se houver divergência entre teste e expectativa, parar no ponto da divergência,
preservar o estado real e registrar a evidência. Não mascarar erro, não criar
conta fictícia, não alterar o kiosk-base e não afirmar sucesso com base apenas em
autoteste.
