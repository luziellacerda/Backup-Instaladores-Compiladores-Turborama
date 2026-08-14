# HANDOFF COMPLETO — TURBORAMA PIX ADMIN R5 — 2026-08-14

Este documento não contém Access Token, Public Key, Client Secret, código de ativação, senha, chave
privada nem dado secreto. Valores publicados anteriormente não podem ser copiados para código, Git,
documentação ou comandos.

## 1. Regra principal

- Trabalhar com evidência real; não mascarar, inventar ou completar dados ausentes.
- Identidade válida do gabinete atual: conta local `Admin`.
- Não usar Arcade, Arkade, arkae ou TurboRama Kiosk como identidade deste gabinete.
- O kiosk base é imutável.
- Escopo único: sobreposição PIX com `emulationstation.exe`,
  `CONFIGURAR-USER-TOKEN-PIX.exe`, `CONFIGURAR-ACCESS-TOKEN-PIX.exe` e `pix-agent`.
- Não alterar Launcher, ROMs, temas, cache, créditos, configuração-base, instalador base do kiosk nem
  as referências funcionais em `D:`.
- Não entregar `.cmd` ou `.bat`.
- Licenciamento on-line não controla preços nem substitui o provedor local. Preços continuam locais no
  EmulationStation. Sem internet, o kiosk e créditos locais continuam funcionando; somente operações
  que realmente dependem da rede, como nova cobrança/baixa Mercado Pago, podem ficar indisponíveis.

## 2. Regra de armazenamento da compilação

- Fonte versionada permanece em:
  `C:\Users\Admin\Documents\Codex\2026-08-04\c-users-admin-documents-codex-2026\TurboramaEmulationStation-repo-QR-FINAL`.
- Todo temporário, cache, restauração NuGet, objeto, CMake, compilação, extração, smoke, validação e
  artefato de teste deve ficar sob `H:\TurboRamaTemp`.
- Não gerar novamente cache ou saída de build dentro do repositório em `C:`.
- Foi feita uma segunda inspeção dos arquivos ignorados pelo Git. Foram removidos somente saídas de
  build não rastreadas: `bin`, `obj`, caches CMake antigos, PDBs, objetos nativos e duas entregas de
  teste já substituídas. O junction `bin\plugins` foi desassociado antes da remoção, sem seguir ou
  apagar seu destino. Total aproximado liberado em `C:`: `1,0 GiB`.
- Depois da limpeza, a busca por `.obj`, `.pdb`, `.ilk`, `.ipdb`, `.iobj` e `CMakeCache.txt` dentro do
  repositório retornou zero. Fontes, dependências necessárias, arquivos versionados e referências em
  `D:` foram preservados.
- Uma tentativa de captura visual em `H:` não produziu arquivo porque a sessão automatizada não tinha
  um handle de desktop válido. Nada foi gravado em `C:` ou `D:` por essa tentativa.

## 3. Repositório e estado de trabalho

- Branch local: `PIX-R5-ADMIN-COMPILADORES-20260813`.
- As alterações desta continuação ainda não foram commitadas nem enviadas.
- Arquivos modificados:
  - `COMPILAR-PIX-COMERCIAL-v25.ps1`;
  - `TurboramaEmulationStation/CMakeLists.txt`;
  - `TurboramaEmulationStation/es-app/src/PixBinaryTrust.h`;
  - `TurboramaEmulationStation/tools/TurboRamaCommercialInstaller/TurboRamaBootstrapper.cpp`;
  - `TurboramaEmulationStation/tools/TurboRamaCommercialInstaller/TurboRamaInstaller.cpp`;
  - `TurboramaEmulationStation/tools/TurboRamaPixOwnerConfigurator/README.md`;
  - `TurboramaEmulationStation/tools/TurboRamaPixOwnerConfigurator/TurboRamaPixOwnerConfigurator.cpp`.
- `git diff --check`: sem erro; aparecem apenas avisos informativos de futura conversão LF para CRLF.
- Busca no diff pelos valores secretos anteriormente expostos: zero ocorrência.
- Nenhuma pasta atual de build ficou não rastreada no repositório.

## 4. Estado real encontrado no gabinete

- A instalação em `D:\emulationstation` contém binários anteriores às correções atuais.
- Os erros vistos no executável instalado não provam falha do fonte atual porque o instalado possui
  hashes diferentes dos novos binários em `H:`.
- Não existe arquivo de erro/status suficiente para recuperar com certeza o motivo histórico exato de
  todo código `10`; essa causa não deve ser inventada.
- O erro antigo `PDV LZPIXCOMP não foi encontrado` pertence a um cadastro legado local Mercado Pago.
- As capturas mostram quatro pares Loja/PDV válidos na mesma conta Mercado Pago, não quatro contas
  financeiras diferentes.
- O programa deve vincular somente uma conta Mercado Pago por máquina, mas pode encontrar vários
  pares Loja/PDV dentro dessa conta antes da limpeza.

## 5. Correções no `CONFIGURAR-USER-TOKEN-PIX.exe`

- Consulta o titular real pelo `/users/me`; Client ID e Application ID não são aceitos como User ID.
- Confere a conta autenticada antes de trocar segredo, cadastro local ou criar recurso remoto.
- Cadastro local ausente ou `accountId` explicitamente vazio é primeiro vínculo.
- Cadastro malformado, grande demais, ambíguo, com schema incompatível ou provider incompatível falha
  fechado antes de trocar segredo ou criar Loja/PDV.
- Uma conta autenticada diferente da conta já vinculada é recusada como conta secundária.
- O daemon também recusa credencial protegida que pertença a outra conta, inclusive quando o cadastro
  já está `ready`.
- `VER CADASTROS` mostra todos os pares compatíveis da conta.
- Quando o par salvo localmente coincide exatamente com o inventário, ele aparece como
  `[ATUAL NESTE PC]` e é pré-selecionado.
- Sem coincidência exata, o programa não adivinha qual dos quatro pares é o correto.
- O operador pode:
  - usar somente o par escolhido; ou
  - usar o par escolhido e remover os outros pares TurboRama.
- A limpeza automática só considera recursos associados com Loja `LZLOJA*` e PDV `LZPIX*`.
- PDVs TurboRama antigos, inativos e o legado `LZPIXCOMP` também entram no plano de remoção; eles não
  aparecem como opção utilizável, mas não podem sobreviver como cadastro antigo.
- Recursos que não pertencem ao padrão TurboRama são preservados.
- O PDV escolhido é preservado.
- Outros PDVs gerenciados são excluídos primeiro pelo ID interno numérico.
- Loja antiga só é excluída se estiver vazia depois da remoção dos PDVs.
- Antes e depois da limpeza, a conta é consultada novamente. Mudança de titular, desaparecimento do
  par escolhido ou permanência de outro par gerenciado produz erro explícito.
- A limpeza exige confirmação em janela separada e nunca é iniciada por simples consulta.
- O cadastro escolhido é validado e ativado localmente antes da limpeza. Se uma exclusão posterior
  falhar, o programa informa limpeza parcial e não mascara o erro.
- O layout principal foi limitado ao gabinete real: tela `1360x768`, área útil real `1360x728`.
- O autoteste verifica que a janela calculada cabe em `1360x728` e que os controles permanecem dentro
  do cliente de `1040x680`.

Documentação oficial usada para as exclusões:

- PDV: `DELETE /pos/{id}`;
- Loja: `DELETE /users/{user_id}/stores/{id}`.

## 6. Correções no agente PIX

- `ValidateSingleMercadoPagoAccount` impede troca silenciosa da conta vinculada.
- `ReadExistingMercadoPagoAccountId` falha fechado para cadastro existente que não prove de forma
  inequívoca a conta anterior.
- O provider legado `online` ainda permite ler o User ID antigo somente para migração; não volta a ser
  provedor de pagamento.
- `BindAuthenticatedAccount` não corrige automaticamente uma conta divergente. Divergência bloqueia
  compras.
- Os testes cobrem segunda conta, segredo sentinela preservado, zero POST remoto e arquivos de cadastro
  inválidos/ambíguos.

## 7. Correções no compilador e instalador

- O compilador comercial ganhou `-DiretorioTemporarioBuild`.
- Ordem de escolha do temporário: parâmetro, `TURBORAMA_BUILD_TEMP_ROOT`, depois `TEMP` atual.
- A raiz de uma unidade é recusada como diretório temporário.
- Lock, CMake, NuGet, smoke, pacote intermediário, extração e retenção ficam dentro da fronteira
  informada.
- CMake ganhou `TURBORAMA_OUTPUT_DIRECTORY`; o EmulationStation é escrito diretamente no destino de
  build em `H:`.
- O teste real de fronteira criou somente caminhos em
  `H:\TurboRamaTemp\compiler-boundary-test` e parou corretamente porque o Git está sujo, antes da
  assinatura.
- O parser do PowerShell aceitou o script atual.
- `PixBinaryTrust.h` retirou o buffer SHA-256 de 64 KiB da pilha e o mantém em heap com limpeza.
- Bootstrapper e configurador retiraram buffers grandes de caminho da pilha.
- Instalador corrigiu o fechamento potencial de handle nulo apontado pela análise estática.
- A correção anterior da DACL permanece: aplicação/restauração usam direitos somente de segurança,
  sem `MAXIMUM_ALLOWED` na rotina real.

## 8. Binários internos atuais em `H:`

### CONFIGURAR-USER-TOKEN-PIX.exe

- Caminho:
  `H:\TurboRamaTemp\patched-20260814-r5\owner\CONFIGURAR-USER-TOKEN-PIX.exe`
- Tamanho: `600576` bytes.
- SHA-256: `BE2BF62A012D141659F376DEB1D28C41E3A0B9BC00C457095A3C0E37336C0EFC`.
- Fonte foi salva antes do binário: comprovado por timestamp.
- Compilação `/analyze /W4 /WX`: zero avisos e zero erros; relatório contém
  `<DEFECTS></DEFECTS>`.
- `--self-test`: código `0`.

### CONFIGURAR-ACCESS-TOKEN-PIX.exe

- Caminho:
  `H:\TurboRamaTemp\validation-20260814-r2\access\CONFIGURAR-ACCESS-TOKEN-PIX.exe`
- Tamanho: `308736` bytes.
- SHA-256: `AEB4FB27543A893D7BA6496765156352635E5473352AAF037F37D995A7982F8E`.
- Lógica funcional do ACCESS não foi alterada nesta continuação.
- Compilação `/W4 /WX`: código `0`.
- `--self-test`: código `0`.

### TurboRamaPixAgent.dll

- Caminho:
  `H:\TurboRamaTemp\validation-20260814-r2\agent-out\TurboRamaPixAgent.dll`
- Tamanho: `542208` bytes.
- SHA-256: `EAC27978DE8230ACA93071A4335C374FF2E9D0173C76C3EEA7DB97AB7908C3F9`.
- Fonte foi salva antes do binário: comprovado por timestamp.
- Build `warnaserror`: zero avisos e zero erros.
- Autoteste pelo apphost correspondente: código `0`.

### EmulationStation

- Caminho:
  `H:\TurboRamaTemp\es-build-20260814-r2\output\emulationstation.exe`
- Tamanho: `789630976` bytes.
- SHA-256: `7AD674DBBC30EE19538065F9420EACA66546B0B4A94184D4D925923FD718D623`.
- Build Release completo: `327` etapas, código `0`.
- Tema incorporado: `992` arquivos, gerado em `H:`.
- O código PIX novo e o executável têm ordem temporal de fonte antes do binário.
- O build completo possui avisos legados/upstream do EmulationStation e bibliotecas externas; não
  declarar o EmulationStation inteiro como livre de avisos.

### Instalador interno

- Caminho:
  `H:\TurboRamaTemp\validation-20260814-r2\installer\TurboRamaInstaller.exe`
- Tamanho: `569344` bytes.
- SHA-256: `D86DCF96187E5FDB40CBA7FED607301C47D235EF97E14BE2291FBB2A4791646D`.
- Fonte foi salva antes do binário: comprovado por timestamp.
- Compilação `/analyze /W4 /WX`: zero avisos e zero erros próprios.
- `--self-test`: código `0`.

### Bootstrapper interno

- Caminho:
  `H:\TurboRamaTemp\validation-20260814-r2\installer\TurboRamaBootstrapper.exe`
- Tamanho: `243712` bytes.
- SHA-256: `050E6EE680E5DE6E6F49EBFEE840C367BF3FC38CDC549A9A8EECA3902C994A19`.
- Compilação `/analyze /W4 /WX`: zero avisos e zero erros próprios.
- `--self-test`: código `0`.

Todos esses executáveis são internos e não assinados. Não são release comercial para venda.

Matriz final repetida depois da última correção do legado `LZPIXCOMP`:

- USER `0`;
- ACCESS `0`;
- agente `0`;
- instalador `0`;
- diagnóstico da identidade instalada `Admin` `0`;
- bootstrapper `0`;
- os cinco testes do EmulationStation `0`;
- QR confirmou `QR_CACHE_TEST=OK`.

## 9. Testes do EmulationStation exato em `H:`

Pasta de execução completa:
`H:\TurboRamaTemp\es-validation-20260814-r3`.

- `--protected-decorations-self-test`: código `0`.
- `--pix-agent-manager-self-test`: código `0`.
- `--pix-agent-trust-self-test`: código `0`.
- `--credit-warning-overlay-self-test`: código `0`.
- `--pix-test-qr-cache`: código `0`; arquivo confirmou `QR_CACHE_TEST=OK`.

Registro de falha preservado: a primeira execução do teste de confiança respondeu
`Agente PIX nao foi instalado` porque a pasta de validação ainda não continha `pix-agent`. Depois de
copiar para `H:` os dez arquivos do agente novo exato, incluindo a DLL SHA-256 acima, o teste foi
repetido e passou com código `0`. Isso confirma o conjunto, não o EXE isolado.

## 10. Testes reais ainda não executados

- Nenhuma credencial publicada no chat foi reutilizada ou gravada.
- Não foi feita chamada autenticada real ao Mercado Pago nesta continuação.
- Não foi excluído nenhum dos quatro pares remotos.
- Não foi criada cobrança real, QR financeiro real nem pagamento real.
- DPAPI e gravação do cadastro protegido ainda precisam ser testados no contexto interativo real da
  conta `Admin`; a sandbox não possui essa identidade operacional.
- O programa novo não foi instalado sobre `D:` nesta continuação.
- A captura automatizada da janela falhou por ausência de handle válido do desktop; portanto o
  dimensionamento possui prova automática e cálculo contra a resolução real, mas a aparência final
  ainda precisa de inspeção humana.
- Não existe instalador único novo desta continuação: o compilador comercial recusou corretamente a
  árvore Git suja e não há certificado privado de assinatura disponível.
- A documentação oficial atual confirmou `DELETE /pos/{id}` e
  `DELETE /users/{user_id}/stores/{id}`. O DNS de `pix.lzgames.com.br` resolveu para a Cloudflare,
  mas a sessão automatizada não conseguiu abrir TCP 443; isso não prova indisponibilidade do servidor
  e o health público atual ficou `NÃO COMPROVADO` nesta sessão.

## 11. Critérios para o próximo teste humano

1. Usar somente a conta Windows `Admin` configurada no JSON/Winlogon.
2. Abrir o `CONFIGURAR-USER-TOKEN-PIX.exe` novo a partir da pasta de teste em `H:`.
3. Inserir a credencial diretamente na interface privada, sem chat, arquivo ou comando.
4. Abrir `VER CADASTROS`.
5. Confirmar visualmente qual par aparece como `[ATUAL NESTE PC]`.
6. Se o par atual for o correto, escolher `USAR E REMOVER OUTROS TURBORAMA`.
7. Confirmar no retorno do programa a quantidade exata de PDVs e lojas vazias removidos.
8. Reabrir `VER CADASTROS` e provar que resta somente o par escolhido.
9. Iniciar o EmulationStation e confirmar que o agente publica PIX disponível sem `LZPIXCOMP`.
10. Fazer primeiro uma cobrança real mínima controlada; registrar apenas status, IDs não secretos e
    resultado, nunca a credencial.

Se qualquer uma dessas etapas falhar, não gerar instalador final e não formatar antes de preservar o
fonte/commit. Registrar texto exato, código de saída e arquivos de status sanitizados.

## 12. Critérios antes de release comercial

- Teste humano completo acima aprovado.
- Teste real de falta de internet confirmando que o kiosk continua navegável.
- Teste de cobrança, pagamento, crédito e reconciliação ponta a ponta.
- Git limpo e commit revisado.
- Compilação comercial reproduzível usando `H:\TurboRamaTemp`.
- Certificado Authenticode privado válido.
- Manifesto e assinaturas verificados.
- Instalador único testado em ambiente isolado e depois no gabinete.

Até isso ocorrer, o estado correto é: **fontes corrigidos e binários internos testados; produção
Mercado Pago e release comercial ainda não comprovadas**.
