# EmulationStation Suite 1.1.0 — candidato de integração

Esta versão usa o identificador TS e a chave CNG já ativados na mesma conta
Windows. A Suite e o EmulationStation mantêm sessões independentes.

O acesso usa `v1/suite/challenges` e `v1/suite/sessions` com um único cabeçalho
`X-TurboRama-Client: EMULATIONSTATION`. O cliente verifica os quatro novos tipos
de resposta ES assinada antes de assinar o desafio ou aceitar uma autorização.
Um servidor antigo, cabeçalho removido ou resposta incompatível impede a abertura.

Se já houver uma sessão ES vigente, a resposta assinada CONFLICT não concede
acesso. Um administrador deve confirmar o encerramento da sessão selecionada
no painel existente; depois o usuário tenta abrir novamente. Clientes dedicados
1.0.1 conservam a política antiga de substituição, uma limitação desse legado.

A telemetria complementar informa até oito interfaces físicas ativas e o IP
observado pelo servidor. A coleta inicial e por alteração tem intervalo mínimo
de um minuto, fora do heartbeat. As provas usam contrato próprio, a chave já
existente e vinculação à sessão autorizada. Uma falha nessa coleta não cancela
uma sessão válida. IP e mudança isolada de MAC não bloqueiam o cliente.

O servidor armazena os endereços cifrados e mostra dados mascarados ao
administrador autorizado. A retenção padrão é 30 dias, configurável no servidor.
O inventário original e o fingerprint permanecem independentes desse complemento.

O workflow entrega EXE, ZIP portátil, ZIP de atualização e SHA-256 como artefatos
de CI associados ao commit. A publicação da release exige execução manual com
`publish_release=true` depois da homologação do servidor de destino. Um artefato
de CI é candidato, não comprovação de implantação ou teste num PC real.

Ponte nativa, integridade do helper, cache DPAPI somente do identificador,
expiração online e retorno dos jogos continuam sujeitos aos testes existentes.
Áudio, memória, temas e código dos jogos conservam a base verificada pelo teste
de preservação. Validação interativa em Windows ainda é necessária para homologar.
