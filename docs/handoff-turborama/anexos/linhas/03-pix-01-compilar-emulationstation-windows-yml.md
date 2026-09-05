# 03-pix: .github/workflows/compilar-emulationstation-windows.yml

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Pipeline GitHub: filtros, dependências, construção, testes, pacote e publicação.

- Antes: `76b214874973fe24017823401216896f3d7a6f40`.
- Depois: `476e06179f89ac209ff808dffb27555d740f93d2`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/.github/workflows/compilar-emulationstation-windows.yml).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 106, depois 106

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/.github/workflows/compilar-emulationstation-windows.yml#L106) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/.github/workflows/compilar-emulationstation-windows.yml#L106)

```text
ANTES | DEPOIS |   CÓDIGO
  106 |    106 |               -T "v143,host=x64" `
  107 |    107 |               -DRETROBAT=OFF `
  108 |    108 |               -DBATOCERA=OFF `
      |    109 | +             -DTURBORAMA_RELEASE_HARDENING=ON `
  109 |    110 |               -DCMAKE_FIND_PACKAGE_PREFER_CONFIG=FALSE `
  110 |    111 |               -DCMAKE_SYSTEM_VERSION=10.0.26100.0
  111 |    112 |             if ($LASTEXITCODE -ne 0) {
  112 |    113 |               throw "Configuracao CMake falhou com codigo $LASTEXITCODE."
  113 |    114 |             }
  114 |    115 |   
      |    116 | +       - name: Validar audio e compatibilidade do ecossistema PIX
      |    117 | +         working-directory: TurboramaEmulationStation
      |    118 | +         run: |
      |    119 | +           $ErrorActionPreference = 'Stop'
      |    120 | +           foreach ($test in @(
      |    121 | +             'Test-AudioHandoff.ps1',
      |    122 | +             'Test-RetroArchAudioRepair.ps1',
      |    123 | +             'Test-LaunchCreditCompatibility.ps1',
      |    124 | +             'Test-CreditManagerFailClosed.ps1'
      |    125 | +           )) {
      |    126 | +             & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" `
      |    127 | +               -NoProfile -ExecutionPolicy Bypass -File ".\tools\tests\$test"
      |    128 | +             if ($LASTEXITCODE -ne 0) { throw "Teste falhou: $test" }
      |    129 | +           }
      |    130 | + 
  115 |    131 |         - name: Compilar Release x64
  116 |    132 |           working-directory: TurboramaEmulationStation
  117 |    133 |           run: |
```

## Trecho 2: antes 173, depois 189

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/.github/workflows/compilar-emulationstation-windows.yml#L173) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/.github/workflows/compilar-emulationstation-windows.yml#L189)

```text
ANTES | DEPOIS |   CÓDIGO
  173 |    189 |               -Destination (Join-Path $stage 'plugins') -Recurse -Force
  174 |    190 |             Copy-Item -LiteralPath (Join-Path $project 'resources') `
  175 |    191 |               -Destination (Join-Path $stage 'resources') -Recurse -Force
      |    192 | +           Copy-Item -LiteralPath (Join-Path $project 'tools\Repair-RetroArchAudio.ps1') -Destination $stage
      |    193 | +           Copy-Item -LiteralPath (Join-Path $project 'tools\AUDIO-LEIA-ME.txt') -Destination $stage
  176 |    194 |             Copy-Item -LiteralPath (Join-Path $project 'bin\x64\Release\screensaver_videos') `
  177 |    195 |               -Destination (Join-Path $stage 'screensaver_videos') -Recurse -Force
  178 |    196 |   
```

## Trecho 3: antes 272, depois 290

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/.github/workflows/compilar-emulationstation-windows.yml#L272) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/.github/workflows/compilar-emulationstation-windows.yml#L290)

```text
ANTES | DEPOIS |   CÓDIGO
  272 |    290 |             '@ | Set-Content -LiteralPath (Join-Path $stage 'TESTAR-ISOLADO.cmd') `
  273 |    291 |               -Encoding ascii
  274 |    292 |   
  275 |        | -           function Invoke-SmokeTest([string]$argument) {
      |    293 | +           function Invoke-SmokeTest([string]$argument, [int]$expectedExitCode = 0) {
  276 |    294 |               $process = Start-Process `
  277 |    295 |                 -FilePath (Join-Path $stage 'emulationstation.exe') `
  278 |    296 |                 -WorkingDirectory $stage `
  279 |    297 |                 -ArgumentList $argument `
      |    298 | +               -WindowStyle Hidden `
  280 |    299 |                 -PassThru
  281 |    300 |               if (-not $process.WaitForExit(30000)) {
  282 |    301 |                 Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
  283 |    302 |                 throw "Teste $argument excedeu 30 segundos."
  284 |    303 |               }
  285 |        | -             if ($process.ExitCode -ne 0) {
      |    304 | +             if ($process.ExitCode -ne $expectedExitCode) {
  286 |    305 |                 throw "Teste $argument falhou com codigo $($process.ExitCode)."
  287 |    306 |               }
  288 |    307 |             }
  289 |    308 |   
  290 |    309 |             Invoke-SmokeTest '--help'
  291 |    310 |             Invoke-SmokeTest '--protected-decorations-self-test'
      |    311 | +           Invoke-SmokeTest '--credit-warning-overlay-self-test'
      |    312 | +           Invoke-SmokeTest '--pix-agent-manager-self-test'
      |    313 | +           # The frontend package does not install the server/agent. Verify that
      |    314 | +           # the existing trust check rejects this absent agent (exit 32).
      |    315 | +           Invoke-SmokeTest '--pix-agent-trust-self-test' 32
      |    316 | + 
      |    317 | +           # Publish this exact tested executable separately for existing PIX
      |    318 | +           # installations; its DLLs/resources still come from the full package.
      |    319 | +           $standaloneExe = Join-Path $stage 'emulationstation.exe'
      |    320 | +           $exeHash = (Get-FileHash -LiteralPath $standaloneExe -Algorithm SHA256).Hash.ToLowerInvariant()
      |    321 | +           $exeHashFile = Join-Path $env:RUNNER_TEMP 'emulationstation.exe.sha256'
      |    322 | +           "$exeHash *emulationstation.exe" |
      |    323 | +             Set-Content -LiteralPath $exeHashFile -Encoding ascii
      |    324 | +           "EXE_PATH=$standaloneExe" | Add-Content -LiteralPath $env:GITHUB_ENV -Encoding utf8
      |    325 | +           "EXE_HASH_PATH=$exeHashFile" | Add-Content -LiteralPath $env:GITHUB_ENV -Encoding utf8
      |    326 | +           "EXE_SHA256=$exeHash" | Add-Content -LiteralPath $env:GITHUB_ENV -Encoding utf8
  292 |    327 |   
  293 |    328 |             $manifest = Join-Path $stage 'SHA256SUMS.txt'
  294 |    329 |             Get-ChildItem -LiteralPath $stage -Recurse -File |
```

## Trecho 4: antes 321, depois 356

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/.github/workflows/compilar-emulationstation-windows.yml#L321) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/.github/workflows/compilar-emulationstation-windows.yml#L356)

```text
ANTES | DEPOIS |   CÓDIGO
  321 |    356 |             "Binarios x64 verificados: $($portableBinaries.Count)"
  322 |    357 |             "Plugins VLC: $pluginCount"
  323 |    358 |   
  324 |        | -       - name: Publicar ZIP em um release separado
      |    359 | +       - name: Publicar EXE e ZIP no release PIX separado
  325 |    360 |           env:
  326 |    361 |             GH_TOKEN: ${{ github.token }}
  327 |    362 |           run: |
```

## Trecho 5: antes 334, depois 369

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/.github/workflows/compilar-emulationstation-windows.yml#L334) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/.github/workflows/compilar-emulationstation-windows.yml#L369)

```text
ANTES | DEPOIS |   CÓDIGO
  334 |    369 |             - Commit de win32-libs: $env:WIN32_LIBS_COMMIT
  335 |    370 |             - Arquivos no pacote: $env:PACKAGE_FILE_COUNT
  336 |    371 |             - SHA-256 do ZIP: $env:ZIP_SHA256
      |    372 | +           - SHA-256 do EXE avulso: $env:EXE_SHA256
  337 |    373 |             - Runner: Windows Server 2022 / Visual Studio 2022
      |    374 | +           - Audio: espera limitada pela liberacao VLC antes do jogo
      |    375 | +           - RetroArch: reparador com backup incluido; veja AUDIO-LEIA-ME.txt
      |    376 | +           - Preservados: servidor PIX, saldo, cronometro, supervisao e caches
  338 |    377 |   
  339 |    378 |             O executavel nao possui assinatura digital. Use `TESTAR-ISOLADO.cmd`
  340 |    379 |             para testar sem alterar o perfil normal do EmulationStation.
      |    380 | + 
      |    381 | +           Para atualizar uma instalacao PIX existente, baixe somente
      |    382 | +           emulationstation.exe. Feche o programa, guarde uma copia do EXE antigo
      |    383 | +           e substitua-o na mesma pasta, mantendo as DLLs, plugins, recursos e
      |    384 | +           configuracoes. O EXE avulso nao e um instalador nem um pacote autonomo.
  341 |    385 |             "@ | Set-Content -LiteralPath $notes -Encoding utf8NoBOM
  342 |    386 |   
  343 |    387 |             & gh release view $env:RELEASE_TAG `
```

## Trecho 6: antes 346, depois 390

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/.github/workflows/compilar-emulationstation-windows.yml#L346) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/.github/workflows/compilar-emulationstation-windows.yml#L390)

```text
ANTES | DEPOIS |   CÓDIGO
  346 |    390 |   
  347 |    391 |             if ($releaseExists) {
  348 |    392 |               & gh release upload $env:RELEASE_TAG `
  349 |        | -               $env:ZIP_PATH $env:ZIP_HASH_PATH `
      |    393 | +               $env:ZIP_PATH $env:ZIP_HASH_PATH $env:EXE_PATH $env:EXE_HASH_PATH `
  350 |    394 |                 --repo $env:GITHUB_REPOSITORY --clobber
  351 |    395 |               if ($LASTEXITCODE -ne 0) {
  352 |    396 |                 throw 'Falha ao atualizar os arquivos do release.'
```

## Trecho 7: antes 362, depois 406

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/.github/workflows/compilar-emulationstation-windows.yml#L362) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/.github/workflows/compilar-emulationstation-windows.yml#L406)

```text
ANTES | DEPOIS |   CÓDIGO
  362 |    406 |               & gh release edit $env:RELEASE_TAG `
  363 |    407 |                 --repo $env:GITHUB_REPOSITORY `
  364 |    408 |                 --title "PIX-SERVIDOR-CONTADOR - Windows x64" `
  365 |        | -               --notes-file $notes --prerelease
      |    409 | +               --notes-file $notes --prerelease=false
  366 |    410 |             }
  367 |    411 |             else {
  368 |    412 |               & gh release create $env:RELEASE_TAG `
  369 |        | -               $env:ZIP_PATH $env:ZIP_HASH_PATH `
      |    413 | +               $env:ZIP_PATH $env:ZIP_HASH_PATH $env:EXE_PATH $env:EXE_HASH_PATH `
  370 |    414 |                 --repo $env:GITHUB_REPOSITORY `
  371 |    415 |                 --target $env:GITHUB_SHA `
  372 |    416 |                 --title "PIX-SERVIDOR-CONTADOR - Windows x64" `
  373 |        | -               --notes-file $notes --prerelease
      |    417 | +               --notes-file $notes
  374 |    418 |             }
  375 |    419 |             if ($LASTEXITCODE -ne 0) {
  376 |    420 |               throw 'Falha ao publicar o release.'
```

## Trecho 8: antes 383, depois 427

[Ver antes](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/.github/workflows/compilar-emulationstation-windows.yml#L383) · [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/476e06179f89ac209ff808dffb27555d740f93d2/.github/workflows/compilar-emulationstation-windows.yml#L427)

```text
ANTES | DEPOIS |   CÓDIGO
  383 |    427 |               ''
  384 |    428 |               "- Release: [$env:RELEASE_TAG]($releaseUrl)"
  385 |    429 |               "- SHA-256: ``$env:ZIP_SHA256``"
      |    430 | +             "- EXE avulso SHA-256: ``$env:EXE_SHA256``"
  386 |    431 |               "- Arquivos no pacote: $env:PACKAGE_FILE_COUNT"
  387 |    432 |             ) | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8
```

Conferência: 8 trechos, 52 linhas adicionadas e 7 removidas.

