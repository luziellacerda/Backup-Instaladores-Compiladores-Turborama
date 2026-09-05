# EmulationStation Suite 1.1.1 — nova tela, mesma ativação

Esta edição integra a nova tela de entrada ao contrato compartilhado da versão
1.1.0. O módulo de acesso continua embutido no `emulationstation.exe`; não há
outro programa de ativação para distribuir ao cliente.

## Entrada

- Informe uma vez o identificador TS que já está ativado no TurboRama Suite,
  usando o mesmo computador e a mesma conta Windows daquela ativação.
- A tela tem um único campo, botões Entrar/Sair e mensagens no mesmo tema.
- O identificador guardado com DPAPI permite tentar a confirmação do servidor
  antes de exibir a tela. Cache não concede acesso nem substitui a prova CNG.
- Falha de servidor, problema de identidade e conflito de sessão têm mensagens
  distintas. A interface não exibe exceções brutas nem inventa nova ativação.
- Quando houver conflito, somente o administrador autorizado pode encerrar a
  sessão EmulationStation selecionada no painel; a sessão Suite é independente.

## Segurança e compatibilidade preservadas

A identidade CNG existente, TLS, autoridades, quatro Kind ES assinados,
anti-replay, expiração, DPAPI, ponte IPC e integridade do helper permanecem
obrigatórios. Não existe licença offline, chave embutida de cliente ou fallback
para consumir a sessão original da Suite.

A coleta complementar de rede continua no contrato autenticado da versão 1.1.0,
limitada a oito interfaces e fora do heartbeat. Falha de telemetria não cancela
a licença. IP é exclusivamente informativo e não provoca bloqueio ou reativação.

Esta atualização da interface preserva os reparos de áudio, memória, temas e
execução de jogos da base sem serviços. Não altera regras PIX, pagamentos ou
locadora nem publica artefatos nas releases das outras edições.

## Validação e distribuição

O workflow próprio compila o Windows x64 e produz EXE, ZIP portátil, ZIP de
atualização e hashes vinculados ao commit. Uma compilação com resultado
`success` ainda pode conter avisos: consultar também as anotações do run.

Até a confirmação do servidor compatível e do teste no Windows já ativado, o
pacote é candidato de integração. A release para uso continua condicionada à
execução manual com `publish_release=true` depois dessa homologação. Testes
sintéticos e de CI não comprovam ativação real nem implantação de produção.

Não desative o Defender, crie exclusões ou restaure itens detectados para testar.
Problemas de servidor não devem ser contornados recriando a chave da Suite.
