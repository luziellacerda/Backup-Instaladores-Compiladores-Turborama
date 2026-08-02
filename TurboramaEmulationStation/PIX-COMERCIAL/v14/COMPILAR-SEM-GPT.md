# Compilar o TurboRama PIX Comercial v14 sem GPT

Na raiz do repositorio, execute com dois cliques:

`COMPILAR-PIX-COMERCIAL-v14.cmd`

O compilador faz automaticamente:

1. verifica Visual Studio Build Tools C++, CMake, .NET 8 e 7-Zip;
2. compila o EmulationStation x64 Release;
3. compila e executa o autoteste do agente PIX;
4. compila o editor externo do Access Token;
5. compila o instalador e o bootstrapper nativos;
6. inclui um runtime .NET 8 privado;
7. gera o instalador EXE unico;
8. confere os hashes internos e testa o arquivo `payload.7z`.

Os arquivos prontos ficam em:

`TurboramaEmulationStation\PIX-COMERCIAL\GERADO-v14`

## Teste completo do instalador

Para tambem instalar em uma pasta isolada de teste:

```powershell
powershell -ExecutionPolicy Bypass -File .\COMPILAR-PIX-COMERCIAL-v14.ps1 -TestarInstalador
```

## Opcoes uteis

- `-Limpar`: apaga somente as pastas de build v14 reconhecidas e recompila do zero.
- `-UsarEmulationStationExistente`: recompila os componentes PIX e reaproveita o `emulationstation.exe` ja gerado; util para teste rapido do empacotamento.
- `-TestarInstalador`: executa uma instalacao real somente na pasta isolada de teste.

O compilador nao acessa `D:\emulationstation`, nao le `secret.dat` e nunca inclui Access Token, cadastro do proprietario, ROMs, temas ou creditos no pacote.

Python nao e necessario: o proprio PowerShell gera o tema embutido.
