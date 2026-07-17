# Descritivo completo — Projeto Novo TurboRama Secure

**Documento de fechamento e comparativo com o projeto/estudo inicial**  
**Versão do produto:** 2.0.0-alpha  
**Fases entregues:** 0 a 6  
**Data de referência:** 2026-07-14  
**Repositório:** `Projeto Novo TurboRama\`

---

## 1. Origem e decisão de arquitetura

### 1.1 Fontes do projeto inicial

| Fonte | Papel | Caminho típico |
|-------|--------|----------------|
| **Estudo / constituição** | Regras de segurança e método de instalação | `AUDITORIA GPT COMO CONSTRUIR PROGRAMA.txt` |
| **TurboRamaFactoryShell** | Referência de UX de fábrica, preflight, pack, recovery | `TurboRamaFactoryShell\` |
| **Pacote estável / monólito** | Comportamento histórico a **não** copiar cegamente | BACKUP-INSTALADOR-ESTAVEL / legados |

### 1.2 Decisão central (greenfield)

- Construir um **projeto novo**, modular, em **.NET 8 / Windows**.
- O **estudo** manda (capture → apply → validate → rollback).
- O **FactoryShell** inspira fluxo de fábrica, **não** é monólito a copiar.
- **Não** unificar instalador + launcher + segurança + recovery em um único EXE.
- **Não** senha kiosk vazia; **não** shell global se shell por usuário bastar; **não** UWF/Keyboard Filter por padrão.

### 1.3 Princípio (estudo)

> Nenhuma alteração no Windows sem: detectar estado → salvar original (baseline) → aplicar → validar → poder restaurar exatamente o capturado.

---

## 2. O que foi construído (visão geral)

### 2.1 Solução modular

```text
Projeto Novo TurboRama\
  TurboRama.sln
  src\
    TurboRama.Core            # Results, steps, paths, logs, IPC protocol, lock
    TurboRama.Configuration   # turborama.json versionado
    TurboRama.Windows         # contas, shell, autologon, baseline, serviços, opcionais
    TurboRama.Security        # DPAPI, gerador de senha, políticas kiosk
    TurboRama.Installation    # engine + steps IInstallationStep
    TurboRama.Rollback        # rollback ordem inversa
    TurboRama.Diagnostics     # preflight + aceite Fase 6
    TurboRama.Launcher        # shell do kiosk (frontend loop)
    TurboRama.Watchdog        # Windows Service — reinicia launcher
    TurboRama.Maintenance     # Windows Service — named pipe manutenção
    TurboRama.UI              # instalador WinForms + CLI
  tests\TurboRama.Tests
  docs\
  scripts\
  *.bat (compilar, fases, pack, aceite)
```

### 2.2 Runtime em disco (equipamento)

```text
C:\TurboRama\
  App\Launcher|Watchdog|Maintenance|Tools
  Frontend\ Config\ Data\ Saves\ Logs\ State\ Backup\ Recovery\ Updates\
```

### 2.3 Perfil padrão

**KioskBasic** — conta Arcade, shell por usuário, autologon Sysinternals, políticas por SID, watchdog/maintenance.  
**Sem** Keyboard Filter, UWF ou branding de boot por padrão.

---

## 3. Fases — o que cada uma fez (detalhado)

### Fase 0 — Fundação

| Item | Entrega |
|------|---------|
| Solução .NET 8 | Multi-projetos, `Directory.Build.props` |
| `ProductPaths` | Layout canônico `C:\TurboRama` |
| `OperationResult` | Resultado padronizado (sucesso/erro/código) |
| `IInstallationStep` | Capture / Apply / Validate / Rollback |
| `InstallationEngine` | Orquestra steps; em falha, rollback do que aplicou |
| `ConfigurationStore` | `turborama.json` |
| Preflight | Admin, não logado como kiosk, disco, frontend aviso |
| UI base | WinForms instalador |
| Logs | `FileTurboRamaLogger` por área |

**Comparável ao estudo:** base de “steps seguros” e preflight antes de mexer no Windows.  
**Comparável ao FactoryShell:** preflight + layout, sem monólito.

---

### Fase 1 — Baseline e prova de rollback

| Item | Entrega |
|------|---------|
| `CaptureWindowsBaselineStep` | Registro (Winlogon/LSA/etc.), BCD export, ACL, serviços, features |
| Baseline em JSON | `C:\TurboRama\Backup\<InstallationId>\baseline\baseline.json` |
| Change manifest | `change-manifest.json` do que se pretende alterar |
| `Phase1ProbeStep` | Prova Capture→Apply→Validate→Rollback em marcador de registro |
| Rollback real | Restaura o capturado, não “Explorer voltou = ok” |

**Comparável ao estudo:** § baseline obrigatório, restauração por comparação.  
**vs FactoryShell:** baseline mais estruturado (documento versionado + manifesto).

---

### Fase 2 — Kiosk básico

| Step | Função |
|------|--------|
| `DeployLauncher` | Publica/copia `TurboRama.Launcher.exe` |
| `CreateKioskAccount` | Conta **Arcade** padrão (não Admin), senha forte, **DPAPI** |
| `ConfigureUserShell` | Shell **por usuário** (hive NTUSER) → Launcher, **não** HKLM global se possível |
| `ConfigureAutologon` | Sysinternals **Autologon64** (sem depender de DefaultPassword em claro) |
| `ApplyKioskPolicies` | Políticas por SID do kiosk + backup |

**Segurança aplicada:**

- Proíbe senha vazia / conta kiosk = Admin.
- Exige outra conta Administrador (recuperação).
- API `UserPrincipal` para criar conta (evita hang de `net.exe` em alguns hosts).
- Senha sem caracteres que quebram linha de comando.

**Validado em máquina de teste:**

- Arcade existe, não é Admin.
- AutoAdminLogon=1, DefaultUserName=Arcade.
- DefaultPassword ausente no Winlogon.
- Admin entra e sai com Explorer normal.
- Reboot: entra em modo kiosk (Arcade + Launcher).

**Comparável ao estudo:** shell por usuário primeiro; sem blank password; Admin de recovery.  
**vs FactoryShell:** `KioskAccountHelper` / `AutoLoginHelper` / `ShellInstaller` reescritos com o contrato Capture/Apply/Validate/Rollback.

---

### Fase 3 — Serviços (Watchdog + Maintenance)

| Componente | Função |
|------------|--------|
| `TurboRama.Watchdog` | Serviço Windows; reinicia Launcher com backoff; para em loop (TR-008) se recovery |
| `TurboRama.Maintenance` | Serviço + named pipe `TurboRamaMaintenance` (comandos fixos: PING, STATUS, ENTER/EXIT_MAINTENANCE, etc.) |
| `maintenance.lock` | Suspende reinícios do watchdog (modo técnico) |
| `DeployServicesBinaries` | Publica com dependências; **para serviços** antes de sobrescrever DLLs |
| `InstallWindowsServices` | `sc create/start`, binPath correto |
| UI Status | Consulta serviços + pipe + UWF/KB (rápido); timeout global para não travar UI |

**Problemas resolvidos na implementação:**

| Problema | Correção |
|----------|----------|
| Serviço 1053 | Host `AddWindowsService` + publish completo de deps |
| Sharing violation ao reaplicar Fase 3 | Stop + kill residual + copy com retry |
| Status travava no pipe | Timeout rígido no cliente; servidor multi-instância + read timeout |
| `ProcessRunner` sem timeout real | Leitura assíncrona + `WaitForExit` |

**Comparável ao estudo:** serviços separados; manutenção sem shell genérico no pipe.  
**vs FactoryShell:** watchdog/maintenance como serviços reais .NET 8, não processo ad-hoc único.

---

### Fase 4 — Módulos opcionais (default OFF)

| Módulo | Serviço | Default |
|--------|---------|---------|
| UWF | `UwfModuleService` (exclusões TurboRama) | OFF |
| Keyboard Filter | `KeyboardFilterModuleService` | OFF |
| Boot branding leve | `BootBrandingModuleService` (BCD com backup) | OFF |

- UI: checkboxes + **Aplicar opcionais** / **Rollback opcionais**.
- Só aplica o que estiver marcado (`EnableUwf`, `EnableKeyboardFilter`, `EnableBootBranding`).
- Nesta máquina de teste (Home/Pro): UWF **não disponível** — comportamento esperado.

**Comparável ao estudo:** default OFF + aviso de risco.  
**vs FactoryShell:** `UwfHelper` / CAD / BootUi equivalentes, sem ativar na instalação básica.

---

### Fase 5 — Pack de fábrica

| Item | Entrega |
|------|---------|
| `scripts\Build-FactoryPack.ps1` | Publish UI/Launcher/Watchdog/Maintenance + monta pasta |
| `GERAR-PACK-FABRICA.bat` | Atalho de geração |
| Saída | `D:\tr-factory-pack\TurboRama-Factory-Pack\` + `.zip` |

**Conteúdo do pack:**

```text
Installer\TurboRama.UI.exe
App\Launcher|Watchdog|Maintenance|Tools\Autologon64.exe
Config\turborama.json
Frontend\          (placeholder do jogo)
INSTALAR.bat
INSTALAR-AUTOMATICO.bat   (--phase2 + --phase3 quiet)
PREFLIGHT.bat
STATUS.bat
REINSTALAR-SERVICOS.bat
VALIDAR-ACEITE.bat        (Fase 6)
00-COMECE-AQUI.txt / LEIA-ME-FABRICA.txt
docs\ (regras, comparativo, mapa)
```

**Comparável ao estudo:** distribuição controlada, não “copiar monólito”.  
**vs FactoryShell:** equivalente a `FactoryPackBuilder` + BATs de fábrica, em pacote limpo modular.

---

### Fase 6 — Aceite de fábrica (parte 6)

| Item | Entrega |
|------|---------|
| `PostInstallValidationService` | ~25 checks de saúde + segurança |
| CLI | `--validate` / `--phase6` / `--accept-factory` + `--clear-locks` |
| UI | botão **Fase 6 Aceite** |
| `VALIDAR-ACEITE.bat` | Execução elevada quiet |
| Relatório | `C:\TurboRama\Logs\Installer\phase6-accept-*.txt` |

**Checks principais:** layout, bins, baseline, Arcade não Admin, DPAPI, autologon, DefaultPassword ausente, serviços RUNNING, locks, defaults F4, frontend, pipe.

**Teste de referência:** `Success=True`, OK=25, AVISOS=0, ERROS=0.

**Comparável ao estudo:** validação pós-install e “não declarar restaurado/pronto sem evidência”.  
**vs FactoryShell:** equivalente a validar pós-instalar / diagnóstico de aceite, formalizado como fase.

---

## 4. Fluxo operacional completo (do zero a um PC kiosk)

```text
1. Gerar pack (máquina de build):
   GERAR-PACK-FABRICA.bat
   → D:\tr-factory-pack\TurboRama-Factory-Pack(.zip)

2. PC alvo (conta Admin, NÃO Arcade):
   - (Opcional) colocar jogo em Frontend\
   - INSTALAR.bat  → UI
     ou INSTALAR-AUTOMATICO.bat → Fase 2 + 3 quiet
   - Preflight OK
   - Fase 2 Kiosk
   - Fase 3 Serviços
   - (Opcional) Fase 4 só se edição/risco ok
   - Reiniciar

3. Pós-reboot:
   - Autologon Arcade + Launcher (+ frontend se path ok)
   - Admin: Outro usuário → manutenção com Explorer

4. Aceite:
   - VALIDAR-ACEITE.bat / Fase 6 Aceite
   - Relatório OK → liberar para fábrica/chão
```

### CLI do instalador

| Argumento | Função |
|-----------|--------|
| `--preflight` | Pré-checagens |
| `--phase2` / `--install-phase2` | Kiosk |
| `--phase3` / `--install-phase3` | Serviços |
| `--validate` / `--phase6` | Aceite |
| `--clear-locks` | Remove maintenance.lock / recovery.flag |
| `--quiet` / `-q` | Sem MessageBox |
| `--result <arquivo>` | Grava OK/FAIL + mensagem |
| `--rollback-phase2` / `--rollback-phase3` | Rollback por fase |

---

## 5. Comparativo com o projeto inicial (matriz)

### 5.1 Estudo (constituição) × Projeto Novo

| Requisito do estudo | Como o Projeto Novo atende |
|---------------------|----------------------------|
| Capture antes de alterar | `CaptureAsync` em cada step + baseline F1 |
| Rollback real | `RollbackService` + snapshots em Backup |
| Sem senha kiosk vazia | `PasswordGenerator` + DPAPI; validação de tamanho |
| Shell preferencialmente por usuário | `ConfigureUserShell` / hive NTUSER |
| Não misturar tudo num EXE | UI / Launcher / Watchdog / Maintenance separados |
| Watchdog com freio de loop | recovery.flag / maintenance.lock / TR-008 |
| Manutenção controlada | Pipe com comandos predefinidos |
| UWF/Filter default off | Config + UI Fase 4 desmarcados |
| Preflight | `PreflightService` |
| Validação pós | Fase 6 `PostInstallValidationService` |
| Logs por componente | pastas em `C:\TurboRama\Logs\...` |

### 5.2 FactoryShell (legado) × Projeto Novo

| Área legada | Módulo novo | Status |
|------------|-------------|--------|
| FactoryDeployer | InstallationEngine + steps | Entregue |
| WindowsBaselineHelper | WindowsBaseline + BaselineStore | Entregue |
| ShellInstaller / WinlogonBridge | UserShell + Autologon | Entregue (por usuário) |
| ShellLauncher | TurboRama.Launcher | Entregue |
| AutoLoginHelper | SysinternalsAutologon + DPAPI | Entregue |
| KioskAccountHelper | LocalAccountService | Entregue |
| KioskPolicyHelper | KioskPolicyService | Entregue |
| TurboRamaWatchdog | TurboRama.Watchdog (serviço) | Entregue |
| Maintenance + PIN | Maintenance pipe + UI Enter/Sair | Entregue (comandos pipe; PIN legado não é o foco) |
| UwfHelper / CAD / Boot | Optional\* modules F4 | Entregue default OFF |
| FactoryPackBuilder | Build-FactoryPack.ps1 + pack | Entregue |
| InstallPreflightHelper | PreflightService | Entregue |
| Validar pós-instalar | Fase 6 | Entregue |

### 5.3 O que **não** foi copiado do monólito (de propósito)

Conforme `REGRAS-NAO-FAZER.md` e o estudo:

1. Shell global HKLM como primeira opção  
2. Senha kiosk vazia / PasswordLess relaxado  
3. Keyboard Filter default  
4. BCD sem export  
5. Timeouts Windows em 1s  
6. UWF sem exclusões  
7. Um único binário “faz tudo”  
8. Scripts BAT como única lógica crítica  
9. Declarar “restaurado” sem baseline  

---

## 6. Segurança — estado validado (máquina de referência)

| Controle | Resultado típico de teste |
|----------|---------------------------|
| Conta Arcade | Existe, ativa |
| Arcade em Administrators | **Não** |
| Conta Admin de recovery | Existe e entra/sai normal |
| Senha kiosk | DPAPI (`kiosk-user.secret`) |
| DefaultPassword Winlogon | Ausente (bom) |
| Autologon | Arcade |
| Serviços | RUNNING + AUTO_START |
| Fase 4 | Tudo false |
| Pack fábrica | ZIP + pasta com EXEs e BATs |
| Fase 6 | ACEITE OK (0 erros) |

**Risco residual conhecido (produto, não instalador):**

- Frontend (`D:\Turborama\TurboRama.exe` ou `C:\TurboRama\Frontend\...`) pode sair com code 0; Launcher reinicia — estabilidade do **jogo** é responsabilidade do build do frontend.
- Pack é **framework-dependent** (.NET 8 Desktop Runtime no PC alvo).
- ACL do secret: depende de permissões NTFS do admin; não é senha em texto no registro.

---

## 7. Scripts e artefatos de operação

| Artefato | Função |
|----------|--------|
| `COMPILAR.bat` | restore + build + test |
| `ABRIR-FASE1..4.bat` | atalhos de desenvolvimento por fase |
| `GERAR-PACK-FABRICA.bat` | gera pack Fase 5 (+ F6 no pack) |
| `VALIDAR-ACEITE.bat` | Fase 6 |
| `REINSTALAR-SERVICOS.bat` | reaplicar serviços |
| `scripts\REINICIAR-MAINTENANCE-PIPE.bat` | redeploy Maintenance com fix de pipe |
| `D:\tr-factory-pack\TurboRama-Factory-Pack\` | pacote de distribuição |
| `D:\tr-factory-pack\TurboRama-Factory-Pack.zip` | mesmo pack compactado |

---

## 8. Histórico de problemas resolvidos (implementação)

1. **SDK .NET ausente** → SDK portátil `D:\tr-dotnet`  
2. **UI/DLL locked** → publish em pastas alternativas (`ui-status`, `ui-phase2c`, pack)  
3. **Serviço 1053** → hosting correto + deps publicadas  
4. **Sharing violation na Fase 3** → stop serviços antes do copy  
5. **Status hang** → timeouts pipe + sc + UI WhenAny  
6. **Pipe max 1 instância presa** → server multi-worker + read timeout  
7. **`net user` hang na criação de conta** → AccountManagement + timeout em ProcessRunner  
8. **maintenance.lock preso** → Sair manutenção / Fase 6 `--clear-locks`  

---

## 9. Escopo entregue vs fora de escopo

### Entregue (0–6)

- Instalador modular seguro  
- Baseline + kiosk + serviços + opcionais + pack + aceite  
- Admin de recuperação preservado  
- Documentação de regras, mapa legado, comparativo, este descritivo  

### Fora de escopo / evolução futura (não bloqueia 0–6)

| Item | Nota |
|------|------|
| Frontend/jogo embutido oficial | Pack só tem pasta `Frontend\` |
| Publish self-contained | Hoje exige .NET 8 Runtime |
| PIN de manutenção idêntico ao legado | Pipe Admin; UX PIN pode evoluir |
| Testes E2E em VM automatizados | Fase 6 cobre aceite local; VM é extra |
| Hardening extremo (AppLocker, WDAC) | Não era fase 0–6 |

---

## 10. Como usar este descritivo com o projeto inicial

1. **Leia o estudo** `AUDITORIA GPT COMO CONSTRUIR PROGRAMA.txt` como constituição.  
2. **Compare** cada regra com a §5 deste documento.  
3. **Use o mapa legado** `MAPA-LEGADO.md` para achar o módulo novo de cada classe antiga.  
4. **Use o pack** Fase 5 em PC novo e **VALIDAR-ACEITE** Fase 6 para liberar.  
5. **Não** reabra o monólito FactoryShell como base de código — só referência de comportamento.

---

## 11. Resumo executivo

| Pergunta | Resposta |
|----------|----------|
| O que é o Projeto Novo? | Reconstrução **segura e modular** do kiosk TurboRama em .NET 8 |
| Compatível com o estudo inicial? | **Sim** — método capture/apply/validate/rollback + 15 regras “não fazer” |
| Compatível com FactoryShell? | **Comportamento/fábrica sim**; **código monólito não** (greenfield) |
| Fases | **0–6 todas entregues e testadas na máquina de referência** |
| Pronto para fábrica? | **Sim**, com pack + aceite Fase 6 e Admin de recovery |

---

## 12. Índice de documentação do repositório

| Documento | Conteúdo |
|-----------|----------|
| `README.md` | Visão geral, build, fases |
| `docs/REGRAS-NAO-FAZER.md` | 15 proibições do estudo |
| `docs/MAPA-LEGADO.md` | FactoryShell → módulos novos |
| `docs/COMPARATIVO-E-CAMINHO.md` | Fontes + status das fases |
| **`docs/DESCRITIVO-COMPLETO-PROJETO.md`** | **Este arquivo — descritivo total e comparativo** |

---

*Fim do descritivo. Gerado para alinhamento com o estudo inicial e o legado FactoryShell, refletindo o estado entregue do Projeto Novo TurboRama Secure (2.0.0-alpha, fases 0–6).*
