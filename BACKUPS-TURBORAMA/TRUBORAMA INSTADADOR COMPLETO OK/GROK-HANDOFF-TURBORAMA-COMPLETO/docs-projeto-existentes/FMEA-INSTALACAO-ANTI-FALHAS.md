# FMEA / Anti-falhas — Instalação TurboRama (produto)
Data: 2026-07-20

## Bugs CRÍTICOS corrigidos nesta rodada
B01 DeployLauncher só paths de dev → agora pack/seed
B02 DeployServices FindExe sem pack → pack/seed
B03 Seed sem parar serviços → stop+kill+retry
B04 Pack incompleto aceito → fail duro
B05 Autologon só warning → erro preflight/seed
B06 .NET 8 só warning → erro preflight + install-full
B07 schtasks SecurityAgent escape → bat intermediário
B08 install paralelo → mutex Global\TurboRamaFactoryFullInstall

## Bloqueios de propósito (OK falhar)
Sem Admin | Logado Arcade | Sem Admin recovery | Disco C <500MB
Sem .NET 8 Desktop | Pack incompleto | Hash diverge | Install duplicado

## Runtime
Frontend ausente = kiosk sobe sem jogo (aviso)
Crash loop = recovery.flag + manutenção
KF completo só após reboot em IoT

## Checklist venda
PREFLIGHT 0 erros → INSTALAR-COMPLETO → reboot Arcade → VALIDAR-ACEITE
Ctrl+End menu | Admin Explorer | copiar D:\Turborama

## Logs
C:\TurboRama\Logs\Installer\ | Launcher\ | Watchdog\ | SEGURANCA-STATUS.txt
