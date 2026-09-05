# Validação da Suite Edition 1.0.1 — 05/09/2026

## Resultado confirmado

Compilação, testes e publicação aprovados no GitHub Actions:

- Fonte: `74449ddc631abda82e23f0d8b80d6bf3fba90f16`.
- Implementação do módulo embutido: `192137a874a2e8d08d9a3e8a3463bef63f1cf58c`.
- [Execução aprovada 33976233515](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/actions/runs/33976233515),
  job `101333303937`.
- [Release de teste es-suite-v1.0.1-74449ddc631a](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/releases/tag/es-suite-v1.0.1-74449ddc631a),
  publicado em 05/09/2026 às 13:02:02, UTC−03:00.
- Runner padrão Windows Server 2022, Visual Studio 2022 e .NET SDK 10.0.400.
  Repositório frontend público; nenhum serviço pago de assinatura foi acrescentado.

## Artefatos e integridade

| Arquivo | Bytes | SHA-256 |
| --- | ---: | --- |
| `emulationstation.exe` | 905412096 | `cce256ebe8d50f9c87e1ee7204042640197cbfd877a978883c2d24d5ec8b9111` |
| `Turborama-ES-Suite-v1.0.1-Atualizacao.zip` | 831971106 | `729cd2b0aaaccb236f2d03d6adf38585305f99f9dde0462fb64c47a48a7a2982` |
| `TurboramaEmulationStation-Suite-v1.0.1-Windows-x64.zip` | 997685000 | `8f1e9ac1c7846385cf2f779e894f861a8b42199768ace3e0de4b04b0ade4f78d` |

O release inclui os arquivos `.sha256` do EXE e do ZIP completo. O hash do módulo
Suite incorporado nesta compilação é
`1dde6d2e12c3b9334881648012a5b26d69c7f2d30952be747afa53ec0a696e8e`.
O módulo não é entregue como arquivo adjacente. A atualização contém somente
o EXE e o manual; as dependências da instalação continuam necessárias.

Os hashes do EXE e do ZIP completo registrados nos logs correspondem aos digests
de assets informados pelo GitHub. O pacote novo não foi baixado no PC do usuário.
O pacote completo teve 176 binários x64 e 154 plugins VLC verificados.

## Testes aprovados

- Preservação da base cliente `5a356172013a620a1a0ecf151c00c9238ea21a24`.
- Testes gerenciados de confiança, assinatura, sessão, replay, validade monotônica,
  cache DPAPI e comunicação privada.
- Testes nativos com módulo sintético: recurso correto/ausente/adulterado, ausência
  de fallback externo, ACL, bloqueio de escrita/exclusão, ambiente controlado,
  identidade local, pipes, revogação e limpeza.
- Perfil sem serviços: oito comandos comerciais recusados; nove marcadores de
  otimização e nove marcadores não comerciais preservados, além das verificações
  de implementação, fontes excluídas e propriedades de hardening.
- DLL gerenciada própria sem os oito marcadores comerciais proibidos.
- Pacote real: integridade válida, probe de identidade ausente esperado no runner
  limpo, marcador adjacente ignorado, byte do recurso adulterado em uma cópia
  recusado e hash do EXE original intacto.

## Falha encontrada e correção limitada ao teste

A execução anterior `33975615916`, fonte `192137a`, compilou o EXE, mas não
publicou release: a varredura binária antiga encontrou `CreditManager` no runtime
.NET incorporado. A classe Microsoft `System.Net.Http.CreditManager` pertence ao
controle de fluxo HTTP, não ao sistema de créditos do TurboRama. Foi conferida
na biblioteca local .NET 10.0.11 e no [código oficial do runtime](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/CreditManager.cs).

A correção `74449dd` não altera fontes nativas/gerenciadas do aplicativo. O scanner
verifica o hash exato do recurso e trata somente esse nome completamente dentro
dele. O mesmo nome fora do recurso continua proibido, os outros sete marcadores
são procurados no EXE inteiro, e a DLL própria é examinada pelos oito sem exceção.
Os testes de borda, repetição, troca de bloco e limites inválidos passaram.

## Diagnóstico local adicional

Foi criado um executável de diagnóstico com o gate nativo atual e o helper real
1.0.0 já disponível no PC, sem novo download. Seu hash conhecido foi conferido
antes e depois, sem alterar o arquivo original. A extração .NET, o probe da
identidade existente (código 0), a rejeição de adulteração (44) e os testes de
pacote passaram. Não ficou diretório privado de extração após os testes.

Esse executável local é somente uma fixture de diagnóstico, não é o frontend
distribuível nem a versão final 1.0.1. O teste não fez login, assinatura de desafio,
ativação, abertura de sessão ou chamada ao servidor. O GitHub validou separadamente
o EXE final com o módulo 1.0.1 realmente publicado.

## Lógica e dados preservados

O código de `Upstream/`, `Program.cs`, `BridgeConnection.cs`, `LicenseForm.cs`,
`LicenseCache.cs` e `PublicAuthority.g.cs` permanece idêntico ao da integração
anterior `4164802`. O projeto gerenciado mudou somente sua versão para 1.0.1.
Mesma licença, chave CNG existente, autoridade e protocolo; sessões ES separadas
para coexistência com a Suite. Nenhuma chave privada ou licença de cliente foi
incluída nos fontes, testes ou artefatos.

Branches remotas anteriores conferidas sem alteração:

- Cliente sem serviços: `5a356172013a620a1a0ecf151c00c9238ea21a24`.
- PIX: `a4ffde18530c2268bd6fee1c78bd660ffe328e48`.
- Extensão do servidor permanece em `769f8b44c87b53ec6393276548a61da79b43aa22`.

## O que ainda não foi homologado

Não houve implantação ou habilitação da extensão em produção nesta atividade.
O login real depende das rotas/flag descritas no manual da versão. Ainda devem
ser validados no ambiente ativo: login simultâneo com a Suite, bloqueio em outro
PC/usuário, revogação, perda de rede e retorno dos jogos. A existência local da
chave não prova uma licença autorizada naquele momento pelo servidor.

O build continua sem Authenticode e pode mostrar SmartScreen. A proteção controla
o acesso desta edição no fluxo normal; não criptografa ROMs/arquivos existentes
nem promete impedir modificação por administrador local. Nenhum certificado
ou serviço pago foi contratado.

O commit que adiciona este relatório é somente documentação; a fonte compilada
e publicada permanece exatamente `74449ddc631abda82e23f0d8b80d6bf3fba90f16`.
