# TurboRama EmulationStation — Suite Edition 1.0.1

Atualização da edição cliente sem serviços comerciais. Agora o módulo de acesso
Suite vem dentro de `emulationstation.exe`: não é necessário entregar nem
atualizar um segundo executável ao lado dele. A lógica de autorização permanece
a da edição 1.0.0, derivada do TurboRama Suite.

## Instalação para teste

1. Use o mesmo computador e a mesma conta do Windows em que o TurboRama Suite já
   foi ativado. Não apague a chave do Suite nem refaça a ativação para atualizar.
2. O administrador precisa implantar e habilitar a extensão EmulationStation no
   servidor. A aprovação da compilação não significa que essa implantação ocorreu.
3. Feche o EmulationStation. Guarde uma cópia do EXE anterior e substitua somente
   `emulationstation.exe` na instalação existente, mantendo DLLs, recursos e perfil.
4. Um `TurboRama.Suite.Access.exe` antigo ao lado do frontend é ignorado. O novo
   frontend não o executa nem o apaga. Guarde o par antigo se precisar voltar à 1.0.0.
5. Abra o frontend. No primeiro acesso, informe o mesmo identificador da licença
   Suite. Não é solicitado um novo código de ativação. Depois da confirmação online,
   o identificador é guardado com DPAPI para esta conta; a autorização não é guardada.

O EXE avulso e o ZIP de atualização reaproveitam as dependências da instalação.
Para uma pasta nova, use o ZIP completo. Nenhum pacote exige instalar compilador,
Visual Studio ou .NET no computador do cliente. O pacote completo traz
`TESTAR-ISOLADO.cmd`, que usa um perfil de teste separado.

## Mesma segurança do Suite, mesma autorização do servidor

O frontend não aceita o identificador da licença como autorização suficiente.
Abre somente a chave CNG existente, criada na ativação Suite, e usa essa identidade
para responder a um desafio do servidor. O servidor precisa confirmar a licença,
o dispositivo e a elegibilidade. As respostas assinadas, o contexto e a validade
são verificados antes de abrir a interface e durante a execução.

Continuam iguais à edição 1.0.0: produto `TURBORAMA_SUITE`, seleção da identidade
CNG/TPM conforme a autoridade, nome da chave por SID, assinatura RSA-PSS,
HTTPS com cadeia/hostname/revogação e pin SPKI, rejeição de replay, prazo monotônico,
heartbeat e cache DPAPI CurrentUser. Nenhuma função de criar, substituir, exportar
ou ativar uma chave foi acrescentada. O código de licenciamento em `Upstream/`,
o programa gerenciado, a tela de login, o cache e a ponte não foram alterados
nesta atualização; o projeto gerenciado recebeu somente a versão 1.0.1.

O cache em `%LOCALAPPDATA%\TurboRama\EmulationStation\Suite\license-id.dpapi`
contém somente o identificador. Copiar esse cache e o EXE para uma instalação
normal de outro PC não transfere a chave privada CNG nem a autorização online.
O Suite original não persiste um recibo compartilhável com esse identificador;
por isso ele é informado uma vez, sem consumir outra ativação.

Isso protege a abertura desta edição no fluxo normal. Não criptografa ROMs,
temas ou outros arquivos já existentes, não protege executáveis antigos sem
licenciamento e não promete impedir engenharia reversa ou um administrador local
de modificar o programa. A política atual do Suite é software; esta atualização
não a troca silenciosamente por TPM obrigatório nem muda seu vínculo de hardware.

## Por que a sessão do EmulationStation é separada

As rotas continuam sendo `POST /v1/suite/emulationstation/challenges` e
`POST /v1/suite/emulationstation/sessions`, consultando a mesma licença e identidade
ativadas. Desafios e sessões próprios evitam que abrir o frontend encerre a sessão
da loja Suite. Não foram adicionados PIX, pagamento, contabilidade ou tempo de locadora.

A extensão está na branch `codex/emulationstation-suite-v1-20260905` do repositório
`luziellacerda/Servidor-pix`, commit `769f8b44c87b53ec6393276548a61da79b43aa22`.
Seu roteiro de implantação e rollback está em `docs/suite/EMULATIONSTATION-INTEGRATION.md`.
Ela começa com `Suite__EmulationStation__Enabled=false` e também depende de
`Suite__Enabled`. Sem implantação ou habilitação, o acesso é recusado; não há
fallback offline. Esta atualização do EXE não altera nem implanta o servidor.

## O que significa EXE único

O CMake recebe o arquivo de acesso e seu SHA-256 exato, valida os dois e incorpora
o binário no recurso Windows RCDATA 31001 do frontend. O hash esperado também fica
embutido. Um arquivo externo não serve como alternativa se o recurso faltar ou
estiver adulterado.

Na abertura, `SuiteAccessGate.cpp` verifica o recurso diretamente na memória mapeada,
sem criar uma segunda cópia grande em heap. Cria um diretório aleatório por execução
em `%LOCALAPPDATA%\TurboRama.Suite.Access.<32 caracteres hexadecimais>`, com ACL para
o usuário atual e SYSTEM. Extrai usando CREATE_NEW, verifica novamente hash e
identidade do arquivo e mantém um handle que impede escrita/exclusão durante o uso.

O módulo continua sendo um processo filho isolado, embora o cliente receba somente
um EXE. Usa ambiente controlado, caminhos absolutos, pipes anônimos herdados e um
Job Object para encerramento. As mensagens continuam READY/CHECK/OK/DENIED;
licença e chaves não trafegam nesses pipes. A extração do runtime .NET também
fica no diretório privado. Não há chave privada de autoridade no recurso embutido.

Na saída normal ou falha tratada, o processo filho é encerrado e o diretório é
limpo sem seguir pontos de redirecionamento. Queda de energia ou encerramento
abrupto pode deixar arquivos temporários de código/runtime, mas não acrescenta
credenciais de ativação a essa pasta. Não se faz uma limpeza indiscriminada do perfil.

Se a autorização expirar ou for revogada, novos jogos são bloqueados. Um emulador
já em execução segue seu fluxo normal até retornar, quando o frontend encerra
pelo caminho de salvamento existente. O job não contém nem encerra os emuladores.

## Preservação e rastreabilidade

- Base cliente: `5a356172013a620a1a0ecf151c00c9238ea21a24`.
- Origem do Suite: `luziellacerda/TRUBORAMA-SUITE`, branch
  `codex/v2.0.2-music-cleanup-final`, commit `44c936ace6e8645edbfe9b15aeb093da35408504`.
- Versão anterior desta integração: fontes `4194c99c3515217b6a00e330f067ae7e7a10a128`,
  documentação consolidada `4164802708f0ee48323ffa766cf92803e56f7ac5`.
- Branch de desenvolvimento mantida: `CLIENTE-SUITE-ATIVADO-v1.0.0-20260905`.
  O nome histórico da branch não é a versão do executável atual.
- Workflow exclusivo: `.github/workflows/compilar-cliente-suite-windows.yml`.
  Tags novas `es-suite-v1.0.1-<12 caracteres do commit>`; releases PIX e cliente
  sem serviços não são substituídos.

As mudanças 1.0.1 são embalagem, extração/diagnóstico nativo, versões, testes,
workflow e documentação. Algoritmos de áudio, memória, cache, jogos, menus, temas
e vídeo continuam sem alterações nesta atualização. O módulo de acesso tem seu
próprio consumo de memória; não se promete consumo total idêntico ao cliente sem
licenciamento. A base cliente não continha o `waitForAudioRelease(3000)` acrescentado
posteriormente no PIX, e esta atualização não declara essa paridade inexistente.

## Compilação e testes

O workflow usa o runner padrão `windows-2022`, Visual Studio 2022, .NET 10.0.400
e dependências nativas fixadas em `468eaba48c028921a4bf2abdfa3f3a00ce8d4c0d`.
O repositório frontend é público; não são usados runners maiores, serviço externo
de assinatura, certificado pago ou novas ferramentas locais para esta entrega.

A ordem é: preservação da base → testes/compilação do módulo Suite → testes da ponte
nativa com fixtures sintéticas → CMake com caminho/hash do módulo → compilação
frontend → testes de serviços removidos/otimizações → testes do pacote real → release.
O build recusa combinar Suite com serviços comerciais ou usar módulo/hash divergentes.

`Test-SuiteNative.ps1` testa recurso ausente/adulterado, módulo externo ignorado,
ACL, bloqueios, ambiente, pipes, revogação e limpeza. `Test-SuitePackage.ps1` verifica
o binário real sem helper adjacente, cria um marcador falso adjacente que deve ser
ignorado, executa o diagnóstico de identidade e altera um byte do recurso em uma
cópia de teste: o acesso precisa falhar. O EXE publicado não é adulterado e seu
SHA-256 é conferido novamente. Nenhuma licença de cliente é usada nos testes.

Diagnósticos que terminam sem abrir interface/jogos nem acessar o servidor:

```text
emulationstation.exe --suite-access-self-test
emulationstation.exe --suite-access-integrity-self-test
emulationstation.exe --suite-access-probe-identity
```

Os dois primeiros retornam 0 quando aprovados e 44 quando falham. O último retorna
0 se a chave existente estiver disponível, 21 se estiver ausente e 44 em falha
de integridade/extração/processo. Não ativa, não exporta e não confirma licença
ativa no servidor. No runner limpo, 21 é o resultado esperado.

Antes de homologar, testar login real com a extensão instalada: Suite e frontend
simultâneos, entrada subsequente, bloqueio em outro PC/usuário, revogação, rede
indisponível e retorno de emulador. Testes sintéticos não substituem essa etapa.

## SmartScreen e custo

O EXE de teste não tem assinatura Authenticode. O aviso do SmartScreen pode
continuar: ele é independente da autorização de dispositivo pelo servidor.
Embutir o módulo e verificar hashes não cria uma assinatura pública da distribuição.
Nenhum certificado foi comprado e nenhuma assinatura autoassinada foi instalada
como solução de confiança. Não é necessário desativar as proteções do Windows
para manter este modelo de licenciamento.

Para voltar à 1.0.0, restaure os dois EXEs correspondentes da compilação anterior,
preservando perfil e chave Suite. Os documentos da 1.0.0 continuam no Git como
histórico; este manual substitui as instruções de entrega em dois arquivos.
