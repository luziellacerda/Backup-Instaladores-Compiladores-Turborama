# EmulationStation Suite 1.1.2 — fechar e validar novamente

Edição Cliente Suite Ativado, sem PIX e sem serviços comerciais. O ES usa a
ativação já existente no mesmo computador e conta Windows. **O programa
TurboRama Suite não precisa estar aberto.** Nenhuma ativação, chave ou licença
nova é criada por esta edição.

## Comportamento

- Ao abrir, o ES valida diretamente no servidor com a chave CNG existente.
  O identificador TS pode ser lembrado com DPAPI, mas isso não armazena acesso.
- Ao fechar a tela de entrada com Sair ou X, a ponte informa cancelamento:
  o programa termina sem apresentar um segundo erro de ativação.
- Ao fechar o ES, sua autorização local é revogada e seus trabalhos de rede
  são cancelados. O módulo integrado tem prazo limitado para sair; se travar,
  seu Job Object é encerrado. Processos de emuladores e da Suite não são alvo.
- A janela de entrada e o módulo usam o mesmo ícone já existente do ES.
- Respostas de login que chegam depois do fechamento não publicam autorização.

## Reabrir como a TurboRama Suite

A correção correspondente do servidor troca a sessão ES anterior da mesma
licença/dispositivo somente depois de uma nova prova válida. O identificador da
sessão anterior não pode renovar a autorização; a sessão da Suite e os demais
clientes continuam separados. Não há espera deliberada de 180 segundos para
reabrir nem necessidade de encerramento manual no painel em um uso normal.

Essa política exige a correção do servidor em produção. O servidor anterior
(`34e31f2`) ainda rejeita uma nova abertura enquanto a sessão antiga é válida:
atualizar só o EXE não corrige essa regra. O protocolo compartilhado, seus quatro
tipos de resposta assinada e as rotas existentes permanecem os mesmos. Não foi
criada rota de fechamento ou migration adicional para esta correção.

O TTL continua sendo uma proteção para falha de rede/travamento. Sessões antigas
não são fechadas instantaneamente em outro processo: deixam de renovar e o
cliente mantém os limites de validade já existentes. Não existe acesso offline,
troca automática de computador, exceção por IP ou substituição da chave CNG.

## Preservação e testes

Menus sem serviços, áudio, memória, temas, vídeos e lançamento dos jogos são
preservados. As outras edições PIX e Cliente Sem Serviços não são alteradas.
Os testes usam identidades sintéticas e incluem cancelamento, EOF, módulo
travado, retorno antecipado, fechamento durante login, heartbeat em andamento,
resposta tardia, reabertura com nova prova e integridade do módulo.

O pacote permanece candidato até a CI e o teste conjunto no Windows com o
servidor atualizado. A publicação de release continua sendo uma ação manual
separada. O EXE não tem Authenticode; não desative o Defender ou crie exclusões.
O alerta Windows sobre `ms-gamingoverlay` não é uma solicitação de ativação Suite;
as configurações ou aplicativos do Windows não são modificados por esta correção.
