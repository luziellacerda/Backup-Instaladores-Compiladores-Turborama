# Turborama ROM Linker - LZ Games

Projeto WinForms .NET Framework 4.8.

Correção desta versão:

- Os ícones de sistemas em `Resources\SystemIcons` agora são `EmbeddedResource`.
- O programa carrega os ícones diretamente de dentro do `.exe`, usando `Assembly.GetManifestResourceStream`.
- Os ícones não precisam existir como arquivos externos na pasta de saída.
- O Visual Studio não copia `Resources\SystemIcons` para `bin\Release`.
- Sidebar mantida e layout escuro/cyberpunk preservado.

Compilar:

1. Abrir `TurboramaRomLinker.sln`.
2. Selecionar `Release / Any CPU`.
3. Recompilar solução.

Regra de uso:

O executável deve ficar na raiz do Turborama, uma pasta acima de `sistema`:

```text
<raiz>\TurboramaRomLinker.exe
<raiz>\sistema\emulationstation\.emulationstation\es_systems.cfg
<raiz>\sistema\roms\
```

As ROMs extras são procuradas em:

```text
C:\TurboRoms\roms
D:\TurboRoms\roms
...
Z:\TurboRoms\roms
```

