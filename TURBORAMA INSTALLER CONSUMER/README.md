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

Desde 10.1.12, a etapa Instalação permite concluir somente a preparação de dependências, sem pasta de destino nem pacote do produto. Para instalar também os arquivos do TurboRama, marque a opção explícita nessa mesma etapa. Quando existem partes `.pkg` ou o sidecar `.exe.sha256.txt` junto do EXE, inclusive incompletos, essa opção inicia marcada e a validação completa permanece obrigatória. O checksum de entrega `.exe.sha256` não é um pacote. A conclusão de dependências não afirma que o produto foi instalado nem que todo o PC foi reparado.

Uma instalação nova sugere a primeira pasta vazia entre `TurboRama`, `TurboRama-2` até `TurboRama-100`, sem criar ou alterar diretórios nessa consulta. O destino escolhido pelo usuário não é trocado ao voltar. A instalação verifica novamente o destino e nunca atualiza uma pasta ocupada por sobrescrita.

Desde 10.1.13, a extração também trata o token de identificação insuficiente que algumas execuções elevadas herdam no Windows. Quando o token UAC vinculado não pode representar o usuário, o instalador cria um token LUA do mesmo usuário, remove privilégios administrativos, reduz a integridade para Medium e confirma essas condições antes de gravar. Isso corrige o erro Win32 1346 ao usar **Executar como administrador** sem permitir que a extração do produto rode elevada. O teste automatizado reproduz esse cenário em uma pasta temporária protegida.

Desde 10.1.14, a detecção do WinFsp aceita o identificador textual de compilação acrescentado à versão da DLL e usa os campos numéricos assinados do recurso `VERSIONINFO` para a comparação. Registro, diretório aprovado e binário ainda precisam concordar. Assim, uma instalação 2025 legítima é classificada como desatualizada e pode receber a atualização explicitamente selecionada, em vez de bloquear todo o plano como versão desconhecida.

## Verificação e compilação

1. Windows 10/11, Visual Studio 2022 com MSBuild e targeting pack .NET Framework 4.7.2.
2. Colocar os 26 payloads exatos de prerequisites.lock.json em resources/prerequisites e as quatro fontes correspondentes do Java em resources/third-party-sources, conforme third-party-sources.lock.json. Os URLs são fontes, não autorização para aceitar hashes diferentes. URLs de atualização contínua podem mudar: nesse caso é necessária nova auditoria.
3. Executar Test-ConsumerUi.ps1 para testar fontes, fluxo, gráficos e extração sintética, sem instalar componentes.
4. Commitar os fontes numa branch própria e executar Build-Consumer.ps1. O build confere payloads, roda os testes, compila e verifica os recursos incorporados por hash. Seu manifesto liga EXE, catálogo e fontes ao commit.

O modo IncludePrerequisitePayloads=false produz somente DLL de validação, nunca um instalador incompleto.

## Componentes conferidos em 05/09/2026

- Visual C++ v14 x64/x86: 14.51.36247.0.
- .NET Desktop 8 x64/x86: 8.0.30 (versão de arquivo 8.0.30.36323).
- .NET Desktop 10 x64/x86: 10.0.11, preservado.
- WebView2: download Evergreen x64 atualizado, SHA-256 fixado no lock. A versão 1.3.265.7 é do instalador, não do motor WebView2.
- Bibliotecas legadas verificadas preservadas para compatibilidade. Pacotes opcionais não são instalados implicitamente.
- Dokany 2.3.1.1000: driver de sistema de arquivos, opt-in desmarcado.
- WinFsp 2026 Beta4 (2.2.26215): pré-lançamento oficial, opção de teste explicitamente identificada e desmarcada. Não é anunciado como versão estável. A versão estável 2025 não foi usada porque as notas oficiais posteriores publicam correções de segurança. Sua inclusão na entrega está sujeita à escolha do usuário.
- Nenhum driver é instalado só por abrir o assistente. Se um instalador sinaliza 3010/1641, as etapas seguintes param e o assistente mantém a pendência de reinicialização; não solicita reboot automático.

Fontes dos drivers: [Dokany](https://github.com/dokan-dev/dokany/releases/tag/v2.3.1.1000), [WinFsp 2026 Beta4, avisos e hashes](https://github.com/winfsp/winfsp/releases/tag/v2.2B4).

## Identidade visual 10.1

Arte original gerada de F-15 e fliperamas, incorporada ao EXE e verificada por SHA-256. Cabeçalho TURBORAMA em todas as cinco etapas, fundo ilustrado na entrada e brilho neon suave. O efeito para quando a página fica oculta, respeita a configuração de animação do Windows e não roda em alto contraste ou sessão remota. Não há efeitos sobre botões, instruções ou caixas de seleção. Detalhes e prompt em `resources/art/ARTWORK.md`.

Fontes oficiais: [Visual C++](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist), [.NET 8](https://dotnet.microsoft.com/en-us/download/dotnet/8.0), [.NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), [WebView2](https://developer.microsoft.com/en-us/microsoft-edge/webview2).

## Limites da entrega

É um candidato para testes, ainda não aprovado para produção. Testes de bitmap e controles não substituem verificar a janela real em diferentes escalas/DPI, nem instalar numa VM limpa.

O host sozinho não contém o produto TurboRama, jogos, ROMs ou BIOS. A instalação do produto exige as partes com os nomes esperados pelo backend e o sidecar SHA-256 correspondente a este novo EXE; sidecars de versões antigas serão rejeitados. Não reutilize nem renomeie um sidecar antigo.

O host não tem assinatura digital de editor. Pode haver alerta do SmartScreen; nenhuma proteção do Windows é desativada e não há garantia de ausência de alertas. Assinaturas dos instaladores incorporados continuam sendo verificadas antes de executá-los; os editores de Dokany/WinFsp são aceitos apenas para seus arquivos exatos, com hash e certificado fixados.
