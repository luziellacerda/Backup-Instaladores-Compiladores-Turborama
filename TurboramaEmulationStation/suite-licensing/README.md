# Acesso do EmulationStation pela ativação do TurboRama Suite

Desde a edição **1.0.1**, o módulo é embutido no `emulationstation.exe`.
Leia também `../docs/SUITE-EDITION-v1.0.1.md`. A lógica gerenciada de licença,
identidade, cache e servidor permanece igual à edição 1.0.0.

Este componente pertence à nova edição do EmulationStation baseada no cliente sem
serviços. Reutiliza a licença e a identidade CNG que o TurboRama Suite já ativou
no computador. O servidor confirma a autorização a cada abertura e a renova
durante o uso. Não existe licença offline neste componente.

## Primeiro acesso do cliente

1. Ative o TurboRama Suite normalmente.
2. Entre no Windows com a mesma conta usada nessa ativação.
3. Abra o novo EmulationStation. Na primeira vez, informe o mesmo **identificador
   da licença** usado na Suite. O código descartável de ativação não é solicitado.
4. A confirmação online libera a interface do EmulationStation. O identificador
   fica salvo protegido com DPAPI desta conta, permitindo a próxima abertura
   automática enquanto o servidor confirmar a licença.

A Suite atualmente não grava um recibo local com o identificador da licença para
outros aplicativos consultarem. Por isso ele é informado uma vez nesta edição.
Copiar esse identificador ou seu cache para outro computador não copia a identidade
criptográfica necessária à autorização.

Se a chave da Suite estiver ausente, o componente orienta a ativar a Suite na
mesma conta. Ele nunca cria, substitui, exporta ou reativa uma chave.

## Integração com o servidor

As chamadas são exclusivamente:

- `POST /v1/suite/emulationstation/challenges`
- `POST /v1/suite/emulationstation/sessions`

O produto continua `TURBORAMA_SUITE`, com `session.open` e
`session.heartbeat`, a mesma licença, chave CNG e autoridades públicas assinadas.
As sessões e desafios ficam separados no servidor para a Suite e o EmulationStation
funcionarem simultaneamente.

A extensão correspondente de `luziellacerda/Servidor-pix` precisa estar implantada.
A configuração `Suite:EmulationStation:Enabled`, representada por
`Suite__EmulationStation__Enabled` no ambiente, começa desabilitada e depende
também de `Suite:Enabled`. Desabilitada, responde HTTP 503 com
`EMULATIONSTATION_DISABLED`. Um servidor ainda sem as rotas retorna 404.
Esta edição falha fechada nos dois casos. Compilar o EXE não implanta o servidor.

## Como a autorização é verificada

`Program.cs` carrega o envelope público assinado e seu hash exato, confere a
política de identidade e abre somente a chave CNG já existente. A autoridade
embutida é a publicada pela Suite; não é uma URL configurável pelo usuário.

`SuiteMachineIdentity.cs` mantém o nome da chave, o sufixo derivado do SID,
os provedores TPM/software, o hardware fingerprint e a assinatura RSA-PSS
originais. A chave pertence à conta do Windows e não é exportada.

`SuiteLicenseClient.cs` valida a cadeia TLS, hostname, revogação e pin SPKI.
Recusa redirecionamentos, proxy, compressão inesperada e corpos ilimitados.
Verifica a assinatura da resposta, produto, licença, dispositivo, sessão,
ação, desafio, contexto e prazo antes de criar qualquer autorização.

`SuiteSession.cs` preserva a contagem monotônica de validade, desconta o tempo
de rede, renova por heartbeat e rejeita repetição de respostas. Falhas transitórias
podem ser repetidas apenas dentro da validade já concedida. Revogação ou expiração
encerra a autorização; o relógio local não prolonga a sessão.

`LicenseCache.cs` salva somente o identificador em
`%LOCALAPPDATA%\TurboRama\EmulationStation\Suite\license-id.dpapi`,
usando DPAPI CurrentUser. O cache nunca contém autorização, sessão, código de
ativação ou chave privada. Todo conteúdo lido continua sujeito ao servidor.

## Ponte com o executável nativo

O executável nativo verifica o SHA-256 aprovado do módulo embutido em RCDATA 31001,
extrai-o com CREATE_NEW em um diretório aleatório privado e mantém o arquivo
bloqueado contra escrita/exclusão depois de conferir hash e identidade.
Inicia esse arquivo pelo caminho absoluto, com ambiente controlado, diretório
privado de extração do runtime e pipes anônimos herdados. O helper permanece
ativo enquanto o EmulationStation precisa da sessão. Um helper adjacente antigo
é ignorado, não executado e não removido. A chave CNG não está embutida no EXE.

O protocolo usa ASCII/UTF-8 sem BOM e LF literal:

| Sentido | Mensagem | Condição |
| --- | --- | --- |
| Helper → ES | `READY\n` | Somente após confirmação online |
| ES → helper | `CHECK\n` | Consulta de validade atual |
| Helper → ES | `OK\n` | Contexto ainda autorizado |
| Helper → ES | `DENIED\n` | Acesso inválido ou comando incorreto |

Nenhuma licença ou chave trafega nesses pipes. O ES trata EOF, encerramento,
ausência de resposta e resposta inválida como perda de autorização. O helper
detecta EOF mesmo durante o primeiro login. Um job do Windows encerra somente
o helper quando o processo principal termina.

## Compilação no GitHub

O workflow da nova edição deve instalar o SDK **10.0.400** e executar, a partir de
`TurboramaEmulationStation`:

```powershell
pwsh -File suite-licensing/Build.ps1
```

O script executa primeiro o projeto `tests/Verifier.csproj`. Depois publica um
helper win-x64 autocontido em `suite-licensing/publish/TurboRama.Suite.Access.exe`
e o hash em `TurboRama.Suite.Access.exe.sha256`. O binário e o hash devem ser
passados à configuração CMake da edição Suite, usando os parâmetros
`TURBORAMA_SUITE_HELPER_PATH` (caminho absoluto) e `TURBORAMA_SUITE_HELPER_SHA256`.
O módulo é incorporado ao EXE, não copiado para o pacote final. O cliente não instala .NET.
Uma alteração no helper exige recompilar o ES para atualizar o hash embutido.

No frontend, `--suite-access-probe-identity` executa o diagnóstico embutido e
retorna 0/21, ou 44 se houver falha de integridade, extração ou processo.
No módulo de compilação, `--probe-identity` é um diagnóstico somente de leitura: retorna
`EXISTING_IDENTITY_AVAILABLE` e código 0 quando a chave existente atende à
política, ou `EXISTING_IDENTITY_UNAVAILABLE` e código 21. Não revela
identificadores, não assina, não usa a rede e não modifica a identidade.

## Validação realizada

O SDK local disponível era 9.0.317. Os mesmos fontes foram compilados com o alvo
de compatibilidade `net9.0-windows`, sem instalar ferramentas nem baixar assets:

```powershell
dotnet build suite-licensing/tests/Verifier.csproj -c Release -p:SuiteCompatibilityTargetFramework=net9.0-windows
dotnet build suite-licensing/TurboRama.Suite.Access.csproj -c Release -p:SuiteCompatibilityTargetFramework=net9.0-windows -p:SelfContained=false -p:PublishSingleFile=false
```

Esses comandos devem ser executados de um diretório fora do alcance de
`suite-licensing/global.json`, usando o caminho completo do projeto, pois
esse arquivo exige o SDK de produção. O alvo de compatibilidade é apenas para
análise local; `Build.ps1` força SDK 10.0.400 e net10.0-windows na publicação.

Passaram os testes de assinatura adulterada, autoridades/pins incorretos,
replay, produto/contexto incorreto, capacidade não forjável, expiração monotônica,
renovação, perda de identidade, cache DPAPI adulterado, CHECK autorizado/revogado,
comando malformado e EOF do pai antes/depois do login. As fixtures são sintéticas
e não são incluídas no helper publicado.

O diagnóstico local reconheceu a identidade existente sem acessar o servidor.
Login real nas novas rotas depende da implantação e habilitação da extensão;
esse resultado não representa uma homologação online em produção.

Veja `UPSTREAM.md` para proveniência e adaptações exatas.
