# TurboRama PIX v25 — licenciamento on-line sem dependência operacional

Este documento fixa a arquitetura correta do TurboRama PIX. O servidor TurboRama Online reconhece
a licença e a máquina. Ele não é provedor de pagamento, não cria cobranças PIX, não guarda o Access
Token do estabelecimento e não é a autoridade dos preços do quiosque.

## Fronteiras obrigatórias

### TurboRama/EmulationStation local

- configura e mantém os preços de 15, 30, 45, 60 e 120 minutos;
- mostra o QR Code e aplica os créditos já confirmados;
- preserva créditos avulsos, F10, F12, jogos e operação do quiosque;
- mantém o cadastro público local do estabelecimento;
- continua utilizável quando o servidor de licença estiver temporariamente indisponível.

### Agente PIX local

- usa `mercadopago` ou `adapter` como provedor de pagamento;
- lê os preços locais gravados pelo TurboRama;
- cria e consulta a cobrança diretamente no provedor configurado;
- protege a credencial do provedor no computador do estabelecimento;
- consulta separadamente o licenciamento TurboRama Online quando ele estiver habilitado.

`mock` existe apenas para autotestes. `online` não é um provedor de pagamento. Cadastros antigos com
`provider=online` são migrados para `mercadopago` com o licenciamento on-line habilitado em campo
separado.

### Servidor TurboRama Online

- cadastra Cliente, Licença e Máquina;
- registra a chave pública da máquina e o tipo de proteção;
- exige prova de posse da chave privada por desafio de uso único;
- permite suspender, revogar, transferir ou exigir nova autenticação;
- registra tentativas de clonagem sem derrubar automaticamente a máquina legítima;
- não recebe preços, token do Mercado Pago, QR Code ou dados necessários para criar a cobrança.

## Perfis de proteção

- `TPM_BOUND`: chave CNG não exportável no TPM;
- `SOFTWARE_BOUND_ONLINE`: chave CNG não exportável por política no perfil Windows, vinculada à
  máquina e acompanhada de verificação on-line;
- `USB_TOKEN_BOUND`: reservado até a homologação de um token criptográfico real. Pendrive comum não
  atende ao requisito.

O perfil não é rebaixado automaticamente. Uma licença `TPM_BOUND` apresentada como
`SOFTWARE_BOUND_ONLINE` deve ser recusada até que o administrador autorize uma transferência.

## Ativação da máquina

1. O administrador cria ou seleciona a licença no painel.
2. O painel emite um código de ativação de uso único.
3. O configurador local gera ou abre a chave privada do perfil escolhido.
4. Somente a chave pública e a prova criptográfica seguem para o servidor.
5. O servidor associa Cliente + Licença + DeviceId.
6. O código de uso único é consumido e apagado da memória local.
7. O cadastro persistente contém a licença, o endereço do servidor e o perfil; não contém o código.

O cadastro da licença não altera provedor, PDV, Access Token ou preços locais.

## Consulta de licença e funcionamento sem o servidor

O agente tenta confirmar periodicamente a licença. Os resultados são tratados assim:

- autorização confirmada: renova a autorização local;
- recusa explícita e autenticada (`401`, `403`, `409` ou falha criptográfica): bloqueia somente novas
  cobranças PIX;
- timeout, perda de internet, DNS, túnel indisponível ou erro `5xx`: preserva a última autorização
  local conhecida e tenta novamente depois.

Uma falha temporária do servidor de licenças nunca deve encerrar o EmulationStation, retirar créditos
já concedidos, bloquear jogos, F10/F12 ou impedir o uso normal do quiosque.

Sem conexão com a internet também não há como o Mercado Pago/banco criar ou confirmar uma nova
cobrança. Nesse caso, somente a função de comprar novos créditos por PIX fica temporariamente
indisponível. O restante do sistema permanece local e operacional.

## Fluxo de pagamento

1. O TurboRama escolhe um pacote e informa ao agente o valor/preço local.
2. O agente valida o pacote contra a tabela local.
3. O agente chama diretamente o Mercado Pago ou o adaptador bancário local configurado.
4. O agente recebe o QR Code e o publica na ponte local do EmulationStation.
5. O agente consulta o pagamento no mesmo provedor.
6. Após confirmação válida, o TurboRama aplica os créditos.

O servidor de licenças não participa desses seis passos.

## Cadastro persistente esperado

Exemplo conceitual, sem credenciais:

```json
{
  "provider": "mercadopago",
  "onlineLicensingEnabled": true,
  "onlineBaseUrl": "https://pix.exemplo.com.br/",
  "onlineLicenseId": "TR-000125",
  "onlineProtectionProfile": "SOFTWARE_BOUND_ONLINE",
  "packagePricesCents": {
    "15": 750,
    "30": 1500,
    "45": 2250,
    "60": 3000,
    "120": 6000
  }
}
```

Os preços acima são exemplos locais. Alterá-los no menu do TurboRama deve continuar sendo suficiente;
a ativação da licença preserva a tabela já existente.

## Regras do painel administrativo

O painel pode mostrar e controlar:

- cliente, licença e máquina;
- `TPM_BOUND`, `SOFTWARE_BOUND_ONLINE` ou `USB_TOKEN_BOUND`;
- último contato, versão, sessão, status e tentativas recusadas;
- `ACTIVE`, `SUSPENDED`, `REVOKED`, `MAINTENANCE` e `TRANSFER_PENDING`;
- ações declarativas `DISABLE_PIX`, `SUSPEND_LICENSE`, `FORCE_REAUTH`, `REQUIRE_UPDATE` e
  `ENTER_MAINTENANCE`.

O painel não pode enviar PowerShell, scripts, executáveis ou código arbitrário ao quiosque.

## Segurança e limitações

- `SOFTWARE_BOUND_ONLINE` é mais fraco que TPM e precisa de monitoramento de máquina/sessão;
- fingerprint de hardware é sinal de risco, não raiz criptográfica;
- uma recusa remota só deve bloquear novas cobranças quando for autêntica e inequívoca;
- indisponibilidade do servidor não deve ser confundida com revogação;
- credenciais publicadas em conversa ou repositório devem ser renovadas e nunca reutilizadas;
- o repositório comercial e as chaves privadas permanecem fora do instalador do consumidor.

## Critérios de aceite

- alterar preços localmente não exige o painel;
- ativar/reconhecer uma máquina não altera preços nem provedor local;
- Mercado Pago e adaptador funcionam sem passar pelo servidor de licenças;
- servidor de licença desligado: quiosque e créditos existentes continuam funcionando;
- provedor/internet desligado: somente novas compras PIX ficam indisponíveis;
- recusa explícita de licença: novas compras PIX são bloqueadas, sem destruir dados locais;
- payload comercial não contém `.cmd`, `.bat`, fonte, PDB, segredo ou chave privada;
- instalador atualiza apenas EmulationStation/PIX sobre um quiosque já existente e preserva Launcher,
  ROMs, temas, cache e configuração-base.
