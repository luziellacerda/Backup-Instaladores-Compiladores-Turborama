# Roteiro de testes em VM (mínimo de liberação)

Ambiente: Windows 10/11 x64 descartável, snapshot limpo, .NET 8 Desktop Runtime, conta **Admin**.

## Preparação

1. Snapshot **limpo** (antes do TurboRama).  
2. Copiar `TurboRama-Factory-Pack` para a VM.  
3. (Opcional) Colocar `Frontend.exe` em `Frontend\`.

## Casos obrigatórios

| ID | Cenário | Passos | Esperado |
|----|---------|--------|----------|
| T01 | Preflight limpo | `PREFLIGHT.bat` | Sem ERRO bloqueante |
| T02 | Install automático | `INSTALAR-AUTOMATICO.bat` | Exit 0, Arcade existe, serviços RUNNING |
| T03 | Reboot kiosk | Reiniciar | Autologon Arcade + Launcher |
| T04 | Admin recovery | Outro usuário → Admin | Explorer normal |
| T05 | Status pipe | UI Status ou Fase 6 | Watchdog+Maintenance OK |
| T06 | Aceite | `VALIDAR-ACEITE.bat` | ACEITE OK (0 erros) |
| T07 | Manutenção | Enter + Sair manutenção | lock on/off |
| T08 | Frontend ausente | Remover frontend, reboot Arcade | Launcher avisa, não trava SO |
| T09 | Rollback kiosk | `--rollback-phase2` (cuidado em prod) | Estado reverte conforme snapshots |
| T10 | Reinstall serviços | `REINSTALAR-SERVICOS.bat` com RUNNING | Para, copia, sobe RUNNING |

## Casos recomendados

| ID | Cenário |
|----|---------|
| T11 | Conta Arcade já existente |
| T12 | Pouco disco (&lt; 500 MB) → preflight ERRO |
| T13 | Install interrompido (kill UI no meio) → reexecutar auto (resume) |
| T14 | Fase 4 nada marcado → noop |
| T15 | BitLocker on → preflight AVISO |

## Critério de liberação de versão

- T01–T07 e T10 **PASS**  
- T08 **PASS** (degradação controlada)  
- T06 report arquivado em `Logs\Installer`  
- Nenhuma das 15 proibições do estudo violada  

## Automação sugerida (futuro)

PowerShell na VM:

```powershell
# exemplo
& .\INSTALAR-AUTOMATICO.bat
# reboot + script de validação pós-logon
& .\VALIDAR-ACEITE.bat
```

---

*Não substitui teste em hardware arcade real antes de volume de fábrica.*
