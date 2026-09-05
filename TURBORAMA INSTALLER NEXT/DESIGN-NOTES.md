# Acabamento dos controles — setembro de 2026

## Referências consultadas

- [Microsoft Fluent 2: Button](https://fluent2.microsoft.design/components/web/react/core/button/usage): hierarquia entre ação primária, secundária e discreta; uma ação primária por tela; contraste e indicação de estados.
- [Microsoft Fluent 2: Material](https://fluent2.microsoft.design/material): superfícies sólidas e profundidade distinguem áreas; Acrylic e Mica têm usos e limitações próprios.
- [Google Material 3: States](https://m3.material.io/foundations/interaction/states/overview): estados normal, hover, foco, pressionado e desabilitado devem ser consistentes.
- [Microsoft: Buttons](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/buttons): rótulos claros, ícones e interação por teclado; espaço para conteúdo dinâmico.
- [Microsoft: client-area animation](https://learn.microsoft.com/en-us/windows/win32/winauto/client-area-animation): a consulta `SPI_GETCLIENTAREAANIMATION` permite respeitar animações desabilitadas. O programa somente lê essa preferência.

Não existe um vencedor objetivo de “melhor gráfico do mercado”. Para esta prévia,
a decisão é uma linguagem desktop discreta, com acento neon restrito à ação
principal e à seleção. Não se trata de WinUI, Mica ou Acrylic real: são controles
WinForms do projeto novo, com desenho vetorial próprio e superfícies sólidas.

## Implementação

- `ActionButton`: material em gradiente leve, bordas arredondadas antialias,
  sombra discreta, brilho superior, foco externo e estados de mouse/teclado.
- `VectorIcon`: caminhos vetoriais originais, sem ícones raster ou fontes de
  símbolos que possam faltar no PC de destino.
- Navegação discreta com ícone, superfície selecionada e marcador adicional.
- Cartões compostos de perfil: toda a superfície é acionável, com nome acessível.
- Filtros segmentados por grupo, com seleção acessível; filtrar não modifica o plano.
- Uma única ação principal na visão geral; Enter aciona o botão visível do hero.
- Atualizar/cancelar diagnóstico usam o mesmo componente, sem renderer paralelo.
- Preferência de movimento reduzido e cores de alto contraste consideradas
  sem alterar configuração alguma do Windows. Validação nativa completa de
  acessibilidade e DPI continua pendente; renderização simulada não a substitui.

Nenhuma biblioteca paga, fonte baixada, pacote NuGet ou serviço externo foi
adicionado. Não há mudanças no diagnóstico, na seleção ou no runner simulado.
Esta revisão não implementa instaladores reais e não muda o estado de assinatura.
