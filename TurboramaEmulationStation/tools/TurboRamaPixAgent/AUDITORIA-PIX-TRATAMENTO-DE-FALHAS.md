# Auditoria PIX e tratamento de falhas — TurboRama

Data da auditoria: 1 de agosto de 2026

## Resultado honesto

O núcleo local do PIX foi reforçado, compilado e testado contra adulteração, duplicidade, falha de disco, chave ausente, sessão falsa, duas instâncias e respostas bancárias divergentes.

Ele ainda **não deve ser colocado em produção** pelos seguintes motivos:

1. a tela para o cliente escolher 15/30/45/60/120 minutos, criar o pedido e exibir o QR ainda não está integrada ao menu do EmulationStation;
2. a integração não foi testada com um Access Token e um `ExternalPosId` reais do Mercado Pago;
3. o executável reforçado ainda não foi instalado sobre `D:\emulationstation`;
4. o agente ainda precisa ser instalado como processo iniciado automaticamente pelo quiosque.

Nenhuma cobrança real foi criada durante esta auditoria.

## Fluxo protegido

1. A interface grava uma solicitação atômica em `pix/requests`.
2. O agente confere identificador, horário, pacote e preço definido no servidor.
3. O agente cria uma order PIX dinâmica no Mercado Pago com chave de idempotência.
4. O agente consulta a order até o provedor informar `processed` e `accredited`.
5. Order, referência externa, BRL e valor são comparados com a solicitação original.
6. Somente na mesma execução que validou o provedor é criado um evento de crédito HMAC-SHA256.
7. O EmulationStation verifica assinatura, nome, identificador, horário e provedor.
8. O `CreditManager` grava primeiro o ledger principal e só então finaliza o evento.
9. O identificador fica no ledger e o comprovante fica em `pix/processed`, criando duas barreiras contra crédito duplicado.

## Falhas corrigidas

### Críticas

- O valor enviado pelo cliente era confiado. Agora o preço vem de `PackagePricesCents`; qualquer divergência é rejeitada.
- Um JSON local podia liberar minutos sem prova do pagamento. Agora todo evento precisa de assinatura HMAC-SHA256 válida.
- Uma sessão local alterada para `approved` podia ser assinada. Agora o agente volta a consultar o provedor; não confia no estado local.
- O mapeamento do Mercado Pago tratava estados incorretamente. Agora aprovação exige order `processed` e detalhe/pagamento `accredited`.
- A resposta remota não era conferida integralmente. Agora são obrigatórios order, referência, moeda BRL, total e pagamento com o valor exato.
- Falha ao salvar o ledger podia ser confundida com sucesso. Agora o conteúdo salvo é verificado; o crédito é desfeito em memória e o evento permanece para nova tentativa.

### Altas

- Duas instâncias do agente podiam concorrer. Agora há bloqueio exclusivo por pasta PIX.
- Falta de chave local podia rejeitar definitivamente pagamento válido. Agora os eventos permanecem intactos até a chave voltar.
- Evento processado podia ser reaplicado após um ledger extremamente antigo. Agora o comprovante processado é uma segunda barreira permanente.
- Arquivo falso pré-criado podia bloquear publicação legítima. A confirmação validada substitui atomicamente esse arquivo.
- Provedor desconhecido podia cair em modo simulado. Agora configurações desconhecidas são recusadas.
- Modo Mercado Pago fica bloqueado enquanto `ProductionEnabled` não for explicitamente ativado.
- Crédito `mock` não é aceito pelo EmulationStation sem o marcador deliberado `allow-mock-credit`.

### Operacionais

- Requisições antigas, IDs inválidos, valores adulterados e JSON corrompido são isolados em `rejected` com motivo.
- Falhas de rede usam timeout e retomada com espera exponencial limitada.
- QR, sessões, eventos, token e chave usam gravação atômica/flush quando aplicável.
- Token é digitado sem aparecer na tela e protegido pelo DPAPI do Windows.
- Comandos desconhecidos são recusados para evitar iniciar o agente continuamente por erro de digitação.
- Logs diários são gravados em `pix/logs`, giram aos 5 MB e são mantidos por 30 dias.
- Dados de teste, token, chave e arquivos de execução foram excluídos do Git.

## Testes executados

| Teste | Resultado observado |
|---|---|
| Compilação do agente .NET | 0 erros e 0 avisos |
| Compilação completa do EmulationStation | concluída e vinculada |
| Preço adulterado (R$ 0,01 por 15 min) | rejeitado; nenhuma sessão criada |
| Estado local forjado como aprovado | voltou a pendente; nenhum crédito criado |
| Sessão com valor adulterado | `security_error`; nenhum crédito criado |
| JSON de sessão quebrado | retirado da fila e isolado |
| Evento assinado válido no verificador C++ | aceito |
| Minutos/valor alterados após assinatura | rejeitados |
| Nome do arquivo diferente do ID assinado | rejeitado |
| Primeiro crédito de 15 minutos | saldo passou para 900 segundos |
| Mesmo evento reapresentado | saldo permaneceu em 900 segundos |
| Chave ausente | saldo não mudou e evento permaneceu na fila |
| Chave restaurada | evento foi aplicado; saldo aumentou corretamente |
| Arquivo de ledger bloqueado | crédito não aplicado e evento preservado |
| Ledger liberado | crédito aplicado na tentativa seguinte |
| Segunda instância do agente | recusada com código 12 |
| Provedor inexistente | recusado com código 10 |
| Produção sem liberação explícita | bloqueada com código 10 |
| Estados bancários simulados | criado=pendente; processado/acreditado=aprovado; cancelado=cancelado |
| Resposta bancária com valor divergente | bloqueada |

Os testes usaram pastas isoladas dentro de `test-results`; nenhum saldo da instalação em `D:` foi alterado.

## Riscos que nenhuma aplicação local consegue eliminar sozinha

- Um administrador do Windows, ou alguém com controle total da mesma conta do quiosque, consegue ler/modificar arquivos e processos locais. O quiosque deve impedir acesso ao Explorer, terminal, Gerenciador de Tarefas e pastas de dados.
- Relógio e certificados raiz do Windows precisam estar corretos para TLS e validação de horários.
- Indisponibilidade do Mercado Pago impede novas confirmações, mas não deve liberar crédito nem apagar solicitações.
- Limites, cadastro comercial, PDV e permissões são definidos pela conta Mercado Pago e precisam ser validados em teste real.

## Próximo teste obrigatório antes de produção

1. Cadastrar/confirmar um PDV dinâmico e preencher `MercadoPago.ExternalPosId`.
2. Salvar o Access Token localmente com `--set-token`; nunca colocá-lo no JSON ou no Git.
3. Fazer uma cobrança de menor valor em ambiente controlado.
4. Confirmar no log: order criada, valor/referência validados, crédito aplicado uma vez e comprovante movido para `processed`.
5. Testar cancelamento, expiração, internet desligada durante o pagamento e reinício do Windows após pagamento.

## Referência oficial usada

- https://www.mercadopago.com.br/developers/pt/docs/qr-code/payment-processing
- https://www.mercadopago.com.br/developers/pt/docs/qr-code/migrate-dynamic-qr-model-to-orders
- https://www.mercadopago.com.br/developers/pt/reference/in-person-payments/qr-code/orders/create-order/post
