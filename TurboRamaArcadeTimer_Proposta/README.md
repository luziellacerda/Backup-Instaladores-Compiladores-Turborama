# TurboRama Arcade Timer

Projeto completo em C# WinForms (.NET 8) para controle de ficha e tempo em máquina arcade.

## Compilar

Execute:

```bat
COMPILAR.bat
```

Ou:

```bat
cd src\TurboRama.ArcadeTimer
dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true
```

## Versão de testes pronta (0.1.0-test)

```text
dist\TurboRama-ArcadeTimer-0.1.0-test\
dist\TurboRama-ArcadeTimer-0.1.0-test.zip
```

- EXE self-contained win-x64  
- `INICIAR-TESTES.bat` · F10 = ficha · Notepad = jogo lab  
- Checklists e smoke do kiosk incluídos  

## Base de testes (lab + kiosk)

Pasta `tests\` — checklists, configs e smokes:

```bat
tests\lab\SMOKE-KIOSK.bat
tests\lab\PREPARAR-LAB-TIMER.bat
tests\lab\SMOKE-TIMER-BUILD.bat
```

Checklists: `tests\checklists\A-TIMER-ISOLADO.md`, `B-CRUZADO-KIOSK.md`, `C-POWER-E-QUEDA.md`  
Config lab (F10 + Notepad como emulador falso): `tests\configs\config.lab.json`

## Teste sem hardware (rápido)

1. `tests\lab\PREPARAR-LAB-TIMER.bat`
2. Execute o EXE na pasta `tests\lab\bin-smoke`
3. Pressione `F10` (ficha de teste).
4. Abra o **Notepad** (lista lab) ou um emulador real.
5. O tempo diminui; ao zerar, só o emulador/Notepad fecha.
6. O EmulationStation não deve ser encerrado.

## Hardware

O aceitador de ficha deve usar encoder USB, Arduino, ESP32, I-PAC ou interface equivalente.

Nunca conecte diretamente 12 V do aceitador à porta USB.

## Arquivos próprios do programa

- `config.json`
- `credit.json`
- `credit.backup.json`
- `logs/`

O programa não altera arquivos do kiosk, EmulationStation, RetroArch ou emuladores.
