# TurboRama Installer — fluxo original, interface Consumer

Projeto Windows Forms/.NET Framework 4.7.2, separado do protótipo Next. O executável completo executa o backend real. Não usa SimulationRunner, WebView para renderizar a interface, os temas Premium antigos ou controles sobrepostos.

## Fluxo preservado

Boas-vindas → Licença → Pré-requisitos → Instalação → Conclusão.

- Aceitação de licença obrigatória antes de avançar.
- Quando um diagnóstico atual confirma os componentes aplicáveis, a etapa de pré-requisitos pode ser dispensada, inclusive no retorno.
- Concluir a preparação não avança sozinho: aguarda Avançar.
- Escolhas ficam bloqueadas durante execução; mudar a seleção invalida o resultado anterior.
- A preparação não começa se o diagnóstico indicar menos de 2 GB livres no disco do Windows; isso é uma reserva inicial, não uma garantia do espaço necessário ao produto.
- A pasta selecionada é preservada ao voltar. Extração mantém a transação segura, não sobrescreve conteúdo existente e roda com token limitado.

## Verificação e compilação

1. Windows 10/11, Visual Studio 2022 com MSBuild e targeting pack .NET Framework 4.7.2.
2. Colocar os 20 payloads exatos de prerequisites.lock.json em resources/prerequisites. Os URLs são fontes, não autorização para aceitar hashes diferentes. URLs de atualização contínua podem mudar: nesse caso é necessária nova auditoria.
3. Executar Test-ConsumerUi.ps1 para testar fontes, fluxo, gráficos e extração sintética, sem instalar componentes.
4. Commitar os fontes numa branch própria e executar Build-Consumer.ps1. O build confere payloads, roda os testes, compila e verifica os recursos incorporados por hash. Seu manifesto liga EXE, catálogo e fontes ao commit.

O modo IncludePrerequisitePayloads=false produz somente DLL de validação, nunca um instalador incompleto.

## Componentes conferidos em 05/09/2026

- Visual C++ v14 x64/x86: 14.51.36247.0.
- .NET Desktop 8 x64/x86: 8.0.30 (versão de arquivo 8.0.30.36323).
- .NET Desktop 10 x64/x86: 10.0.11, preservado.
- WebView2: download Evergreen x64 atualizado, SHA-256 fixado no lock. A versão 1.3.265.7 é do instalador, não do motor WebView2.
- Bibliotecas legadas verificadas preservadas para compatibilidade. Pacotes opcionais não são instalados implicitamente.

Fontes oficiais: [Visual C++](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist), [.NET 8](https://dotnet.microsoft.com/en-us/download/dotnet/8.0), [.NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), [WebView2](https://developer.microsoft.com/en-us/microsoft-edge/webview2).

## Limites da entrega

É um candidato para testes, ainda não aprovado para produção. Testes de bitmap e controles não substituem verificar a janela real em diferentes escalas/DPI, nem instalar numa VM limpa.

O host sozinho não contém o produto TurboRama, jogos, ROMs ou BIOS. A instalação do produto exige as partes com os nomes esperados pelo backend e o sidecar SHA-256 correspondente a este novo EXE; sidecars de versões antigas serão rejeitados. Não reutilize nem renomeie um sidecar antigo.

O host não tem assinatura digital de editor. Pode haver alerta do SmartScreen; nenhuma proteção do Windows é desativada e não há garantia de ausência de alertas. Assinaturas dos instaladores Microsoft continuam sendo verificadas antes de executá-los.
