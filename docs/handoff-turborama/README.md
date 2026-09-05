# Handoff técnico TurboRama — duas versões

Tutorial de construção, funcionamento, alterações, testes e entrega para manutenção humana. Data de referência: **5 de setembro de 2026**.

## Comece aqui

| Se você quer… | Leia |
|---|---|
| Entender o escopo e a numeração linha a linha | [00 — Como ler](00-COMO-LER.md) |
| Saber de onde vieram as versões e quais commits/releases são reais | [01 — Histórico e estado](01-HISTORICO-E-ESTADO.md) |
| Entender a construção e a primeira inicialização | [02 — Arquitetura e tema](02-ARQUITETURA-E-INICIALIZACAO.md) |
| Entender as otimizações que devem ser preservadas | [02A — Memória e vídeos](02A-MEMORIA-E-VIDEOS.md) |
| Manter a versão sem PIX/locadora/contabilidade/tempo | [03 — Cliente sem serviços](03-VERSAO-SEM-SERVICOS.md) |
| Manter a versão PIX e entender a correção de áudio | [04 — PIX e áudio](04-VERSAO-PIX-E-AUDIO.md) |
| Compilar no GitHub e publicar sem misturar variantes | [05 — Construção e releases](05-COMPILACAO-E-RELEASES.md) |
| Testar, instalar, diagnosticar, reverter e continuar | [06 — Testes e manutenção](06-TESTES-E-MANUTENCAO.md) |
| Conferir literalmente cada alteração de código | [42 anexos numerados](anexos/linhas/README.md) |
| Ver como a documentação foi conferida | [Validação do handoff](VALIDACAO-DO-HANDOFF.md) |

## O que está documentado

- Base de otimizações: 0e02780; correção de inicialização/tema: 5414039; base comum de CI: 76b2148.
- Cliente sem serviços: 947dad45f5e9cd556cce6f15045a5dd6119bdf95.
- PIX com áudio/EXE avulso: 476e06179f89ac209ff808dffb27555d740f93d2.
- 42 comparações de arquivos, envolvendo 33 caminhos-fonte distintos, 203 trechos e todas as 4.682 linhas adicionadas/649 removidas nesses intervalos.
- Links permanentes para fonte, histórico, testes e entregas; números de linha fixados nas revisões indicadas.

As explicações são organizadas por arquivo, função e bloco lógico. Os anexos apresentam o código literal com números antes/depois, sem omitir linhas alteradas. Bibliotecas de terceiros e toda a implementação histórica do servidor não são reexplicadas linha a linha: o escopo e os limites estão no capítulo 00.

## Alertas importantes

**As duas versões não têm paridade completa de áudio.** A PIX contém a espera pela liberação VLC e o reparador RetroArch. O cliente 947dad4 preserva as otimizações herdadas, mas ainda não contém essa nova espera C++ nem o reparador no pacote. Este handoff registra a diferença; não modifica código para eliminá-la.

**O ecossistema PIX não foi reconstruído nem reconfigurado neste handoff.** O frontend continua integrado ao agente/servidor existentes. Testes sintéticos e rejeição de agente ausente não substituem homologação real.

**Um EXE não é uma instalação completa.** Preserve DLLs, plugins, recursos, configurações e os arquivos próprios do servidor. As tags de release são móveis; registre commit, execução e hash do arquivo para rastreabilidade.

## Natureza desta entrega

Este diretório é documentação, replicada nas duas branches para permitir comparação. Não altera fontes, workflows, emuladores ou dados do servidor; não recompila nem substitui os EXEs já publicados. A revisão técnica do programa descrita acima continua sendo a referência mesmo depois dos commits que adicionam o manual.
