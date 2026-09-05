# TurboRama Next — projeto novo

Este projeto começa com arquivos novos. Não inclui nem referencia MainForm,
controles Designer, TurboramaPremiumUi, TurboramaPremiumTheme ou assemblies do
InstallerHost anterior. Nenhum controle antigo é escondido, reparentado ou decorado.

## Marco atual: interface executável para avaliação

- Navegação, seleção de componentes, perfis e revisão do plano são reais.
- Diagnóstico de hardware e registro é somente leitura.
- A execução disponível é explicitamente uma **simulação**. Não há download,
  instalação, extração de produto ou elevação neste executável.
- Não afirma que o computador executará todos os jogos/emuladores.
- Esta versão não substitui nem atualiza a release anterior.

## Fronteiras

`ShellForm` possui navegação, foco e estado ocupado. `SetupSession` armazena a
seleção e invalida o plano ao alterar opções. Páginas não executam instaladores.
`ReadinessService` fornece diagnóstico cancelável. `IPlanRunner` permite testar
progresso, falha e repetição com execução simulada claramente identificada.

O provisionamento real e a extração do produto precisarão de implementação e
auditoria próprias antes de liberar um instalador completo. A compilação desta
prévia é necessária para testar os controles reais; não é um aceite de produção.

## Reproduzir os testes

Em PowerShell, execute `./Test-Preview.ps1`. O script usa o MSBuild instalado e o
compilador do .NET Framework para compilar o projeto novo e seus testes de estado,
controles e renderização. Aceita `-MSBuildPath` para outra instalação do Visual
Studio. Não instala SDKs, pacotes ou aplicações, não eleva, não publica e não muda
configurações de segurança. As imagens de escala são simulações geométricas, não
certificação de DPI nativo em vários monitores. Testes com serviço falso não
comprovam uma instalação real.

Orçamento desta etapa: sem contratação de assinatura. Um certificado não é
requisito para desenvolver/testar; a prévia continua sem assinatura. Não há
promessa de eliminar o aviso do SmartScreen, nem instrução para contorná-lo.
