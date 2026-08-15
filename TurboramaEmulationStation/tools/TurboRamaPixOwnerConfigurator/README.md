# CONFIGURAR-USER-TOKEN-PIX

Programa portatil de manutencao do administrador LZ Games.

O programa:

- consulta a conta, as lojas e os PDVs reais diretamente no Mercado Pago;
- permite selecionar um unico PDV ativo;
- pode remover somente cadastros antigos gerenciados pelo TurboRama, mediante confirmacao;
- valida o Access Token e o PDV no servidor LZ Games;
- envia a credencial somente por HTTPS para `painelpix.lzgames.com.br`;
- usa um codigo bancario de uso unico, emitido pelo painel e valido por 15 minutos.

O programa nao depende da conta Windows do kiosk, nao para o agente PIX e nao
grava Access Token, `secret.dat` ou `owner-settings.json` no gabinete. O servidor
mantem apenas uma conexao Mercado Pago ativa por cliente; um novo cadastro
substitui a conexao anterior desse cliente.

Ele deve ficar somente com o administrador. Nao faz parte do payload instalado
no kiosk e deve ser removido da maquina depois da manutencao.

Autoteste local, sem rede e sem cobranca:

```text
CONFIGURAR-USER-TOKEN-PIX.exe --self-test
```
