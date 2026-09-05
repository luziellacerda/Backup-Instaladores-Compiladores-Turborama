# Validação da nova edição Suite — 05/09/2026

Frontend: `CLIENTE-SUITE-ATIVADO-v1.0.0-20260905`, fonte `4194c99c3515217b6a00e330f067ae7e7a10a128`.

Servidor: `codex/emulationstation-suite-v1-20260905`, fonte `769f8b44c87b53ec6393276548a61da79b43aa22`.

## Evidências concluídas

- Base cliente preservada contra `5a356172013a620a1a0ecf151c00c9238ea21a24`: apenas cinco arquivos existentes alterados pelos pontos de integração; core, memória, menus, vídeos, áudio e workflows anteriores preservados.
- Componente nativo C++17 compilado com `/W4 /WX`; testes de hash, protocolo, expiração e integração de processos/pipes passaram.
- Módulo gerenciado compilado localmente com alvo de compatibilidade SDK 9 instalado. Verificadores de assinatura, TLS, replay, capacidade, expiração/renovação monotônica, DPAPI e comunicação passaram.
- Consulta local somente de leitura encontrou identidade Suite existente no provedor `SOFTWARE_BOUND_ONLINE`. Nenhuma ativação, criação de chave ou sessão real executada.
- TLS do servidor conferido com validação normal da cadeia/nome: corresponde ao pin do envelope Suite atual do Git; não corresponde ao envelope anterior da pasta pública fornecida.
- Backups privados indicados pelo usuário não foram abertos, copiados ou alterados; apenas seus nomes/tamanhos foram consultados para identificar a finalidade.
- [Validação herdada do servidor](https://github.com/luziellacerda/Servidor-pix/actions/runs/33974034488): sucesso.
- [Servidor EmulationStation e PostgreSQL 16](https://github.com/luziellacerda/Servidor-pix/actions/runs/33974034512): sucesso. Aplicação das migrations 001–022, permissões, coexistência Suite/ES, provas cruzadas, replay, revogação e elegibilidade comercial testados em banco isolado.
- Artefato do servidor: `servidor-suite-emulationstation-review-769f8b44c87b53ec6393276548a61da79b43aa22`, id `9971779967`, 704843 bytes, disponível na execução acima.
- [Build frontend Windows](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/actions/runs/33974073584): **sucesso completo**. SDK 10.0.400, preservação da base, compilação/verificador do módulo, build nativo Release x64, testes comerciais negativos, otimizações preservadas e validação/empacotamento aprovados.
- O log registrou `NO_COMMERCIAL_SERVICES_TEST=OK`, nove marcadores de otimização preservados, nove marcadores não comerciais preservados e oito comandos comerciais rejeitados.
- O log registrou `SUITE_PACKAGE_INTEGRITY=OK (correto, ausente, alterado, restaurado)`.
- SHA-256 do módulo de acesso gerado e fixado no frontend: `50ea067538e2d9216f3b7afc990b5b328f76e0f0bfae2b5205a28eca3940cbc1`.
- Conferência remota após publicação: branches anteriores continuam em `5a356172013a620a1a0ecf151c00c9238ea21a24` (cliente) e `a4ffde18530c2268bd6fee1c78bd660ffe328e48` (PIX).

## Pacotes publicados

[Pré-release Suite v1.0.0](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/releases/tag/es-suite-v1.0.0-4194c99c3515), vinculada exatamente ao commit de fonte `4194c99c3515217b6a00e330f067ae7e7a10a128`.

| Pacote | Bytes | SHA-256 |
| --- | ---: | --- |
| `Turborama-ES-Suite-v1.0.0-Atualizacao.zip` | 831984790 | `51002631f70897d7fe57229a4bf4d89bf6a39e169760894682da9ee2d476a716` |
| `TurboramaEmulationStation-Suite-v1.0.0-Windows-x64.zip` | 997686811 | `16ec52df1a6fd23fefa4a001f0035a19933d53da696c5752ed7f2e4ece1761fe` |

O pacote de atualização contém os dois executáveis que precisam ser substituídos juntos. O pacote completo contém também os runtimes e recursos. Os pacotes foram produzidos no GitHub e não foram baixados neste PC. Os tamanhos e hashes acima foram conferidos nos metadados dos assets publicados; o hash do pacote completo coincide com o registrado pelo workflow.

## Etapa operacional ainda necessária

A extensão não foi instalada nem habilitada em produção. O recurso continua desabilitado por padrão. O login real no novo frontend exige instalar a migration 022 e o servidor candidato, publicar as duas rotas no proxy se necessário e habilitar `Suite__EmulationStation__Enabled=true` no serviço Suite. Seguir o runbook da branch do servidor. A licença real fornecida pelo usuário não está em commits, testes, logs ou artefatos.

O pacote inicial de frontend é uma pré-release de teste sem Authenticode. As verificações acima não equivalem a uma homologação de login real, áudio de jogos ou execução em outra máquina.
