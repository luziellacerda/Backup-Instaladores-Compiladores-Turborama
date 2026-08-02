# TurboRama PIX — relatório de validação da versão comercial

Data: 01/08/2026

## Resultado automatizado

- Compilação do EmulationStation: concluída.
- Compilação do agente Windows: concluída com 0 avisos e 0 erros.
- Autoteste criptográfico do agente: aprovado.
- Publicação segura da tabela com 5 pacotes: aprovada.
- Bloqueio de preço adulterado: aprovado.
- Criação atômica do pedido, inclusive em caminho longo do Windows: aprovada.
- Geração e validação do arquivo PNG do QR: aprovada.
- Confirmação assinada do pagamento simulado: aprovada.
- Persistência de 15 minutos (`remainingSeconds=900`): aprovada.
- Bloqueio da repetição do mesmo pagamento: aprovado, sem alteração do saldo.
- Isolamento de pagamento simulado sem autorização explícita: aprovado.
- Mercado Pago sem token: corretamente marcado como indisponível (`ready=false`).
- Verificação do provedor sem token: corretamente recusada.
- Provedor adicional por adaptador bancário: compilação e contrato aprovados.
- Autenticação Bearer e idempotência obrigatória do adaptador: aprovadas.
- Validação de banco, referência, ordem, moeda, valor e estado: aprovada.
- Resposta aprovada do adaptador: crédito somente após nova consulta autenticada.
- Adaptador remoto usando HTTP sem TLS: corretamente bloqueado.

## Fluxo entregue

O cliente abre `START > COMPRAR TEMPO COM PIX`, escolhe um pacote, confirma o valor, lê o QR e aguarda. A tela diferencia geração, espera, confirmação, persistência, expiração, cancelamento e erro. O sucesso só aparece depois que o crédito foi realmente gravado.

## Validação obrigatória na instalação real

O código e o fluxo local estão validados, mas uma transação monetária real depende do Access Token de produção e do identificador real do PDV do estabelecimento. Para outro banco, depende da API e das credenciais reais desse banco por meio do contrato `CONTRATO-ADAPTADOR-BANCARIO.md`. O instalador testa a credencial antes de iniciar o serviço. Depois da configuração, o operador deve pagar uma compra real do menor pacote e conferir o recebimento na própria conta antes de liberar a máquina ao público.

Nenhum Access Token é incluído neste pacote ou no código-fonte.
