# 02-cliente: TurboramaEmulationStation/es-app/src/guis/GuiMenu.h

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Declarações dos menus acompanhando a presença ou ausência de serviços.

- Antes: `76b214874973fe24017823401216896f3d7a6f40`.
- Depois: `947dad45f5e9cd556cce6f15045a5dd6119bdf95`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/guis/GuiMenu.h).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 40, depois 40

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/guis/GuiMenu.h#L40) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/guis/GuiMenu.h#L40)

```text
ANTES | DEPOIS |   CÓDIGO
   40 |     40 |           static void updateGameLists(Window* window, bool confirm = true);
   41 |     41 |           static void editKeyboardMappings(Window *window, IKeyboardMapContainer* mapping, bool editable);
   42 |     42 |   
      |     43 | +         #ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
   43 |     44 |           // Locadora — acesso ao painel (F11 / senha)
   44 |     45 |           static void requestCreditSettingsAccess_static(Window* window);
   45 |     46 |           static void openCreditSettings_static(Window* window);
      |     47 | +         #endif
      |     48 | + 
      |     49 | +         #ifdef TURBORAMA_NO_COMMERCIAL_SERVICES
      |     50 | +         // F11 conserva somente as acoes gerais do sistema, sem o painel comercial.
      |     51 | +         static void requestTurboSystemMenuAccess_static(Window* window);
      |     52 | +         #endif
   46 |     53 |   
   47 |     54 |           // Menu Start protegido por senha admin
   48 |     55 |           static void requestMainMenuAccess_static(Window* window);
   49 |     56 |   
   50 |     57 |   private:
      |     58 | +         #ifdef TURBORAMA_NO_COMMERCIAL_SERVICES
      |     59 | +         // Somente o fluxo autenticado acima pode abrir as acoes privilegiadas.
      |     60 | +         static void openTurboSystemMenu_static(Window* window);
      |     61 | +         #endif
      |     62 | + 
   51 |     63 |           void addEntry(const std::string& name, bool add_arrow, const std::function<void()>& func, const std::string iconName = "");
   52 |     64 |           void addVersionInfo();
   53 |     65 |           void openCollectionSystemSettings();
```

## Trecho 2: antes 64, depois 76

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/es-app/src/guis/GuiMenu.h#L64) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/es-app/src/guis/GuiMenu.h#L76)

```text
ANTES | DEPOIS |   CÓDIGO
   64 |     76 |           void openNetworkSettings(bool selectWifiEnable = false);        
   65 |     77 |           void openQuitMenu();
   66 |     78 |           void openSystemInformations();
      |     79 | +         #ifndef TURBORAMA_NO_COMMERCIAL_SERVICES
   67 |     80 |           void openCreditSettings();
   68 |     81 |           void requestCreditSettingsAccess();
   69 |     82 |           void requestCreditAccountingAccess();
   70 |     83 |           static void requestCreditAccountingAccess_static(Window* window);
   71 |     84 |           static void openCreditAccounting_static(Window* window);
   72 |     85 |           static void openCreditManageCredits_static(Window* window);
      |     86 | +         #endif
   73 |     87 |           void openServicesSettings();
   74 |     88 |           void openMultiScreensSettings();
   75 |     89 |           void openDmdSettings();
```

Conferência: 2 trechos, 14 linhas adicionadas e 0 removidas.

