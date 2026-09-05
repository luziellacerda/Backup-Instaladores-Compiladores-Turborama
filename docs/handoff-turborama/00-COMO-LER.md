# Como ler e conferir este handoff

[Voltar ao início](README.md)

## 1. O que este material entrega

Este é o handoff da construção, separação, correções e distribuição de duas variantes do TurboRama EmulationStation: CLIENTE SEM SERVIÇOS e PIX-SERVIDOR-CONTADOR. A fotografia técnica é de 5 de setembro de 2026.

O manual acompanha o caminho completo dentro desse escopo: fonte, CMake, tema embutido, inicialização, navegação, vídeos/memória, lançamento do emulador, fronteira PIX, testes, GitHub Actions, release e instalação. Os anexos reproduzem todas as linhas adicionadas/removidas nas quatro comparações declaradas abaixo, com contexto e números verificáveis.

Isso não é uma explicação de cada linha de todas as bibliotecas de terceiros nem uma auditoria integral do servidor de pagamentos. O código herdado é explicado nos pontos necessários para compreender essas mudanças. Não há promessa de que todos os bugs do produto foram encontrados. A distinção evita transformar uma documentação verificável em uma afirmação falsa de cobertura total.

## 2. Revisões congeladas

| Papel | Revisão |
|---|---|
| Antes da otimização de vídeo | 6f6b8b8372610fc2abe1e137d99a48c3ec52412e |
| Otimização comum herdada | 0e02780b761cb488c591416d2986130efcc166dd |
| Base comum das duas variantes | 76b214874973fe24017823401216896f3d7a6f40 |
| Cliente sem serviços documentado | 947dad45f5e9cd556cce6f15045a5dd6119bdf95 |
| PIX documentado, incluindo entrega do EXE | 476e06179f89ac209ff808dffb27555d740f93d2 |

Os commits que adicionam este manual vêm depois dessas revisões, mas não modificam o programa. Números de linha dos capítulos são das revisões expressamente citadas, não de uma branch móvel futura.

## 3. Ordem de estudo

1. Leia o [histórico](01-HISTORICO-E-ESTADO.md) para saber qual versão é qual.
2. Leia a [arquitetura e inicialização](02-ARQUITETURA-E-INICIALIZACAO.md).
3. Entenda [memória e vídeos](02A-MEMORIA-E-VIDEOS.md) antes de remover caches ou mudar o VLC.
4. Siga a versão [sem serviços](03-VERSAO-SEM-SERVICOS.md) ou [PIX e áudio](04-VERSAO-PIX-E-AUDIO.md).
5. Use [construção e publicação](05-COMPILACAO-E-RELEASES.md) para reproduzir a entrega.
6. Execute o roteiro de [testes, atualização e manutenção](06-TESTES-E-MANUTENCAO.md).
7. Abra os [anexos linha a linha](anexos/linhas/README.md) ao estudar um arquivo específico.

## 4. Como ler uma linha do anexo

~~~text
ANTES | DEPOIS |   CÓDIGO
   10 |     10 |   uma linha mantida
   11 |        | - linha que existia antes
      |     11 | + linha que existe depois
~~~

A coluna vazia não significa linha desconhecida: significa que a linha só pertence a uma das duas revisões. Linhas com espaço são contexto. Cada trecho contém links separados para o arquivo anterior e o resultante. Os números começam em 1.

Quando um arquivo inteiro é novo, todas suas linhas aparecem como adição. Quando algo foi substituído, a versão antiga aparece com menos e a nova com mais. Os contadores ao final permitem confrontar o anexo com git diff --numstat.

As explicações humanas ficam nos capítulos, por função e bloco lógico. A listagem numerada é a evidência literal correspondente. Não confunda um comentário automático sobre sintaxe com explicação de intenção: para entender por que existe um mutex, leia a seção de concorrência; para conferir exatamente onde ele entrou, use o anexo.

## 5. Vocabulário mínimo

| Termo | Significado neste projeto |
|---|---|
| Fonte | Arquivos C++, cabeçalhos, CMake e scripts; ainda não são o programa executável |
| Commit | Fotografia identificada pelo hash Git; fixa conteúdo, mas não prova teste |
| Branch | Ponteiro móvel para uma linha de desenvolvimento |
| Tag de release | Ponteiro usado para identificar a fonte distribuída; os workflows atuais o atualizam |
| Workflow | Receita YAML que o GitHub executa |
| Runner | Computador temporário do GitHub; aqui executa Windows e Visual Studio |
| Build/compilação | Tradução e ligação dos fontes para gerar o EXE |
| DLL | Biblioteca necessária ao EXE durante a execução |
| Frontend | Interface de seleção de jogos; o EmulationStation não é o emulador |
| Player VLC | Instância que toca os vídeos do menu; é distinta do áudio do jogo |
| Cache | Recurso guardado para reutilização; não é automaticamente vazamento |
| Pool | Conjunto reutilizável de objetos/buffers com limites |
| Callback | Função chamada por outro componente, possivelmente em outra thread |
| Mutex | Exclusão mútua para proteger estado compartilhado |
| Condition variable | Espera por uma condição de estado sem ficar consultando em laço ocupado |
| Idempotência | Repetir a operação já aplicada não produz novas mudanças |
| Smoke test | Teste curto de execução; não equivale a uso real completo |
| Hash SHA-256 | Identificador do conteúdo do arquivo; não substitui assinatura/autenticidade |

## 6. Regras para não destruir a separação

- Um diretório de build por variante. Nunca reaproveitar CMakeCache.txt entre PIX e cliente.
- Uma branch, um workflow e uma tag de entrega por variante.
- Não copiar o EXE sem serviços sobre uma instalação que deve continuar PIX.
- Não remover recursos de desempenho para retirar cobrança: essas são responsabilidades diferentes.
- Não copiar configurações reais de credenciais, licença, saldo ou servidor para exemplos de documentação.
- Não confundir teste que rejeita agente ausente com teste de integração com um agente válido.
- Correção aplicada a uma branch não aparece automaticamente na outra.

## 7. Limites já conhecidos

A PIX contém a espera de áudio adicionada em 7de017c. O cliente 947dad4 não contém essa espera nem o reparador no pacote: a diferença é documentada, não corrigida neste commit de handoff. O reparo de configurações locais RetroArch foi outra operação, fora do código Git.

Os testes automatizados aprovados não comprovam som em um jogo real, pagamento real, certificação comercial nem compatibilidade com todas as máquinas. Consulte a matriz de validação antes de declarar uma versão aprovada em produção.
