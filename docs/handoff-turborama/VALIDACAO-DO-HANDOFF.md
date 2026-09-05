# Validação desta documentação

[Início](README.md)

Verificação efetuada em 5 de setembro de 2026. Este relatório valida a documentação; não representa uma nova compilação ou homologação do programa.

- 42 anexos de comparação presentes, para 33 caminhos-fonte distintos.
- 203 trechos de diff; 4.682 adições e 649 remoções, conferidas com git diff --numstat para cada arquivo e intervalo.
- 7.096 linhas literais de código/contexto comparadas ao conteúdo Git das revisões anterior/resultante; 71 blobs de fonte consultados nessa verificação. Nenhuma divergência.
- 576 referências de linhas em links permanentes verificadas quanto à existência do arquivo e aos limites dos intervalos; 91 combinações de revisão/arquivo consultadas.
- Links internos conferidos e blocos de código fechados. Links para a revisão anterior de arquivos novos foram substituídos por indicação explícita de que o arquivo ainda não existia.
- Revisão humana cruzada corrigiu três imprecisões: --parallel 1 não elimina /MP; a pasta irmã de bibliotecas tem precedência no CMake; o mutex Local do tema coordena a mesma sessão Windows, não todas as sessões.
- Corrigida a contagem de testes novos PIX e uma referência que ultrapassava o final do teste de compatibilidade de lançamento.
- Busca por padrões comuns de tokens/chaves privadas não encontrou esses padrões na documentação. Isso é uma checagem auxiliar, não auditoria universal de segredos.
- A documentação mantém as diferenças reais entre PIX e cliente, especialmente a ausência da nova barreira de áudio e do reparador no cliente947dad4.

Os anexos preservam literalmente os espaços das linhas de fonte. A checagem Git de formatação desconsiderou espaços finais/linhas finais desses registros literais; não alterou os trechos para maquiar diferenças.

As contagens de links internos podem crescer quando este relatório é ligado ao índice. As contagens de código/diffs são fixadas pelo manifesto.json e pelos commits documentados. Validar existência e intervalo de um link não prova, sozinho, a interpretação humana da função; por isso foi feita também leitura e revisão dos capítulos.

Escopo de publicação: somente docs/handoff-turborama, com o mesmo conteúdo nas duas branches. Fontes, workflows, tags de executáveis e configurações de operação não fazem parte desses commits de documentação.
