# Proteção comercial do TurboRama PIX v25

Este documento descreve o perfil `-ProtecaoComercial` já implementado. Ele torna uma cópia extraída inútil para novas cobranças quando não existe a chave privada autorizada, o TPM original e uma licença válida. Ele **não** promete um executável impossível de copiar ou analisar: esse tipo de binário não existe em um computador controlado pelo cliente.

## Extensão para máquinas sem TPM

O perfil offline descrito neste documento continua exigindo TPM. Para gabinetes sem TPM foi criada
uma extensão on-line dedicada ao licenciamento da máquina e exige prova RSA-PSS, sessão
exclusiva e autorização do servidor em toda nova cobrança. O perfil é explícito e nunca ocorre
rebaixamento automático de `TPM_BOUND` para software.

Leia o fluxo, a instalação e as limitações em
[`ARQUITETURA-LICENCIAMENTO-ONLINE-PIX-v25.md`](ARQUITETURA-LICENCIAMENTO-ONLINE-PIX-v25.md).
O modo `SOFTWARE_BOUND_ONLINE` é propositalmente classificado como mais fraco: seu controle decisivo
é a credencial financeira permanecer fora do quiosque. `USB_TOKEN_BOUND` permanece bloqueado até a
escolha e validação de um token criptográfico real; pendrive comum não é aceito.

## Camadas implementadas

1. **Assinatura Authenticode fixada no programa.** O build incorpora o thumbprint público autorizado. Antes de iniciar componentes PIX, o EmulationStation usa `WinVerifyTrust` e recusa binários sem assinatura válida ou assinados por outro editor. No perfil comercial, a cadeia inteira também passa por consulta de revogação on-line e falha fechada; o runtime privado do .NET precisa ter assinatura válida do Windows/Microsoft.
2. **Manifesto fechado do bundle inteiro.** Depois de assinar o agente e copiar o runtime privado, o compilador calcula uma raiz SHA-256 canônica de todos os arquivos, caminhos e conteúdos. Essa raiz entra nos executáveis nativos assinados. DLL/JSON alterado, arquivo extra, dependência RID trocada ou runtime incompleto faz o lançamento falhar.
3. **Processo .NET sem ambiente herdado perigoso.** O launcher monta uma allowlist de variáveis e desativa diagnóstico/profiling; `DOTNET_STARTUP_HOOKS`, `DOTNET_ADDITIONAL_DEPS`, stores externos e variáveis de profiler não são herdados. O perfil comercial também recusa fallback para apphost/runtime global.
4. **Endurecimento do executável nativo.** O Release comercial ativa otimização/LTCG, Control Flow Guard, `/GS`, ASLR, DEP/NX, High Entropy VA, CET, eliminação de código não usado e build reproduzível (`/Brepro`).
5. **Pacote do cliente reduzido.** O empacotador usa allowlist fechada para a saída do agente e recusa fontes, projetos, scripts, testes, exemplos, objetos, PDBs, emissores e formatos de chave/certificado. O emissor de licenças e sua chave privada não entram no instalador.
6. **Cofre criptograficamente selado ao Windows e ao TPM.** O Access Token passa pelo DPAPI e por AES-256-GCM. A chave AES aleatória é embrulhada com RSA-OAEP-SHA256 pela chave privada persistente, RSA de pelo menos 2048 bits e não exportável criada no Microsoft Platform Crypto Provider. Abrir `secret.dat` exige uma operação privada no TPM original e o mesmo SID do Windows; o fingerprint público sozinho não permite decriptar.
7. **Licença offline por máquina.** Uma compilação comercial contém apenas o certificado público do emissor. Cada licença autoriza `TurboRama-PIX`, versão principal 25 e recurso `pix-production`, é assinada fora do quiosque e vinculada ao fingerprint público do TPM. A licença é revalidada imediatamente antes de criar uma nova cobrança. O emissor mantém ledger durável e recusa o mesmo pedido duas vezes.
8. **Instalação e release protegidas.** O instalador promove o conjunto de arquivos de forma transacional, aplica ACLs aos arquivos PIX e só promove a entrega após o teste isolado. Promoção e recuperação revalidam Authenticode, editor pinado, timestamp, hashes e o 7-Zip aprovado. A árvore Git deve estar limpa e identificada antes de qualquer assinatura oficial.

As chaves de confiança não devem ficar “escondidas no código”. O certificado público pode ser conhecido; a segurança vem da **chave privada não exportável**, preferencialmente guardada em token ou HSM e acessível somente no computador privado de fábrica.

## Pré-requisitos obrigatórios

### Computador privado de compilação/emissão

- Windows x64 e Windows PowerShell 5.1;
- Visual Studio 2022 com **Desenvolvimento para desktop com C++** e Windows SDK/SignTool;
- .NET SDK 8;
- dependências de build já versionadas no repositório;
- repositório comercial **privado**, com autenticação forte e acesso somente à equipe autorizada; nunca publique os fontes PIX, o emissor ou o histórico que já os contenha;
- certificado válido de assinatura de código, com chave privada e EKU de Code Signing, instalado em `CurrentUser\My` ou `LocalMachine\My`;
- certificado separado para emissão de licenças, RSA de pelo menos 2048 bits ou ECDSA P-256/P-384/P-521, com chave CNG não exportável obrigatoriamente em TPM, smart card, token ou HSM; o próprio emissor consulta o `NCRYPT_IMPL_HARDWARE_FLAG` e recusa software, ainda que seja chamado fora do compilador;
- servidor RFC 3161 aprovado pela emissora do certificado;
- para produção, chave privada não exportável em token/HSM e backup operacional conforme a política do fornecedor.

### Quiosque

- Windows 10/11/IoT x64 atualizado, Secure Boot ativo e TPM 2.0 pronto;
- execução dos comandos na **mesma conta Windows do quiosque** configurada no Launcher, sem trocar para Admin;
- modo de manutenção oficial durante instalação/atualização;
- data/hora e cadeia de certificados do Windows corretas.
- acesso HTTPS aos serviços de cadeia/CRL/OCSP dos certificados; se o Windows não conseguir comprovar a revogação no perfil comercial, o componente PIX não inicia.

## Gerar o instalador comercial

Abra o PowerShell na raiz do repositório e execute:

```powershell
.\COMPILAR-PIX-COMERCIAL-v25.ps1 `
  -Limpar `
  -TestarInstalador `
  -SemPausa `
  -ProtecaoComercial `
  -CertificadoThumbprint SEU_THUMBPRINT_SHA1_DE_40_DIGITOS `
  -CertificadoEmissorLicencaThumbprint THUMBPRINT_SEPARADO_DO_EMISSOR `
  -ServidorCarimboDoTempo URL_RFC3161_APROVADA
```

Se o certificado estiver em `LocalMachine\My`, acrescente:

```powershell
-LocalCertificado LocalMachine
```

O perfil comercial já exige assinatura: ausência de certificado utilizável, chave privada, EKU correto, assinatura válida, licença pública incorporada ou teste do instalador deve encerrar o build sem promover uma entrega final. Antes de distribuir, confira `ASSINATURA-AUTHENTICODE.txt`, `CHECKSUMS-SHA256.txt`, o relatório de compilação e o log do teste.

O build também encerra se o Git estiver indisponível ou se houver arquivo modificado/não rastreado. Primeiro revise e faça o commit da versão aprovada; somente o commit limpo pode receber a assinatura comercial.

## Ativar um quiosque

Use um dos layouts instalados:

```powershell
$InstallRoot = 'D:\emulationstation'
# Layout clássico, quando aplicável:
# $InstallRoot = 'D:\Turborama\emulationstation'

$Dotnet = Join-Path $InstallRoot 'pix-agent\runtime\dotnet.exe'
$Agent  = Join-Path $InstallRoot 'pix-agent\TurboRamaPixAgent.dll'
```

### 1. Criar o pedido no próprio quiosque

Feche o EmulationStation e, na conta Windows automática do quiosque, execute:

```powershell
& $Dotnet $Agent --license-request 'D:\pedido-turborama-pix.json'
```

O pedido contém somente identidade pública/fingerprint do TPM e dados do produto; não contém Access Token, Client Secret ou senha. Nunca aceite pedido enviado sem associá-lo ao número de série/cliente esperado, e registre cada ID de pedido emitido para impedir emissão duplicada acidental.

### 2. Emitir na fábrica

Transfira apenas o pedido para o computador privado. Na raiz do repositório:

```powershell
.\GERAR-LICENCA-PIX-COMERCIAL.ps1 `
  -Pedido 'C:\Pedidos\pedido-turborama-pix.json' `
  -Saida 'C:\Licencas\quiosque-turborama-pix.license' `
  -CertificadoThumbprint SEU_THUMBPRINT_SHA1_DE_40_DIGITOS
```

Por padrão, o wrapper mantém o ledger antirrepetição em `%LOCALAPPDATA%\TurboRama\license-issuer\issued-requests.log`. Guarde esse arquivo em volume criptografado e backup controlado; nunca o apague ou restaure para uma versão antiga. É possível indicar outro caminho seguro com `-RegistroEmissoes`.

Para certificado em `LocalMachine\My`, acrescente `-LocalCertificado LocalMachine`. O emissor não sobrescreve um arquivo de saída existente. Seu autoteste isolado é:

```powershell
.\GERAR-LICENCA-PIX-COMERCIAL.ps1 -AutoTeste
```

Não copie para o cliente o repositório, o projeto `tools\TurboRamaPixLicenseIssuer`, o wrapper acima, certificados com chave privada, arquivos PFX, PINs ou segredos do HSM.

### 3. Instalar e confirmar no mesmo quiosque

Leve somente a licença assinada de volta e execute, na mesma conta Windows do quiosque:

```powershell
& $Dotnet $Agent --install-license 'D:\quiosque-turborama-pix.license'
& $Dotnet $Agent --license-status
```

Depois, remova dos meios de transporte as cópias temporárias segundo a política da empresa. A licença instalada permanece protegida pelas ACLs do sistema. Formatar o Windows, trocar o usuário/SID, substituir a placa/TPM ou limpar o TPM exige novo cadastro seguro e nova licença.

## Endurecimento do Windows recomendado

Para chegar ao nível comercial mais alto, as camadas do aplicativo devem ser acompanhadas por controles do sistema:

- **BitLocker:** criptografar os volumes do Windows e do TurboRama, manter a chave de recuperação fora do quiosque e validar recuperação antes da venda. Isso reduz cópia offline do disco; não substitui a licença TPM.
- **App Control for Business/WDAC:** criar uma política que permita Windows/Microsoft e o editor Authenticode do TurboRama, começar em modo de auditoria, revisar todos os executáveis necessários e somente depois aplicar modo imposto. Assine a política para dificultar adulteração. Uma regra incompleta pode impedir o quiosque de iniciar; teste em gabinete reserva e mantenha mídia de recuperação.
- manter Secure Boot, TPM, Defender e atualizações do Windows ativos; não criar exclusões amplas para a pasta TurboRama;
- usar conta de quiosque sem privilégios administrativos e restringir fisicamente USB, boot externo, firmware e acesso ao gabinete;
- manter certificado, token/HSM, PIN, lista de licenças e chaves de recuperação sob controle de duas pessoas e com auditoria.

Referências oficiais:

- WinVerifyTrust: <https://learn.microsoft.com/windows/win32/api/wintrust/nf-wintrust-winverifytrust>
- SignTool: <https://learn.microsoft.com/windows/win32/seccrypto/signtool>
- Control Flow Guard: <https://learn.microsoft.com/cpp/build/reference/guard-enable-control-flow-guard>
- TPM no Windows: <https://learn.microsoft.com/windows/security/hardware-security/tpm/how-windows-uses-the-tpm>
- DPAPI/CryptProtectData: <https://learn.microsoft.com/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata>
- BitLocker: <https://learn.microsoft.com/windows/security/operating-system-security/data-protection/bitlocker/>
- App Control for Business: <https://learn.microsoft.com/windows/security/application-security/application-control/app-control-for-business/appcontrol>
- Políticas WDAC assinadas: <https://learn.microsoft.com/windows/security/application-security/application-control/app-control-for-business/deployment/use-signed-policies-to-protect-appcontrol-against-tampering>

## Limitações e bloqueios atuais

- Não existe proteção local que impeça completamente leitura, cópia, engenharia reversa, depuração ou captura de memória por um administrador/atacante com controle total do computador. As camadas acima fazem o material copiado falhar fechado para uso comercial; não tornam os bytes invisíveis.
- A consulta de revogação Authenticode do perfil comercial privilegia segurança: indisponibilidade dos serviços de cadeia/CRL/OCSP pode bloquear a inicialização do componente PIX até a conectividade voltar. Valide os domínios exigidos pelo certificado real na rede do quiosque, sem desativar a verificação.
- A chave TPM não é exportável, mas o TPM pode atuar como oráculo para outro código autorizado executando no mesmo SID do quiosque. Por isso ACL, conta sem administrador e WDAC assinado não são opcionais contra um cliente hostil; a evolução de isolamento mais forte é executar o agente em serviço com SID exclusivo e IPC mínimo.
- A verificação do manifesto termina antes de o Windows reabrir o executável pelo caminho. ACL e WDAC precisam impedir troca de arquivo nessa pequena janela; sem controle do Windows, nenhuma conferência feita apenas pelo processo elimina completamente esse risco de tempo de verificação/tempo de uso.
- O código do compilador não é a autoridade de emissão. Mesmo com o repositório, uma pessoa não deve conseguir emitir licença válida sem a chave privada autorizada. Se essa chave vazar, revogue o certificado, publique um novo build com outra raiz de confiança e reemita licenças.
- Licença e TPM protegem a autenticidade e o uso do produto oficial, mas não impedem alguém de estudar fontes que tenham sido publicados e criar um clone independente. Confirme que a branch comercial e todo o histórico com os componentes PIX estão em repositório privado; apenas apagar uma branch pública não elimina cópias ou forks já feitos.
- O ledger local impede repetição e concorrência normais, mas um administrador da máquina emissora ainda pode restaurar um backup antigo. Para auditoria forte, replique cada emissão para um registro remoto transacional ou log autenticado/encadeado protegido pela fábrica; não trate o arquivo local como contador monotônico infalível.
- Neste gabinete de laboratório, o Microsoft Platform Crypto Provider/TPM ainda não respondeu como pronto. A prova real TPM+DPAPI e a ativação no usuário automático do quiosque permanecem obrigatórias antes da venda.
- Não há atualmente certificado real de assinatura de código com chave privada disponível neste computador. Portanto, ainda não é possível declarar ou gerar aqui uma entrega `-ProtecaoComercial` liberada para consumidor.
- Access Token e Client Secret já publicados em conversa, captura de tela, log ou repositório devem ser **revogados e substituídos** antes de qualquer teste real ou venda. Não reutilize credenciais expostas e nunca as inclua em comando, código, documentação ou instalador.
- A liberação comercial exige teste real no hardware Windows IoT: instalação elevada no modo manutenção, reinicialização, ativação TPM, configuração pelo usuário do quiosque, cobrança PIX de valor controlado, confirmação do crédito, reconciliação após reinício e verificação de rollback/recuperação.

## Critério mínimo de liberação

Só marque a versão como final para venda quando todos os itens abaixo estiverem comprovados e registrados:

- build `-ProtecaoComercial` limpo, assinado, carimbado e com checksums;
- certificado privado protegido e emissor de licença ausente do pacote do cliente;
- TPM 2.0 pronto e pedido/licença válidos na conta automática real;
- credenciais novas e não expostas;
- cobrança e crédito reais concluídos, inclusive após reinício e falha de rede simulada;
- BitLocker ativo com recuperação testada;
- WDAC validado primeiro em auditoria e depois imposto sem bloquear Launcher, EmulationStation, agente ou manutenção;
- cópia do pacote para outro PC comprovadamente recusada para novas cobranças.
