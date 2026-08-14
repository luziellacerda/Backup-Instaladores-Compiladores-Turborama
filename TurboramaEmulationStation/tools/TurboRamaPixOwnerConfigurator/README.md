# CONFIGURAR-USER-TOKEN-PIX

Configurador comercial do proprietário PIX com interface LZ Games.

Ele identifica a conta pelo Access Token, cria/reaproveita Loja e PDV do
Mercado Pago e grava o mesmo `owner-settings.json` consumido pelo
EmulationStation. Também configura provedores bancários que implementem o
contrato de adaptador TurboRama.

No gabinete atual, o programa deve ser executado pela conta `Admin` indicada
simultaneamente em `turborama.json` e no AutoLogon. A validação compara os SIDs
reais antes de transmitir a credencial.

`VER CADASTROS` consulta a conta sem modificar recursos. Quando existem vários
pares Loja/PDV, o programa marca `[ATUAL NESTE PC]` somente quando os IDs salvos
localmente correspondem exatamente ao inventário retornado. O operador pode:

- usar somente o par selecionado; ou
- usar o par selecionado e remover os outros pares gerenciados pelo TurboRama.

A segunda opção exige confirmação explícita. Ela preserva o par escolhido e
qualquer recurso que não use os prefixos `LZLOJA`/`LZPIX`. A exclusão segue os
endpoints oficiais: primeiro o PDV e somente depois uma loja antiga que tenha
ficado sem nenhum PDV.

O segredo é enviado ao agente por um pipe anônimo e não é gravado em JSON,
linha de comando ou log.

Autoteste:

```text
CONFIGURAR-USER-TOKEN-PIX.exe --self-test
```
