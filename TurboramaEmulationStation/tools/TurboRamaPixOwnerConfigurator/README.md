# CONFIGURAR-USER-TOKEN-PIX

Configurador comercial do proprietário PIX com interface LZ Games.

Ele identifica a conta pelo Access Token, cria/reaproveita Loja e PDV do
Mercado Pago e grava o mesmo `owner-settings.json` consumido pelo
EmulationStation. Também configura provedores bancários que implementem o
contrato de adaptador TurboRama.

O segredo é enviado ao agente por um pipe anônimo e não é gravado em JSON,
linha de comando ou log.

Autoteste:

```text
CONFIGURAR-USER-TOKEN-PIX.exe --self-test
```
