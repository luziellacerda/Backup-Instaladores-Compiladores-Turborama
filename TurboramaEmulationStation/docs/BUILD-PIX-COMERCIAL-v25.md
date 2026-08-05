# Build do TurboRama PIX Comercial v25

## Build limpo e tema embutido

O build oficial é iniciado por `COMPILAR-PIX-COMERCIAL-v25.cmd` na raiz do repositório. Para apagar somente saídas geradas e reconstruir tudo:

```powershell
.\COMPILAR-PIX-COMERCIAL-v25.ps1 -Limpar -TestarInstalador
```

Python não é necessário. O CMake exige Windows PowerShell 5.1 e executa `tools\Pack-EmbeddedTheme.ps1`, que usa apenas o .NET do Windows. Se o empacotador estiver ausente, a configuração falha claramente; o build nunca reutiliza silenciosamente um recurso antigo.

O `embedded_theme.bin` é criado dentro da pasta de build. Ele contém:

- ZIP com arquivos em ordem estável, datas normalizadas e gravação atômica;
- cabeçalho `TRTHEME1:<hash>` com a identidade do conteúdo;
- payload ofuscado esperado pelo frontend.

Na execução, o frontend só aceita uma pasta de cache cuja `.payload` corresponda exatamente à identidade embutida no executável. Um cache de versão anterior é ignorado e o tema correto é extraído em uma nova pasta.

Teste focado do empacotador:

```powershell
.\tools\tests\Test-EmbeddedThemeBuild.ps1
```

## Assinatura Authenticode opcional

O repositório não contém certificado nem senha. Para assinar um build oficial, instale um certificado de assinatura de código com chave privada no repositório `My` do Windows e informe seu thumbprint:

```powershell
.\COMPILAR-PIX-COMERCIAL-v25.ps1 `
  -Limpar -TestarInstalador -ExigirAssinatura `
  -CertificadoThumbprint SEU_THUMBPRINT_DE_40_DIGITOS `
  -ServidorCarimboDoTempo URL_RFC3161_APROVADA_PELA_EMISSORA
```

Também podem ser usadas as variáveis `TURBORAMA_SIGN_CERT_THUMBPRINT` e `TURBORAMA_SIGN_TIMESTAMP_URL`. Use `-LocalCertificado LocalMachine` somente quando o certificado tiver sido instalado nesse armazenamento.

Quando a assinatura é habilitada, o build assina e verifica os executáveis antes de empacotá-los, assina o instalador final e assina a cópia distribuída do reparador PowerShell. `ASSINATURA-AUTHENTICODE.txt` registra o resultado. `-ExigirAssinatura` impede que um pacote oficial seja produzido sem certificado.

## Supervisão do agente PIX

O frontend verifica a cada 15 segundos o avanço de `updatedAtUnixSeconds` e a identidade completa publicada no contrato schema 2 de `agent-status.json`: modo `daemon`, PID, instante de criação do processo (`FILETIME`) e hash do token efêmero entregue pelo manager. O agente renova o heartbeat em timer independente, inclusive durante chamadas bancárias e entre itens de um lote. O processo recebe 90 segundos de tolerância para inicialização; heartbeat estagnado provoca reinício somente depois de a mesma instância ser revalidada.

O daemon mantém um mutex singleton e outro mutex ligado ao PID. O supervisor só reconhece a combinação exata de caminho, PID, `FILETIME`, token e mutexes; ele não enumera nem adota processos apenas porque usam o mesmo `dotnet.exe`. Estado ausente ou inválido só autoriza uma nova partida quando os mutexes e a instância anteriormente esperada provam que não há daemon. Falha de acesso ou qualquer divergência produz estado `Unknown` e bloqueia encerramento/reinício.

Antes de forçar o encerramento, o frontend grava `agent-stop.request` de forma atômica e dirigido ao PID, `FILETIME` e token da instância validada. O agente publica `stopping`, remove o sentinel e encerra após concluir suas gravações. `TerminateProcess` fica restrito ao mesmo handle já validado e o encerramento só é aceito depois de o Windows confirmar a saída do processo. O sentinel histórico `installer-update` permanece aceito exclusivamente para a atualização comercial.

## Reparação de instalações antigas

O reparador tools\TurboRamaRuntime\REPARAR-INSTALACAO-TURBORAMA.ps1 e incluido
no instalador e na pasta de saida. Ele corrige a configuracao antiga do Launcher
e remove somente caches obsoletos do tema, criando backup antes das alteracoes.

## Promocao transacional da release

GERADO-v25 nunca e usado como area de montagem. O build cria todo o candidato em
%LOCALAPPDATA%\Temp\TurboRama-v25-build\pack\PIX-COMERCIAL\GERADO-v25, assina e
verifica os binarios quando um certificado foi informado, valida o payload e
executa o smoke test isolado.

-TestarInstalador e obrigatorio para promover a entrega. Sem essa opcao, o
comando encerra sem tocar em PIX-COMERCIAL\GERADO-v25; o candidato permanece
somente na area temporaria local para investigacao.

Depois de o candidato passar, a promocao normalmente troca diretorios completos
no mesmo volume. A saida anterior e preservada em release-backups\p-<data>-<id>,
fora da area que -Limpar pode remover. Se o Windows mantiver bloqueada apenas uma
pasta canonica historica e incompleta (vazia ou contendo exclusivamente o log de
compilacao), ha um fallback de bootstrap estritamente limitado: ela e preservada
em release-backups\bootstrap-<data>-<id> e os artefatos validados sao movidos,
com CHECKSUMS-SHA256.txt por ultimo. Esse fallback nunca e aceito para uma
release completa. Portanto uma release valida nao mistura instalador novo com
checksums, relatorio ou instrucoes antigos. O manifesto CHECKSUMS-SHA256.txt
cobre todo artefato distribuido, exceto o proprio manifesto.

O teste isolado inclui um kioskUser Windows resolvivel e primeiro confirma que
um usuario inexistente e recusado sem alterar o destino. O modo de smoke so e
aceito pelo pacote candidato em
%LOCALAPPDATA%\Temp\TurboRama-v25-smoke\install, resolvido pelo local conhecido
do Windows; a instalacao de producao continua exigindo elevacao e a verificacao
de identidade/autologon.

## 7-Zip redistribuido

O unico descompactador aceito e tools\TurboRamaCommercialInstaller\vendor\7za.exe
versao 24.09, com SHA-256
223B873C50380FE9A39F1A22B6ABF8D46DB506E1C08D08312902F6F3CD1F7AC3.
O binario, LICENSE-7ZIP-24.09.txt, COPYING-LGPL-2.1.txt e
NOTICE-7ZIP-24.09.txt seguem no payload e na entrega final e sao verificados no
manifesto.
