# 02-cliente: TurboramaEmulationStation/tools/compilar.ps1

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Orquestração local de compilação; os valores da variante devem coincidir com o workflow.

- Antes: `76b214874973fe24017823401216896f3d7a6f40`.
- Depois: `947dad45f5e9cd556cce6f15045a5dd6119bdf95`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/tools/compilar.ps1).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 389, depois 389

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/TurboramaEmulationStation/tools/compilar.ps1#L389) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/tools/compilar.ps1#L389)

```text
ANTES | DEPOIS |   CÓDIGO
  389 |    389 |       $exitCode = Invoke-BuildCommand -Command $CMakePath -Arguments @(
  390 |    390 |           '-S', $Root,
  391 |    391 |           '-B', $BuildDir,
  392 |        | -         '-A', 'x64'
      |    392 | +         '-A', 'x64',
      |    393 | +         '-DTURBORAMA_ENABLE_COMMERCIAL_SERVICES=OFF',
      |    394 | +         '-DTURBORAMA_RELEASE_HARDENING=ON'
  393 |    395 |       ) -StepId 3 -OnLine $onLine
  394 |    396 |   
  395 |    397 |       if ($exitCode -ne 0) {
```

Conferência: 1 trechos, 3 linhas adicionadas e 1 removidas.

