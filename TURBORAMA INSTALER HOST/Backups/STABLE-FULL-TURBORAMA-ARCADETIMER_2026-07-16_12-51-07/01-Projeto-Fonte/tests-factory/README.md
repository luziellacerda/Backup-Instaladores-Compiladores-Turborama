# Testes de fábrica — TurboRama Kiosk (+ Timer quando existir)

## Scripts

| Script | O quê |
|--------|--------|
| `scripts\SMOKE-KIOSK.bat` | Segurança + agent + filter (seguro) |
| `..\..\TurboRamaArcadeTimer_Proposta\tests\lab\*.bat` | Base Timer |

## Checklists cruzados

Copiados/referência:

- Timer isolado / cruzado / power → pasta do ArcadeTimer:  
  `D:\Backup-Instaladores-Compiladores-Turborama\TurboRamaArcadeTimer_Proposta\tests\checklists\`

## Ordem na linha (máquina nova)

1. Instalar pack TurboRama → reboot  
2. `SMOKE-KIOSK.bat` → PASS  
3. Validar Ctrl+End + PIN kiosk  
4. (Opcional) Instalar Arcade Timer → checklists A + B  
5. `BACKUP-SEGURANCA-PANE.bat` no projecto / pack  

## Resultados

Guardar em `results\` com data.
