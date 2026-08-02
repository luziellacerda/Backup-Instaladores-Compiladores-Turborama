# Contrato do adaptador bancário TurboRama PIX

Este contrato permite integrar qualquer banco ou plataforma que ofereça uma API PIX. A parte específica do banco fica no adaptador; o EmulationStation, o contador, os pacotes, a assinatura e a prevenção de crédito duplicado não mudam.

## Segurança obrigatória

- O adaptador deve exigir `Authorization: Bearer <segredo>` em todas as rotas.
- O mesmo segredo é informado durante a instalação e fica protegido pelo Windows (DPAPI).
- Em outro computador, a URL deve usar HTTPS. HTTP só é aceito em `127.0.0.1` ou `localhost`.
- O adaptador deve consultar o banco antes de informar `approved`; nunca confie somente em tela, redirecionamento ou arquivo local.
- Cada solicitação usa `X-Idempotency-Key`. Repetir a mesma chave deve retornar a mesma cobrança, nunca criar outra.
- Valores são inteiros em centavos e a moeda é sempre `BRL`.
- Respostas devem ter no máximo 64 KiB.

## 1. Estado do adaptador

`GET /v1/health`

Resposta HTTP 200:

```json
{
  "schemaVersion": 1,
  "providerId": "meu-banco",
  "ready": true
}
```

`providerId` deve ser exatamente o mesmo configurado no instalador.

## 2. Criar cobrança PIX

`POST /v1/orders`

Cabeçalhos:

```text
Authorization: Bearer <segredo>
X-Idempotency-Key: <externalReference>
Content-Type: application/json
```

Corpo enviado pelo TurboRama:

```json
{
  "schemaVersion": 1,
  "externalReference": "PIX-IDENTIFICADOR-UNICO",
  "amountCents": 750,
  "currency": "BRL",
  "minutes": 15,
  "description": "Tempo TurboRama - 15 min",
  "expiresInSeconds": 900
}
```

Resposta HTTP 200 ou 201:

```json
{
  "schemaVersion": 1,
  "providerId": "meu-banco",
  "providerOrderId": "ORDEM-DO-BANCO-123",
  "externalReference": "PIX-IDENTIFICADOR-UNICO",
  "amountCents": 750,
  "currency": "BRL",
  "qrData": "000201...",
  "status": "pending"
}
```

`providerOrderId` aceita somente letras, números, hífen e sublinhado, com até 128 caracteres. `qrData` deve conter o PIX Copia e Cola completo.

## 3. Consultar cobrança

`GET /v1/orders/{providerOrderId}`

Resposta HTTP 200 com os mesmos campos da criação. Estados aceitos:

- `pending`: aguardando pagamento;
- `approved`: dinheiro confirmado pelo banco;
- `cancelled`, `canceled`, `expired` ou `refunded`: cobrança encerrada sem crédito.

Qualquer outro estado é bloqueado. Para liberar minutos, o TurboRama exige simultaneamente:

- `providerId` correto;
- `providerOrderId` correto;
- `externalReference` original;
- `amountCents` exatamente igual ao pacote;
- moeda `BRL`;
- estado `approved`.

## Erros

Use HTTP `400` para pedido inválido, `401`/`403` para credencial, `404` para cobrança inexistente, `409` para conflito e `429`/`5xx` para falha temporária. Corpo opcional:

```json
{ "message": "descrição curta sem dados secretos" }
```

Falhas temporárias são repetidas automaticamente com intervalo crescente. Credenciais, tokens bancários e chaves privadas nunca devem aparecer na resposta ou nos logs.

## Configuração

No instalador escolha `2 - Outro banco ou plataforma via adaptador TurboRama` e informe:

1. URL base, por exemplo `http://127.0.0.1:8765/`;
2. `providerId` do adaptador;
3. segredo Bearer compartilhado.

O instalador testa `/v1/health` antes de ativar o sistema.
