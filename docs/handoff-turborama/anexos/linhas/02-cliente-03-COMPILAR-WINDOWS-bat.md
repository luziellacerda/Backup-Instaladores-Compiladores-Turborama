# 02-cliente: TurboramaEmulationStation/COMPILAR-WINDOWS.bat

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Atalho local de construção: encaminha os parâmetros da variante. Não é o workflow GitHub.

- Antes: `76b214874973fe24017823401216896f3d7a6f40`.
- Depois: `947dad45f5e9cd556cce6f15045a5dd6119bdf95`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/COMPILAR-WINDOWS.bat).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 99, depois 99

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/COMPILAR-WINDOWS.bat#L99) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/COMPILAR-WINDOWS.bat#L99)

```text
ANTES | DEPOIS |   CÓDIGO
   99 |     99 |     mkdir build 2>nul
  100 |    100 |   )
  101 |    101 |   
  102 |        | - "%CMAKE_EXE%" -S . -B build -G "Visual Studio 17 2022" -A x64
      |    102 | + "%CMAKE_EXE%" -S . -B build -G "Visual Studio 17 2022" -A x64 -DTURBORAMA_ENABLE_COMMERCIAL_SERVICES=OFF -DTURBORAMA_RELEASE_HARDENING=ON
  103 |    103 |   if errorlevel 1 (
  104 |    104 |     echo.
  105 |    105 |     echo [ERRO] cmake configure falhou.
```

## Trecho 2: antes 132, depois 132

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/COMPILAR-WINDOWS.bat#L132) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/COMPILAR-WINDOWS.bat#L132)

```text
ANTES | DEPOIS |   CÓDIGO
  132 |    132 |   )
  133 |    133 |   echo.
  134 |    134 |   echo  Teste: emulationstation.exe --windowed --debug --resolution 1280 720
  135 |        | - echo  Credito: F10 = ficha  ^| sem credito = nao lanca jogo
  136 |        | - echo  Config:  %%USERPROFILE%%\.emulationstation\arcade_credit.cfg
      |    135 | + echo  Perfil: cliente sem PIX, pagamentos, locadora ou controle de tempo
      |    136 | + echo  Jogos e demais recursos do EmulationStation permanecem ativos
  137 |    137 |   echo ============================================================
  138 |    138 |   pause
  139 |    139 |   exit /b 0
```

Conferência: 2 trechos, 3 linhas adicionadas e 3 removidas.

