# Proveniência do componente de acesso

Fonte: [TRUBORAMA-SUITE](https://github.com/luziellacerda/TRUBORAMA-SUITE/tree/44c936ace6e8645edbfe9b15aeb093da35408504),
branch de origem `codex/v2.0.2-music-cleanup-final`, commit imutável
`44c936ace6e8645edbfe9b15aeb093da35408504`.

Este componente reutiliza código desse projeto do mesmo proprietário.
Não altera os arquivos originais, chaves ou configuração do aplicativo Suite.
Cabeçalhos de proveniência foram acrescentados e quebras de linha normalizadas.

| Arquivo original | Objeto Git original |
| --- | --- |
| `Licensing/SuiteAuthorityConfiguration.cs` | `f1af8ee3ea5be1bcb4262651ac8808c4ab89904c` |
| `Licensing/SuiteProtocol.cs` | `baa24989cf4240bad3f4b1204ed2961f9a06f8d1` |
| `Licensing/SuiteMachineIdentity.cs` | `bbfad2dd0a82c87e4898b4ff886c356bf64ea401` |
| `Licensing/SuiteLicenseClient.cs` | `ef6c447ce19022658106a9ae57ecb072930c8c60` |
| `Licensing/SuiteSession.cs` | `d3e3f513c61af57160cc637369fb0ed76830b4f8` |
| `tests/CatalogVerifier/SuiteProtocolVerifier.cs` | `71922854ab6c628e09b70ea4d4cc08546024b456` |

## Adaptações, arquivo por arquivo

`Upstream/SuiteAuthorityConfiguration.cs`: lógica original integral, incluindo
envelope assinado, hash exato, chave offline, chave online distinta, validade,
SPKI canônico, URL HTTPS, JSON estrito e metadados de assembly.

`Upstream/SuiteProtocol.cs`: na versão 1.1.0, as duas rotas de sessão são
compartilhadas e usam os quatro Kind ES assinados. Os domínios originais, produto,
ações e bytes da prova de máquina permanecem iguais. CONFLICT assinado e sem
janela de autorização é reconhecido apenas após validar assinatura e contexto.
Os DTOs e validadores puros de ativação foram conservados para permitir comparação
com a suíte de testes original. Não há método HTTP de ativação no cliente desta
edição, nem qualquer interface que receba código de ativação.

`Upstream/SuiteMachineIdentity.cs`: `Describe()` passa a
`OpenExistingSelectedKey()`. Removidos `OpenOrCreateSelectedKey`,
`CreateTpmOrSoftwareFallback`, `CreateKey` e assinatura de inventário.
Nome da chave, escopo UserKey, seleção do provedor, validação da não exportação,
SID, fingerprint, SPKI, derivação de DeviceId e assinatura de prova continuam
iguais. Nenhum caminho deste componente chama `CngKey.Create`.

`Upstream/SuiteLicenseClient.cs`: removidos a exceção e o fluxo de ativação,
construtores/dependências de inventário e APIs genéricas de operações usadas
por catálogo/downloads. Mantidos o construtor somente de licença,
`OpenSessionAsync`, tratamento de indisponibilidade da identidade, desafios
anti-replay, HTTP limitado, cancelamento, parser assinado e pinagem TLS.
O seam interno de transporte permanece para testes sintéticos.

`Upstream/NetworkInventoryContract.cs` e `SuiteNetworkInventory.cs`: contrato
complementar com domínios próprios, validação canônica, limite de oito interfaces,
escopo de aplicação e prova pela chave CNG existente. O cabeçalho ES só é enviado
nas duas rotas compartilhadas de sessão. `NetworkInventoryCollector.cs` faz a
coleta com debounce fora do fluxo de autorização e sem registrar endereços crus.

`Upstream/SuiteSession.cs`: mantidos o contexto/capacidade, inscrições atômicas,
serialização de operações, abertura, loop de heartbeat, monitor independente de
expiração, cancelamento, limites monotônicos, janela de validade descontando rede,
renovação e descarte. Removidos dependências e APIs do catálogo, publicação de
inventário, `ActivateAndOpenAsync` e a fábrica que exigia autoridade de conteúdo.
O construtor usa o vencimento da autoridade de licença. `Program.cs` cria
somente esse runtime, após validar a autoridade e a chave já existente.

Na versão 1.1.2, o descarte revoga a capacidade antes de esperar, limita a espera
por tarefas a três segundos e não descarta o semáforo ainda usado por uma
operação atrasada. Cancelamento/fechamento impede publicar resposta de login
tardia. Não há nova ação de protocolo nem fechamento remoto: a reabertura
valida uma sessão nova como na Suite; a política de substituição pertence ao
store ES do servidor e continua isolada do store original da Suite.

`tests/SuiteProtocolVerifier.cs`: reutiliza os testes criptográficos e de sessão
originais. Removido o teste que realizava ativação HTTP simulada; substituída a
inspeção da API de ativação por verificação de sua ausência. O teste de configuração
ausente instancia um runtime indisponível explicitamente, porque a distribuição
real contém a autoridade pública aprovada. Fixtures puramente sintéticas não
pertencem ao executável publicado.

## Arquivos novos

- `Program.cs`: entrada exclusiva `--bridge`, validação inicial e diagnóstico
  `--probe-identity` sem assinatura/rede/criação de chave.
- `BridgeConnection.cs`: pipes privados, tokens fixos, CHECK contínuo e
  cancelamento por EOF/comando malformado. Em 1.1.2, `CANCELLED` distingue a
  desistência do usuário antes de READY; nunca concede acesso ou oculta revogação.
- `LicenseForm.cs`: primeira entrada do identificador já usado na Suite;
  confirmação online, cache após sucesso e encerramento quando a sessão perde validade.
- `LicenseCache.cs`: somente identificador, DPAPI CurrentUser, limites de leitura
  e gravação temporária seguida de substituição atômica.
- `PublicAuthority.g.cs`: metadados públicos exatos da autoridade já aprovada.
- `TurboRama.Suite.Access.csproj`, `global.json`, `app.manifest`, `Build.ps1`:
  build autocontido win-x64, sem elevação, SDK fixado e teste obrigatório antes da publicação.
- `tests/AccessIntegrationVerifier.cs`: fixtures adicionais DPAPI/IPC sem acesso
  a dados reais, rede ou identidade CNG.
- `tests/RuntimeShutdownVerifier.cs`: descarte limitado, resposta tardia, heartbeat
  cancelado e reabertura por nova prova, somente com dados e servidor sintéticos.

## Autoridade pública fixada

Os bytes originais foram codificados em Base64 no assembly, incluindo a quebra de
linha final do envelope JSON. Não há segredo privado nesse arquivo.

| Artefato público original | SHA-256 |
| --- | --- |
| `authority/public/suite-authority-envelope.json` | `20f7f066b654aad700c4733c9b011495a2bb9b52e7a8b3a77e806cdedebfa3e6` |
| `authority/public/suite-authority-issuer.spki.der` | `9ba572cc64ccfd9dcada0699ab5e4f43e4662f84c1a82908a1125aa56c987b3a` |

Não se usa a autoridade de catálogo/downloads. Trocar a autoridade de licença
exige revisar os novos arquivos públicos aprovados e recompilar o helper e o ES.
O cliente não aceita substituí-la por arquivo, argumento ou variável de ambiente.
