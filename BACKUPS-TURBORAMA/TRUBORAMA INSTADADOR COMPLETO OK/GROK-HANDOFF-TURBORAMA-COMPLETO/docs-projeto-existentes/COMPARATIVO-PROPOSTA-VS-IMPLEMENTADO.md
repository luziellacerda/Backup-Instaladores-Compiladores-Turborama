# Comparativo: Proposta de reconstrução segura × Projeto Novo implementado

**Proposta:** texto “Proposta de reconstrução segura do TurboRama” (estudo/constituição)  
**Implementação:** `Projeto Novo TurboRama` 2.0.0-alpha (fases 0–6)  
**Data:** 2026-07-14  

### Legenda

| Símbolo | Significado |
|---------|-------------|
| **OK** | Atende de forma adequada |
| **PARCIAL** | Atende o núcleo; falta profundidade ou itens secundários |
| **NÃO** | Não implementado ou fora do entregável atual |
| **N/A** | Não aplicável nesta edição/escopo |

### Veredito executivo

| Aspecto | Avaliação |
|---------|-----------|
| **Alinhamento arquitetural com a proposta** | **Alto (~80–85%)** |
| **Princípio capture/apply/validate/rollback** | **OK** |
| **Kiosk básico seguro (conta, shell, autologon, políticas)** | **OK** |
| **Serviços Watchdog + Maintenance** | **OK** |
| **Opcionais F4 default OFF** | **OK** |
| **Pack + aceite fábrica** | **OK** (F5–F6 da implementação; F5 “qualidade” da proposta é mais ampla) |
| **Lacunas restantes (industrial / cert)** | Certificado Authenticode real (verificação soft já existe); MSIX/MSI; SecurityAgent dedicado; Shell Launcher *aplicado* (sonda + hive default seguro); CI VM automático |

O projeto **está de acordo com o espírito e o núcleo da proposta**. Não é 100% de cada parágrafo (a proposta descreve o sistema “completo ideal”; a implementação entrega o caminho **KioskBasic + fábrica** de forma segura e utilizável).

---

## §1 Objetivo do sistema

| Requisito da proposta | Status | Evidência / nota |
|----------------------|--------|------------------|
| Inicialização direta no frontend | **OK** | Launcher como shell do Arcade + autologon |
| Conta exclusiva kiosk | **OK** | Conta `Arcade` (configurável) |
| Login automático seguro | **OK** | Sysinternals Autologon + DPAPI (sem DefaultPassword em claro, na prática testada) |
| Bloqueio de acesso às configs do Windows | **OK/PARCIAL** | Políticas por SID do kiosk; sem Keyboard Filter default; sem hook global no launcher |
| Recuperação automática do frontend | **OK** | Launcher loop + Watchdog com backoff/lock |
| Modo técnico protegido | **OK** | Admin + Explorer; Enter/Sair manutenção; pipe + lock |
| Proteção de dados/config | **PARCIAL** | Layout e DPAPI; ACLs de layout capturadas no baseline; permissões finas por pasta ainda genéricas |
| Restauração completa do Windows | **PARCIAL** | Rollback por steps + baseline rico; não é “imagem completa do SO” nem restore point automático em todos os casos |
| Diagnóstico claro de falha | **OK** | `OperationResult`, logs por componente, Status, Fase 6 aceite |
| Sem substituir arquivos nativos do Windows | **OK** | APIs, Registro, serviços, sc/dism/bcdedit encapsulados |
| Só APIs/recursos oficiais | **OK** | Sem patch de binários do Windows |

---

## §2 Princípio principal

| Requisito | Status | Nota |
|-----------|--------|------|
| Nenhuma alteração sem registrar original | **OK** | `CaptureAsync` + baseline + snapshots |
| 1–4 Detectar / salvar / aplicar / validar | **OK** | `IInstallationStep` |
| 5 Restaurar em falha ou sob demanda | **OK** | Engine faz rollback dos steps aplicados; rollback por fase na UI/CLI |
| Operações pequenas e transacionais | **OK** | Steps com `Order` e nomes persistidos |
| Não um único processo longo cego | **OK** | Fases 0–6 + stages em `installation-state.json` |

---

## §3 Arquitetura de projetos

| Projeto proposto | Status | Implementado |
|------------------|--------|--------------|
| TurboRama.Core | **OK** | Results, steps, paths, logs, IPC protocol, lock, manifest |
| TurboRama.Configuration | **OK** | `turborama.json` versionado |
| TurboRama.Installation | **OK** | Engine + steps |
| TurboRama.Rollback | **OK** | Rollback ordem inversa |
| TurboRama.Windows | **OK** | Contas, shell, autologon, baseline, serviços, opcionais |
| TurboRama.Security | **OK** | DPAPI, senha, políticas kiosk |
| TurboRama.Launcher | **OK** | Loop frontend; Admin = manutenção |
| TurboRama.Watchdog | **OK** | Windows Service |
| TurboRama.Maintenance | **OK** | Windows Service + named pipe |
| TurboRama.Diagnostics | **OK** | Preflight + PostInstall (Fase 6) |
| TurboRama.UI | **OK** | WinForms + CLI |
| TurboRama.Tests | **PARCIAL** | Existe; poucos testes unitários (não E2E VM) |

### §3.1 OperationResult

| Campo exigido | Status |
|---------------|--------|
| Success, Message, ErrorCode, Exception | **OK** |
| OperationName, CommandOrApi, ExitCode | **OK** |
| PreviousState, CurrentState, CanRollback | **OK** |
| (extra) Duration | **OK** |

### §3.2 Configuration

| Requisito | Status |
|-----------|--------|
| JSON com schemaVersion, kioskUser, paths, flags F4, watchdog | **OK** |
| Versionamento de schema | **OK** (`schemaVersion`) |
| Senhas não em texto no JSON | **OK** (DPAPI em arquivo separado) |
| Credential Manager / LSA Secrets / hash PIN | **PARCIAL** | DPAPI implementado; Credential Manager e hash de PIN de manutenção não são o foco atual |

---

## §4 Instalador máquina de estados

| Requisito | Status | Nota |
|-----------|--------|------|
| Stages persistidos | **OK** | `installation-state.json` + `completedStages` |
| Continuar após falha/reboot | **PARCIAL** | Stages persistidos e skip de concluídos; resume fino “última etapa segura” e force redo por fase existem; não há recovery automático sofisticado pós-crash mid-step |
| Não repetir cegamente tudo | **OK** | Skip de stages concluídos; `force` remove subset para reaplicar |
| Enum de estágios | **OK** | NotStarted → … → Installed + Failed/RollingBack |

---

## §5 Pré-validação obrigatória

| Check da proposta | Status |
|-------------------|--------|
| Privilégio Admin | **OK** |
| Não logado como kiosk | **OK** |
| Nome kiosk válido | **OK** |
| Espaço em disco | **OK** |
| Frontend existe | **PARCIAL** (aviso, não bloqueia sempre) |
| Outra conta administrativa | **PARCIAL** | Exigida na criação da conta kiosk; preflight não lista todas as contas Admin explicitamente como o texto |
| Edição/versão Windows, x64/x86 | **NÃO/PARCIAL** | Não checado de forma completa no preflight |
| BitLocker | **NÃO** |
| UWF estado | **PARCIAL** | Status/Fase 6 e módulo F4; preflight comenta “expandir” |
| Device Lockdown / Keyboard Filter disponibilidade | **PARCIAL** | Via snapshot de features no baseline/F4, não todos no preflight |
| Integridade/hashes do pacote | **NÃO** |
| Ponto de restauração / imagem recovery | **NÃO** |
| Instalações anteriores | **PARCIAL** | State/Backup por InstallationId |
| Permissões pasta destino | **PARCIAL** | EnsureLayout; não auditoria ACL completa no preflight |
| BCD atual, shell, autologon, serviços TurboRama | **PARCIAL** | Capturados no baseline/Fase 6; preflight enxuto |
| Interromper sem Admin de recovery | **OK** | Na criação da conta kiosk (`HasOtherAdministrator`) |

---

## §6 Baseline completo

| Área | Status | Nota |
|------|--------|------|
| 6.1 Registro (path, name, tipo, existed, value, view 32/64) | **OK** | Modelo de snapshot de registro |
| 6.2 BCD export + enum + hash | **OK/PARCIAL** | Export e enum; import automático cauteloso (não cego) |
| 6.3 Recursos opcionais (DeviceLockdown, Embedded*, UWF) | **OK** | Snapshot de features |
| 6.4 Serviços (existência, start, state, bin, conta) | **OK** | ServiceSnapshot |
| 6.5 Tarefas agendadas XML | **NÃO/PARCIAL** | Não é o modelo principal (serviços em vez de tasks genéricas) |
| 6.6 ACLs icacls + owner/herança | **PARCIAL** | icacls save no baseline; modelo SID/owner menos rico |
| 6.7 Contas e perfis | **OK/PARCIAL** | Capture de conta kiosk; não inventário completo de todos os usuários do SO |

---

## §7 Diretório de instalação

| Requisito | Status |
|-----------|--------|
| Layout `C:\TurboRama\App\{Launcher,Watchdog,Maintenance,SecurityAgent}` … | **OK** (SecurityAgent pasta reservada; agente próprio não é produto ativo) |
| Separar EXE de dados | **OK** |
| ACLs recomendadas por pasta (App ro/exec, Backup só Admin) | **PARCIAL** | Layout criado; hardening ACL fino por pasta não totalmente automatizado na instalação |

---

## §8 Instalação de arquivos (atômica)

| Requisito | Status |
|-----------|--------|
| Staging → hash → assinatura → previous → current | **NÃO** | Cópia/publish direta para `App\...` |
| Não copiar sobre processo em execução | **PARCIAL** | Fase 3 para serviços antes de copiar; não pipeline `.staging/current/previous` |
| Facilitar update/rollback de versão de binários | **PARCIAL** | Rollback de steps; não versionamento atômico de pastas App |

**Lacuna importante** vs proposta §8 — deploy de arquivos ainda é “copy/publish”, não atômico completo.

---

## §9 Conta kiosk

| Requisito | Status |
|-----------|--------|
| Nome configurável (padrão Arcade) | **OK** |
| Sem Admin | **OK** |
| Sem senha vazia; senha forte aleatória | **OK** |
| Segredo de autologon seguro (DPAPI) | **OK** |
| Registrar se já existia | **OK** |
| Não reutilizar cegamente conta existente | **PARCIAL** | Atualiza senha se existir e demove Admin; não pede “autorização” UI explícita |
| Perfil por logon/CreateProfile | **OK** | CreateProfile + fallback |

---

## §10 Login automático

| Requisito | Status |
|-----------|--------|
| Opcional (`enableAutoLogon`) | **OK** |
| Preferir Sysinternals / LSA, evitar DefaultPassword | **OK** |
| Salvar valores originais | **OK** |
| Configurar + validar | **OK** |
| Impedir temporariamente autologon (técnico) | **PARCIAL** | Admin login manual; sem toggle “hold autologon” dedicado além de manutenção |
| Não alterar LimitBlankPasswordUse / PasswordLess | **OK** | Não implementamos esses relaxamentos |

---

## §11 Shell

| Requisito | Status |
|-----------|--------|
| Prioridade 1 Shell Launcher oficial | **NÃO** |
| Prioridade 2 Assigned Access | **NÃO** |
| Prioridade 3 shell por usuário (hive) | **OK** (implementado) |
| Prioridade 4 HKLM global último recurso | **OK** (não usado como caminho principal) |
| Admin → explorer | **OK** |
| Kiosk → Launcher | **OK** |
| Launcher em Admin = manutenção, sem bloqueios | **OK** |

---

## §12 Launcher

| Requisito | Status |
|-----------|--------|
| Iniciar frontend, reiniciar, logar falhas | **OK** |
| UI carregamento / desligar / reboot / auth técnico | **PARCIAL** | Loop + MessageBox básico; UX “loading form” rica do legado não portada por completo |
| Comunicação com serviço manutenção | **PARCIAL** | Protocolo existe; launcher não é cliente rico de todos os comandos |
| Não alterar Registro/BCD/serviços no boot | **OK** |
| Não rodar como Admin permanente | **OK** |

---

## §13 Watchdog

| Requisito | Status |
|-----------|--------|
| Windows Service | **OK** |
| Reiniciar launcher com política de backoff | **OK** |
| Detectar loop / recovery | **OK** (TR-008 / recovery.flag) |
| maintenance.lock impede restart | **OK** |
| Desativável pelo modo técnico | **OK** | Enter maintenance / lock |

---

## §14 Serviço de manutenção

| Requisito | Status |
|-----------|--------|
| Serviço restrito | **OK** |
| Named pipe + ACL (Admin/SYSTEM) | **OK** |
| Só comandos predefinidos | **OK** |
| Sem shell genérico | **OK** |
| Comandos: reboot, shutdown, enter/exit kiosk/maintenance, etc. | **OK/PARCIAL** | STATUS, ENTER/EXIT, REBOOT, SHUTDOWN, restart launcher…; “instalar update” / UWF via pipe não todos expostos |

---

## §15 Bloqueios do kiosk

| Camada | Status |
|--------|--------|
| 1 Usuário sem privilégios | **OK** |
| 2 Políticas por SID | **OK** |
| 3 Keyboard hook no launcher (Win/Alt+Tab…) | **NÃO/PARCIAL** | Não é o núcleo do Launcher atual |
| 4 Keyboard Filter default OFF + condições | **OK** | Módulo F4 default OFF |

Ctrl+Alt+Del não tratado como requisito básico: **OK** (alinhado).

---

## §16 UWF

| Requisito | Status |
|-----------|--------|
| Módulo opcional independente | **OK** |
| Default OFF | **OK** |
| Exclusões Data/Saves/Logs/Config (+ App etc.) | **OK** no enable |
| Validar edição/overlay | **PARCIAL** | Detecta presença uwfmgr; UI de overlay “agora/próximo boot” simplificada |
| Painel técnico rico UWF | **PARCIAL** | Status texto; não dashboard completo |

---

## §17 Boot e branding

| Requisito | Status |
|-----------|--------|
| Separado do kiosk principal | **OK** | Fase 4 checkbox |
| BCD com backup | **OK** | BootBranding + baseline BCD |
| Embedded Boot completo | **PARCIAL** | Branding leve; não full Embedded Boot Exp productizado |

---

## §18–27 (tecnologia, atualização, segurança avançada, etc.)

A proposta (trecho final) cita stack e práticas:

| Tema | Status |
|------|--------|
| .NET + Windows Services + WinForms | **OK** |
| Registry API | **OK** |
| sc/DISM/BCD encapsulados | **OK** |
| JSON versionado | **OK** |
| Logging (Serilog) | **PARCIAL** | Logger próprio em arquivo (equivalente funcional, não Serilog) |
| Assinatura digital de EXEs | **NÃO** |
| MSIX/MSI | **NÃO** | Pack pasta/ZIP + BATs |
| Task Scheduler API | **NÃO** (serviços preferidos) |
| Atualização atômica App | **NÃO** (§8) |
| SecurityAgent dedicado | **NÃO** (pasta reservada) |

---

## §28 Estratégia de testes

| Requisito | Status |
|-----------|--------|
| Testes em VMs descartáveis (matriz grande) | **NÃO** automatizado |
| Unit tests | **PARCIAL** | Poucos |
| Aceite pós-install (Fase 6) | **OK** | Validação local com 25 checks |
| Liberar só após install+restore auto | **PARCIAL** | Processo manual + Fase 6; não CI VM |

---

## §29 Ordem de desenvolvimento da proposta × o que foi feito

| Proposta | Implementação | Status |
|----------|---------------|--------|
| **Fase 1** fundação + baseline + rollback | Fases **0–1** | **OK** |
| **Fase 2** kiosk básico | Fase **2** | **OK** |
| **Fase 3** confiabilidade (watchdog, manutenção, recovery) | Fase **3** (+ parte update atômico ainda fraca) | **OK/PARCIAL** |
| **Fase 4** proteção avançada (UWF, Filter, Embedded, branding) | Fase **4** default OFF | **OK/PARCIAL** (Embedded logon/boot completos limitados) |
| **Fase 5** qualidade (VM tests, assinatura, instalador final, docs recovery) | Fase **5** pack + docs + Fase **6** aceite | **PARCIAL** (pack e docs OK; VM/assinatura/MSI não) |

*Numeração:* a implementação adicionou **Fase 0** (fundação), **Fase 5 pack**, **Fase 6 aceite** — mapeamento conceitual acima.

---

## §30 Quinze correções vs monólito antigo

| # | Correção exigida | Status no Projeto Novo |
|---|------------------|-------------------------|
| 1 | Não shell global se por usuário bastar | **OK** |
| 2 | Não senha kiosk vazia | **OK** |
| 3 | Não relaxar LimitBlankPasswordUse etc. | **OK** |
| 4 | Não Keyboard Filter por padrão | **OK** |
| 5 | Não BCD sem export/rollback | **OK** (baseline + módulo F4 com backup) |
| 6 | Não timeouts globais 1s | **OK** (não aplicado) |
| 7 | Não UWF sem exclusões | **OK** (default OFF; exclusões se enable) |
| 8 | Não apagar perfil sem validação | **OK/PARCIAL** |
| 9 | Não alterar sem capturar anterior | **OK** |
| 10 | Não “Explorer voltou = restaurado” | **OK** (baseline/validação) |
| 11 | Não watchdog recria config no rollback | **OK** (lock/recovery) |
| 12 | Não um único EXE faz tudo | **OK** |
| 13 | Não frontend permanente como Admin | **OK** |
| 14 | Não só BAT/PS para operações críticas | **OK** (lógica em C#; BAT só bootstrap) |
| 15 | Não “original restaurado” sem comparar baseline | **OK/PARCIAL** | Comparação via snapshots; UI de diff completa pode evoluir |

**As 15 correções críticas da proposta estão fundamentalmente atendidas.**

---

## §31 Resultado esperado

| Resultado desejado | Status |
|--------------------|--------|
| Menor risco de bloquear o Windows | **OK** |
| Manutenção mais simples (Admin + manutenção) | **OK** |
| Instalação retomável | **PARCIAL** |
| Rollback confiável | **OK** no desenho dos steps |
| Logs claros | **OK** |
| Segurança de credenciais | **OK** (DPAPI) |
| Isolamento kiosk × admin | **OK** |
| Atualizações seguras | **PARCIAL** (§8 atômico faltando) |
| Diagnóstico completo | **OK** (Status + F6) |
| Recuperação se frontend não inicia | **OK** (launcher loop + watchdog; recovery se loop) |

---

## Tabela-resumo por “bloco” da proposta

| Bloco | Adequação |
|-------|-----------|
| Objetivos (§1) | **~90%** |
| Princípio transacional (§2) | **~95%** |
| Arquitetura projetos (§3) | **~95%** |
| Máquina de estados (§4) | **~80%** |
| Preflight (§5) | **~45–55%** (núcleo sim; lista longa incompleta) |
| Baseline (§6) | **~75–85%** |
| Layout (§7) | **~90%** |
| Deploy atômico (§8) | **~25%** |
| Conta kiosk (§9) | **~90%** |
| Autologon (§10) | **~90%** |
| Shell (§11) | **~70%** (hive OK; Shell Launcher/AA não) |
| Launcher (§12) | **~70%** |
| Watchdog (§13) | **~90%** |
| Maintenance (§14) | **~85%** |
| Bloqueios (§15) | **~70%** (hook launcher fraco) |
| UWF (§16) | **~75%** |
| Branding/BCD (§17) | **~60%** |
| Qualidade/testes/assinatura (§28–29 F5 proposta) | **~40%** |
| 15 correções (§30) | **~95%** |

**Média ponderada (núcleo arcade seguro):** projeto **de acordo**.  
**Média se exigir 100% de cada linha da proposta ideal:** ainda **não completo**.

---

## O que falta para ficar “100% proposta” (backlog priorizado)

### Prioridade alta
1. **Deploy atômico** de App (`.staging` / `current` / `previous` + hash).  
2. **Preflight expandido** (edição Windows, BitLocker aviso, serviços/tarefas TurboRama existentes, shell/autologon atuais).  
3. **Testes VM** mínimos (install + reboot + rollback) documentados/automatizados.

### Prioridade média
4. Shell Launcher / Assigned Access quando a edição suportar (antes do hive).  
5. Keyboard hook no launcher (camada 3) opcional.  
6. UI técnica UWF mais rica.  
7. Cliente de manutenção no launcher (PIN/comandos).

### Prioridade baixa / industrialização
8. Assinatura digital + MSIX/MSI.  
9. SecurityAgent.  
10. Serilog / logging estruturado avançado.  
11. Credential Manager além de DPAPI.  
12. Diff visual baseline vs atual na UI.

---

## Conclusão direta

| Pergunta | Resposta |
|----------|----------|
| O projeto está **de acordo** com a proposta? | **Sim, no essencial e na filosofia de segurança.** |
| É a implementação **literal completa** de todos os parágrafos? | **Não** — há lacunas (deploy atômico, preflight longo, Shell Launcher oficial, testes VM, assinatura). |
| Pode ser usado como **kiosk arcade seguro** alinhado ao texto? | **Sim** (Fases 0–4 núcleo + 5 pack + 6 aceite). |
| As **15 mudanças mais importantes** (§30) foram feitas? | **Sim, de forma substantiva.** |

**Frase final:** o Projeto Novo TurboRama **implementa a proposta de reconstrução segura** como sistema operacional de kiosk **transacional, modular e reversível**; o que falta é sobretudo **endurecimento industrial** (deploy atômico, preflight máximo, qualidade VM/assinatura), não a correção do modelo antigo inseguro.
