# TurboRama PIX Test

Protótipo local para validar o fluxo PIX sem tocar no EmulationStation, no `CreditManager`, no tema ou em qualquer saldo real.

## O que este teste faz

- mostra somente os pacotes de 15, 30, 45, 60 e 120 minutos;
- calcula preço por minuto configurável;
- cria uma cobrança **simulada** com identificador único;
- mostra um QR visual e código PIX **não pagável**;
- simula a confirmação do pagamento;
- grava um evento atômico em `runtime/pix/inbox/`;
- importa esse evento em um contador de demonstração;
- não duplica créditos já processados;
- mostra alertas somente ao cruzar 15 e 5 minutos;
- nunca lê nem grava os arquivos reais `arcade_credit.*`.

## Como executar

Forma mais simples: clique duas vezes em `INICIAR-TESTE-PIX.cmd`.

Ou, no PowerShell, dentro desta pasta:

```powershell
dotnet run
```

Depois abra:

```text
http://127.0.0.1:18888
```

Para testar os alertas rapidamente, escolha a velocidade `60×` ou `300×`, compre 30 minutos simulados e inicie o contador de teste.

## Onde ficam os dados de teste

Todos os dados ficam ao lado do executável de teste, em:

```text
runtime/
├─ transactions.json
└─ pix/
   ├─ inbox/
   │  └─ PIXTEST-....json
   └─ processed.json
```

É seguro apagar a pasta `runtime` com o teste fechado para recomeçar. Ela é ignorada pelo Git.

## Próxima integração com o EmulationStation

O projeto final manterá a mesma fila de eventos, mas o receptor será uma ponte mínima dentro de `CreditManager`:

1. o Pix Agent real grava um evento aprovado;
2. o EmulationStation importa cada `transactionId` uma única vez;
3. ele utiliza a rotina existente de adicionar minutos;
4. a importação também ocorre antes de descontar o tempo quando um jogo termina.

O teste usa somente o provedor `mock`. Não contém credencial bancária, token, chave PIX real ou API de pagamento.
