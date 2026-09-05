# TurboRama EmulationStation — Suite Edition 1.0.0

Nova edição Windows x64 que abre usando uma licença já ativada pelo TurboRama Suite no mesmo usuário do Windows. A identidade criptográfica é a mesma; a sessão de execução do EmulationStation é separada para permitir usar os dois programas juntos.

## Origem e escopo

| Componente | Base imutável |
| --- | --- |
| EmulationStation cliente sem serviços | `CLIENTE-SEM-SERVICOS-20260904-1818`, commit `5a356172013a620a1a0ecf151c00c9238ea21a24` |
| Código original de licenciamento Suite | `luziellacerda/TRUBORAMA-SUITE`, branch `codex/v2.0.2-music-cleanup-final`, commit `44c936ace6e8645edbfe9b15aeb093da35408504` |
| Nova branch do frontend | `CLIENTE-SUITE-ATIVADO-v1.0.0-20260905` |
| Extensão do servidor | `luziellacerda/Servidor-pix`, branch `codex/emulationstation-suite-v1-20260905` |

A numeração 1.0.0 identifica esta edição; a origem continua sendo EmulationStation 42. As branches anteriores e seus workflows/releases continuam independentes.

As alterações em arquivos preexistentes do frontend se limitam à configuração de compilação, identificação da edição, inicialização, observação da autorização no loop principal e uma verificação antes do lançamento dos jogos. Os arquivos originais de menus, tema, recursos, áudio, cache de texturas, carrossel, VLC e gerenciamento de memória são preservados. `Test-SuiteClientPreservation.ps1` bloqueia alterações fora dessa lista e exclusões de arquivos existentes. O teste anterior `Test-NoCommercialServicesBuild.ps1` continua validando as otimizações e a ausência dos serviços comerciais.

Esta base cliente não continha o `waitForAudioRelease(3000)` acrescentado posteriormente na branch PIX. Esta edição preserva a base cliente escolhida; não declara uma paridade de áudio que essa base não tinha. As alterações de licenciamento não substituem nem removem sua lógica de áudio.

## Como usar

1. O TurboRama Suite precisa ter sido ativado neste computador, usando a mesma conta do Windows que abrirá o EmulationStation.
2. O administrador deve instalar e habilitar a extensão EmulationStation no servidor. O roteiro está na branch complementar do servidor. Compilar ou baixar este frontend não instala essa extensão.
3. No primeiro acesso, informe o mesmo **identificador da licença** usado no login do Suite. Não é solicitado outro código de ativação. O Suite original não persiste esse identificador para leitura por outros programas.
4. Após o primeiro acesso confirmado, o identificador fica protegido por DPAPI no perfil do usuário. Nas próximas aberturas a consulta ao servidor acontece automaticamente.
5. Extraia o pacote completo ou atualize juntos `emulationstation.exe` e `TurboRama.Suite.Access.exe` em uma instalação cliente com seus runtimes. Não misture executáveis de compilações diferentes: o frontend confere o SHA-256 exato do módulo de acesso.

O pacote completo inclui as dependências Windows; o computador do cliente não precisa de Visual Studio, SDK .NET nem compilador. O pacote de atualização contém os dois executáveis e este manual e reaproveita os runtimes já instalados.

## O que autoriza a abertura

O número da licença sozinho não libera o programa. O módulo de acesso abre a chave CNG persistente que o Suite criou para esse usuário, assina o desafio do servidor e verifica a resposta assinada. Não cria outra chave, não ativa outro dispositivo e não exporta a chave privada.

São preservados o produto `TURBORAMA_SUITE`, o cálculo de DeviceId a partir da chave pública, o vínculo com SID/usuário do Windows, a política TPM/software definida pela autoridade e o formato criptográfico do protocolo Suite. A cópia usa somente as partes de licenciamento necessárias; não carrega catálogo, downloads da loja, cobrança, contabilidade ou controle de tempo de locadora.

```text
Ativação já feita no Suite
          |
          +-- chave CNG deste usuário + licença/dispositivo no servidor
                            |
                +-----------+-----------+
                |                       |
          sessão do Suite       sessão EmulationStation
          rotas existentes      rotas /suite/emulationstation
                |                       |
           loja/catalogo        acesso ao frontend/jogos
```

O servidor original armazenava uma sessão por licença/dispositivo. Reabrir a mesma licença em outro cliente substituía a sessão anterior. Por isso a extensão usa tabelas próprias de desafios e sessões, consultando as mesmas licenças e dispositivos existentes. Os identificadores de desafio e sessão do EmulationStation não são aceitos nas rotas originais de sessão/catálogo; os da loja não são aceitos nas rotas da extensão. A abertura de outro EmulationStation pode substituir a sessão anterior do próprio EmulationStation, sem substituir a sessão da loja.

## Arquivos e responsabilidades

| Arquivo/pasta | Responsabilidade |
| --- | --- |
| `es-app/src/SuiteAccessGate.cpp/.h` | Valida o módulo por SHA-256, inicia o processo privado de acesso, acompanha sua autorização e falha fechado quando ele termina ou deixa de responder. |
| `es-app/src/main.cpp` | Exige acesso antes de carregar mídia/interface; observa a sessão e executa a saída normal após sua perda. |
| `es-app/src/FileData.cpp` | Confere a autorização antes de desmontar áudio/janela e iniciar um jogo. O restante do lançamento é preservado. |
| `suite-licensing/Program.cs` | Abre a autoridade assinada e a identidade existente, cria o runtime de licenciamento e a comunicação com o frontend. |
| `suite-licensing/LicenseForm.cs` | Primeiro login com a mesma licença; abre a sessão e mantém o acompanhamento enquanto o frontend estiver vivo. |
| `suite-licensing/LicenseCache.cs` | Guarda somente o identificador da licença com DPAPI CurrentUser após confirmação online. Não guarda autorização offline, sessão, código de ativação ou chave privada. |
| `suite-licensing/BridgeConnection.cs` | Comunicação privada por pipes herdados, com tokens fixos de estado. A licença e as provas criptográficas não são transmitidas nesse canal. |
| `suite-licensing/Upstream/` | Código de protocolo, confiança, transporte, identidade e sessão derivado do Suite. O manifesto e a documentação nessa pasta de integração identificam origem e adaptações. |
| `suite-licensing/Build.ps1` | Testa e publica o módulo autossuficiente com as informações públicas da autoridade embutidas. |
| `.github/workflows/compilar-cliente-suite-windows.yml` | Compila somente esta branch, preserva os testes anteriores e publica um pacote próprio. |

## Sessão, rede e encerramento

Cada abertura exige confirmação online. Durante a execução, o runtime renova a sessão com desafios assinados e usa o prazo concedido pelo servidor; uma falha transitória de rede não cria autorização adicional. A validade local usa relógio monotônico e desconta o tempo das requisições. O módulo continua trabalhando enquanto o frontend espera o emulador terminar.

O frontend acompanha o módulo por um canal privado com prazo curto de resposta. A perda da licença ou do módulo bloqueia novos lançamentos. Se um emulador já estiver rodando, o fluxo existente do jogo continua até retornar; o frontend então encerra pelo caminho normal de salvamento. O Job Object de limpeza contém apenas o módulo de acesso, não os emuladores.

O HTTP antigo do frontend não é usado para esta autorização. O módulo conserva HTTPS, validação da cadeia/nome do certificado, pin da chave pública TLS, assinatura RSA-PSS das respostas, correspondência de licença/dispositivo/sessão/desafio e rejeição de replay.

## Configuração pública e arquivos de chave

A configuração assinada e sua chave pública de emissor vêm do commit do Suite indicado acima. O hash do envelope e a chave do emissor ficam incorporados ao módulo. A chave pública de asserção online e o pin TLS são obtidos desse envelope verificado. Não há senha mestra, licença de teste ou chave privada no repositório ou nos artefatos.

A autoridade de catálogo não é necessária para abrir o frontend. Não é preciso copiar chaves privadas do servidor nem gerar uma nova ativação para construir esta edição. A assinatura Authenticode da distribuição é uma etapa distinta: os pacotes iniciais de teste deste workflow são sem assinatura digital, assim como o workflow cliente de origem. O pin entre os dois executáveis não deve ser apresentado como substituto da assinatura de toda a distribuição.

Na conferência de 05/09/2026, a pasta pública fornecida pelo proprietário, `TurboramaAuthorityPublic-20260901`, continha um envelope Suite emitido em 27/08/2026. O envelope no commit do Git foi emitido em 02/09/2026. A chave do emissor e a chave online são as mesmas, mas o pin TLS mudou. Uma conexão TLS somente de leitura confirmou cadeia/nome válidos e correspondência com o pin do Git, não com o antigo. Foi mantido o envelope atual, SHA-256 `20F7F066B654AAD700C4733C9B011495A2BB9B52E7A8B3A77E806CDEDEBFA3E6`, sem modificar a pasta original. Os arquivos de catálogo dessa pasta também diferem dos do Git e não entram nesta edição.

Nenhum programa cliente impede um administrador local de alterar seus próprios binários. As verificações implementadas protegem o fluxo normal, a origem do módulo e as provas de licença; não constituem promessa de inviolabilidade contra modificação do executável.

## Compilação no GitHub

O workflow fixa as dependências nativas no commit `468eaba48c028921a4bf2abdfa3f3a00ce8d4c0d`, usa Visual Studio 2022 x64 no runner e SDK .NET `10.0.400` para o módulo Suite. Primeiro compila/testa o módulo, calcula seu SHA-256, passa o hash ao CMake e então compila o frontend.

As opções principais são `TURBORAMA_ENABLE_COMMERCIAL_SERVICES=OFF`, `TURBORAMA_RELEASE_HARDENING=ON` e `TURBORAMA_REQUIRE_SUITE_LICENSE=ON`. O CMake recusa a configuração sem hash válido do módulo e recusa combinar esta edição com serviços comerciais. Desabilitar a opção na fonte produz outro perfil de compilação, não uma opção de usuário no EXE distribuído.

As tags de teste usam `es-suite-v1.0.0-<12 caracteres do commit>`. O workflow não move tags das versões PIX ou cliente sem serviços. O pacote inclui manifesto SHA-256, informações de origem e este manual.

## Validação e implantação

Os testes do módulo usam licenças e chaves sintéticas, sem credenciais de clientes. Cobrem assinaturas/contextos inválidos, limites de sessão e confiança. O frontend testa seu protocolo e recusa módulo ausente ou com um byte alterado. A extensão do servidor tem testes próprios de convivência das duas sessões, isolamento, replay e revogação; seu workflow executa também testes no PostgreSQL.

Diagnósticos que terminam sem abrir o frontend: `emulationstation.exe --suite-access-self-test`, `emulationstation.exe --suite-access-integrity-self-test` e `TurboRama.Suite.Access.exe --probe-identity`. Eles não concedem acesso nem iniciam jogos.

Antes de homologar a versão em produção, instalar a extensão do servidor e confirmar, no PC já ativado: abertura do Suite e do frontend simultaneamente, entrada automática subsequente, troca de usuário Windows, bloqueio/revogação, expiração durante falha de rede e retorno de um emulador. Compilação e testes sintéticos não substituem essa validação real.

## Atualização e retorno

Mantenha os dois executáveis da mesma compilação. Para voltar, restaure o pacote anterior completo da edição desejada. A extensão é aditiva no servidor e inicia desabilitada; seguir seu runbook para desligá-la sem alterar a licença, o dispositivo, a sessão original do Suite ou dados de PIX. Não remova a chave CNG e não refaça a ativação como procedimento de atualização.
