# TurboRama PIX Comercial v13

## Instalação

1. Copie `INSTALAR-TURBORAMA-PIX-COMERCIAL-v13.exe` para o computador da máquina.
2. Confirme que o TurboRama existente está em `D:\emulationstation\emulationstation.exe`.
3. Abra o instalador e confirme a instalação.
4. O instalador fecha somente o EmulationStation e o agente PIX. Ele não desliga nem reinicia o computador.
5. ROMs, temas, créditos e configurações existentes são preservados. Um backup é criado automaticamente em `D:\emulationstation\backups`.

## Cadastro do proprietário — protegido por senha

1. Abra o EmulationStation.
2. Pressione **START**.
3. Digite a senha administrativa já usada no sistema.
4. Entre em **CONFIGURAÇÃO PIX DO PROPRIETÁRIO**.
5. Cadastre os dados do estabelecimento, User ID do Mercado Pago, identificadores da loja e do caixa/PDV, endereço, preços e Access Token.
6. Salve e ative. Os dados permanecem salvos depois de desligar ou reiniciar o computador.

O Access Token é protegido pelo Windows para o usuário que fez o cadastro. Use um token que comece com `APP_USR`. O User ID, o número da aplicação e a Public Key não substituem o Access Token.

## Compra do cliente — livre, sem senha

1. O cliente pressiona **SELECT** em qualquer tela normal do EmulationStation.
2. Escolhe 15, 30, 45, 60 ou 120 minutos, conforme os valores cadastrados pelo proprietário.
3. O sistema exibe o QR Code PIX e acompanha o pagamento.
4. O tempo só é liberado depois que o provedor confirmar o pagamento aprovado.
5. A mesma cobrança não adiciona créditos duas vezes.

O cliente não consegue abrir a configuração do proprietário pelo botão SELECT.

## Observações importantes

- Não misture credenciais de teste com produção. Para receber pagamentos reais, configure o Access Token de produção da conta proprietária.
- Antes de vender ou instalar comercialmente, troque qualquer Access Token que já tenha sido enviado em conversa, captura de tela ou arquivo não protegido.
- Este executável ainda não possui assinatura digital comercial. O Windows pode mostrar um aviso de editor desconhecido.

