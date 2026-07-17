# Proteção pesada do Turborama ROM Linker

Este pacote prepara uma proteção forte para dificultar descompilação do programa .NET Framework 4.8.
Não existe proteção 100% impossível, mas esta configuração deixa o código bem mais difícil de ler.

## Arquivos importantes

- `Compilar_Proteger_MAXIMO.bat` — compila Release e aplica proteção pesada.
- `Compilar_Proteger_NORMAL.bat` — proteção forte, porém menos agressiva.
- `ConfuserEx_Turborama_MAXIMO.crproj` — renomeação Unicode, strings/constantes, recursos, control flow, ref proxy, anti-tamper, anti-debug, anti-dump e invalid metadata.
- `ConfuserEx_Turborama_NORMAL.crproj` — recomendado para distribuição, com menos chance de falso positivo.

## Como usar

1. Abra a solução no Visual Studio e confirme que compila em Release / Any CPU.
2. Coloque `Confuser.CLI.exe` em:

   `Protecao\Ferramentas\Confuser.CLI.exe`

3. Execute:

   `Protecao\Compilar_Proteger_MAXIMO.bat`

4. O executável protegido sai em:

   `TurboramaRomLinker\bin\Release\Protegido_MAXIMO\TurboramaRomLinker.exe`

## Recomendação profissional

- Use `MAXIMO` para teste fechado.
- Use `NORMAL` para cliente final se algum antivírus reclamar.
- Para distribuição profissional, assine digitalmente o EXE depois de proteger.
- Apague qualquer `.pdb` antes de distribuir.

## O que fica protegido

- nomes de classes, métodos, campos e propriedades;
- strings e constantes;
- recursos embutidos;
- fluxo interno do código;
- metadados do assembly;
- tentativa de dump/debug em runtime.

