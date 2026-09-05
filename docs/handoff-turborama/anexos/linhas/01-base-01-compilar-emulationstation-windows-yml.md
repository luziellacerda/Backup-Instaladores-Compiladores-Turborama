# 01-base: .github/workflows/compilar-emulationstation-windows.yml

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Pipeline GitHub: filtros, dependências, construção, testes, pacote e publicação.

- Antes: `0e02780b761cb488c591416d2986130efcc166dd`.
- Depois: `76b214874973fe24017823401216896f3d7a6f40`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/.github/workflows/compilar-emulationstation-windows.yml).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 0, depois 1

Arquivo novo nesta comparação; não existe na revisão anterior. [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/76b214874973fe24017823401216896f3d7a6f40/.github/workflows/compilar-emulationstation-windows.yml#L1)

```text
ANTES | DEPOIS |   CÓDIGO
      |      1 | + name: PIX-SERVIDOR-CONTADOR - EmulationStation Windows x64
      |      2 | + 
      |      3 | + "on":
      |      4 | +   push:
      |      5 | +     branches:
      |      6 | +       - PIX-SERVIDOR-CONTADOR-20260904-1605
      |      7 | +     paths:
      |      8 | +       - .github/workflows/compilar-emulationstation-windows.yml
      |      9 | +       - TurboramaEmulationStation/**
      |     10 | +   workflow_dispatch:
      |     11 | + 
      |     12 | + permissions:
      |     13 | +   contents: write
      |     14 | + 
      |     15 | + concurrency:
      |     16 | +   group: emulationstation-windows-x64-${{ github.ref }}
      |     17 | +   cancel-in-progress: true
      |     18 | + 
      |     19 | + env:
      |     20 | +   PROJECT_DIR: TurboramaEmulationStation
      |     21 | +   WIN32_LIBS_COMMIT: 468eaba48c028921a4bf2abdfa3f3a00ce8d4c0d
      |     22 | +   RELEASE_TAG: build-PIX-SERVIDOR-CONTADOR-20260904-1605
      |     23 | +   PACKAGE_BASENAME: TurboramaEmulationStation-Windows-x64
      |     24 | +   _CL_: /Zm300
      |     25 | + 
      |     26 | + jobs:
      |     27 | +   build:
      |     28 | +     if: github.ref_name == 'PIX-SERVIDOR-CONTADOR-20260904-1605'
      |     29 | +     runs-on: windows-2022
      |     30 | +     timeout-minutes: 120
      |     31 | + 
      |     32 | +     defaults:
      |     33 | +       run:
      |     34 | +         shell: pwsh
      |     35 | + 
      |     36 | +     steps:
      |     37 | +       - name: Baixar somente o projeto EmulationStation
      |     38 | +         uses: actions/checkout@de0fac2e4500dabe0009e67214ff5f5447ce83dd # v6.0.2
      |     39 | +         with:
      |     40 | +           fetch-depth: 1
      |     41 | +           persist-credentials: false
      |     42 | +           sparse-checkout: TurboramaEmulationStation
      |     43 | + 
      |     44 | +       - name: Baixar dependencias Windows fixadas
      |     45 | +         uses: actions/checkout@de0fac2e4500dabe0009e67214ff5f5447ce83dd # v6.0.2
      |     46 | +         with:
      |     47 | +           repository: batocera-linux/batocera-emulationstation-win32-dependencies
      |     48 | +           ref: 468eaba48c028921a4bf2abdfa3f3a00ce8d4c0d
      |     49 | +           path: TurboramaEmulationStation/win32-libs
      |     50 | +           fetch-depth: 1
      |     51 | +           persist-credentials: false
      |     52 | + 
      |     53 | +       - name: Validar fontes e dependencias
      |     54 | +         run: |
      |     55 | +           $ErrorActionPreference = 'Stop'
      |     56 | +           $project = Join-Path $env:GITHUB_WORKSPACE $env:PROJECT_DIR
      |     57 | +           $deps = Join-Path $project 'win32-libs'
      |     58 | + 
      |     59 | +           foreach ($required in @(
      |     60 | +             (Join-Path $project 'CMakeLists.txt'),
      |     61 | +             (Join-Path $project 'embedded-theme\TURBORAMA\theme.xml'),
      |     62 | +             (Join-Path $deps 'SDL2\x64\SDL2.dll'),
      |     63 | +             (Join-Path $deps 'SDL2_mixer\x64\SDL2_mixer.dll'),
      |     64 | +             (Join-Path $deps 'FreeImage\x64\FreeImage.dll'),
      |     65 | +             (Join-Path $deps 'curl\x64\bin\libcurl.dll'),
      |     66 | +             (Join-Path $deps 'libvlc\x64\libvlc.dll'),
      |     67 | +             (Join-Path $deps 'libvlc\x64\libvlccore.dll'),
      |     68 | +             (Join-Path $deps 'libvlc\x64\plugins')
      |     69 | +           )) {
      |     70 | +             if (-not (Test-Path -LiteralPath $required)) {
      |     71 | +               throw "Arquivo obrigatorio ausente: $required"
      |     72 | +             }
      |     73 | +           }
      |     74 | + 
      |     75 | +           $actual = (& git -C $deps rev-parse HEAD).Trim()
      |     76 | +           if ($LASTEXITCODE -ne 0 -or $actual -ne $env:WIN32_LIBS_COMMIT) {
      |     77 | +             throw "Commit inesperado de win32-libs: $actual"
      |     78 | +           }
      |     79 | + 
      |     80 | +           "Fonte: $env:GITHUB_SHA"
      |     81 | +           "Dependencias: $actual"
      |     82 | +           cmake --version
      |     83 | + 
      |     84 | +       - name: Testar empacotador do tema
      |     85 | +         working-directory: TurboramaEmulationStation
      |     86 | +         run: |
      |     87 | +           $ErrorActionPreference = 'Stop'
      |     88 | +           & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" `
      |     89 | +             -NoLogo -NoProfile -ExecutionPolicy Bypass `
      |     90 | +             -File .\tools\tests\Test-EmbeddedThemeBuild.ps1
      |     91 | +           if ($LASTEXITCODE -ne 0) {
      |     92 | +             throw "Teste do tema falhou com codigo $LASTEXITCODE."
      |     93 | +           }
      |     94 | + 
      |     95 | +       - name: Configurar CMake para Visual Studio 2022 x64
      |     96 | +         working-directory: TurboramaEmulationStation
      |     97 | +         run: |
      |     98 | +           $ErrorActionPreference = 'Stop'
      |     99 | +           Remove-Item Env:CMAKE_TOOLCHAIN_FILE -ErrorAction SilentlyContinue
      |    100 | +           Remove-Item Env:VCPKG_ROOT -ErrorAction SilentlyContinue
      |    101 | +           Remove-Item Env:VCPKG_INSTALLATION_ROOT -ErrorAction SilentlyContinue
      |    102 | + 
      |    103 | +           cmake -S . -B build-github `
      |    104 | +             -G "Visual Studio 17 2022" `
      |    105 | +             -A x64 `
      |    106 | +             -T "v143,host=x64" `
      |    107 | +             -DRETROBAT=OFF `
      |    108 | +             -DBATOCERA=OFF `
      |    109 | +             -DCMAKE_FIND_PACKAGE_PREFER_CONFIG=FALSE `
      |    110 | +             -DCMAKE_SYSTEM_VERSION=10.0.26100.0
      |    111 | +           if ($LASTEXITCODE -ne 0) {
      |    112 | +             throw "Configuracao CMake falhou com codigo $LASTEXITCODE."
      |    113 | +           }
      |    114 | + 
      |    115 | +       - name: Compilar Release x64
      |    116 | +         working-directory: TurboramaEmulationStation
      |    117 | +         run: |
      |    118 | +           $ErrorActionPreference = 'Stop'
      |    119 | +           cmake --build build-github `
      |    120 | +             --config Release `
      |    121 | +             --target emulationstation `
      |    122 | +             --parallel 1
      |    123 | +           if ($LASTEXITCODE -ne 0) {
      |    124 | +             throw "Compilacao falhou com codigo $LASTEXITCODE."
      |    125 | +           }
      |    126 | + 
      |    127 | +           $exe = '.\bin\x64\Release\emulationstation.exe'
      |    128 | +           if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
      |    129 | +             throw "Executavel nao foi gerado em $exe."
      |    130 | +           }
      |    131 | + 
      |    132 | +       - name: Montar e validar pacote portatil
      |    133 | +         run: |
      |    134 | +           $ErrorActionPreference = 'Stop'
      |    135 | +           Set-StrictMode -Version Latest
      |    136 | + 
      |    137 | +           $project = Join-Path $env:GITHUB_WORKSPACE $env:PROJECT_DIR
      |    138 | +           $deps = Join-Path $project 'win32-libs'
      |    139 | +           $stage = Join-Path $env:RUNNER_TEMP $env:PACKAGE_BASENAME
      |    140 | +           $zip = Join-Path $env:RUNNER_TEMP ($env:PACKAGE_BASENAME + '.zip')
      |    141 | + 
      |    142 | +           if (Test-Path -LiteralPath $stage) {
      |    143 | +             Remove-Item -LiteralPath $stage -Recurse -Force
      |    144 | +           }
      |    145 | +           if (Test-Path -LiteralPath $zip) {
      |    146 | +             Remove-Item -LiteralPath $zip -Force
      |    147 | +           }
      |    148 | +           New-Item -ItemType Directory -Path $stage | Out-Null
      |    149 | + 
      |    150 | +           $runtime = [ordered]@{
      |    151 | +             (Join-Path $project 'bin\x64\Release\emulationstation.exe') = 'emulationstation.exe'
      |    152 | +             (Join-Path $deps 'FreeImage\x64\FreeImage.dll') = 'FreeImage.dll'
      |    153 | +             (Join-Path $deps 'curl\x64\bin\libcurl.dll') = 'libcurl.dll'
      |    154 | +             (Join-Path $deps 'libvlc\x64\libvlc.dll') = 'libvlc.dll'
      |    155 | +             (Join-Path $deps 'libvlc\x64\libvlccore.dll') = 'libvlccore.dll'
      |    156 | +             (Join-Path $deps 'SDL2\x64\SDL2.dll') = 'SDL2.dll'
      |    157 | +             (Join-Path $deps 'SDL2_mixer\x64\SDL2_mixer.dll') = 'SDL2_mixer.dll'
      |    158 | +             (Join-Path $deps 'SDL2_mixer\x64\optional\libmodplug-1.dll') = 'libmodplug-1.dll'
      |    159 | +             (Join-Path $deps 'SDL2_mixer\x64\optional\libogg-0.dll') = 'libogg-0.dll'
      |    160 | +             (Join-Path $deps 'SDL2_mixer\x64\optional\libopus-0.dll') = 'libopus-0.dll'
      |    161 | +             (Join-Path $deps 'SDL2_mixer\x64\optional\libopusfile-0.dll') = 'libopusfile-0.dll'
      |    162 | +           }
      |    163 | + 
      |    164 | +           foreach ($item in $runtime.GetEnumerator()) {
      |    165 | +             if (-not (Test-Path -LiteralPath $item.Key -PathType Leaf)) {
      |    166 | +               throw "Runtime obrigatorio ausente: $($item.Key)"
      |    167 | +             }
      |    168 | +             Copy-Item -LiteralPath $item.Key `
      |    169 | +               -Destination (Join-Path $stage $item.Value) -Force
      |    170 | +           }
      |    171 | + 
      |    172 | +           Copy-Item -LiteralPath (Join-Path $deps 'libvlc\x64\plugins') `
      |    173 | +             -Destination (Join-Path $stage 'plugins') -Recurse -Force
      |    174 | +           Copy-Item -LiteralPath (Join-Path $project 'resources') `
      |    175 | +             -Destination (Join-Path $stage 'resources') -Recurse -Force
      |    176 | +           Copy-Item -LiteralPath (Join-Path $project 'bin\x64\Release\screensaver_videos') `
      |    177 | +             -Destination (Join-Path $stage 'screensaver_videos') -Recurse -Force
      |    178 | + 
      |    179 | +           # Inclui os runtimes MSVC/OpenMP para o ZIP funcionar sem instalador.
      |    180 | +           $vswhere = Join-Path ${env:ProgramFiles(x86)} `
      |    181 | +             'Microsoft Visual Studio\Installer\vswhere.exe'
      |    182 | +           $vsRoot = (& $vswhere -latest -version '[17.0,18.0)' -products * `
      |    183 | +             -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
      |    184 | +             -property installationPath | Select-Object -First 1).Trim()
      |    185 | +           if (-not $vsRoot) {
      |    186 | +             throw 'Visual Studio 2022 com C++ nao foi localizado no runner.'
      |    187 | +           }
      |    188 | + 
      |    189 | +           $redistRoot = Join-Path $vsRoot 'VC\Redist\MSVC'
      |    190 | +           $redistVersion = Get-ChildItem -LiteralPath $redistRoot -Directory |
      |    191 | +             Where-Object { $_.Name -match '^\d+(\.\d+){2,3}$' } |
      |    192 | +             Sort-Object { [version]$_.Name } -Descending |
      |    193 | +             Select-Object -First 1
      |    194 | +           if (-not $redistVersion) {
      |    195 | +             throw 'Runtime redistribuivel do MSVC nao foi localizado.'
      |    196 | +           }
      |    197 | + 
      |    198 | +           $crt = Join-Path $redistVersion.FullName 'x64\Microsoft.VC143.CRT'
      |    199 | +           $crtFiles = @(Get-ChildItem -LiteralPath $crt -File -Filter '*.dll')
      |    200 | +           if ($crtFiles.Count -lt 3) {
      |    201 | +             throw "Conjunto CRT incompleto em $crt."
      |    202 | +           }
      |    203 | +           $crtFiles | Copy-Item -Destination $stage -Force
      |    204 | + 
      |    205 | +           $openMp = Join-Path $redistVersion.FullName `
      |    206 | +             'x64\Microsoft.VC143.OpenMP\vcomp140.dll'
      |    207 | +           if (-not (Test-Path -LiteralPath $openMp -PathType Leaf)) {
      |    208 | +             throw "Runtime OpenMP ausente: $openMp"
      |    209 | +           }
      |    210 | +           Copy-Item -LiteralPath $openMp -Destination $stage -Force
      |    211 | + 
      |    212 | +           function Assert-X64PortableExecutable([string]$path) {
      |    213 | +             $stream = [IO.File]::OpenRead($path)
      |    214 | +             $reader = [IO.BinaryReader]::new($stream)
      |    215 | +             try {
      |    216 | +               if ($stream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5A4D) {
      |    217 | +                 throw "Cabecalho PE invalido: $path"
      |    218 | +               }
      |    219 | +               $stream.Position = 0x3C
      |    220 | +               $peOffset = $reader.ReadInt32()
      |    221 | +               if ($peOffset -lt 0 -or $peOffset -gt ($stream.Length - 6)) {
      |    222 | +                 throw "Offset PE invalido: $path"
      |    223 | +               }
      |    224 | +               $stream.Position = $peOffset
      |    225 | +               if ($reader.ReadUInt32() -ne 0x00004550) {
      |    226 | +                 throw "Assinatura PE invalida: $path"
      |    227 | +               }
      |    228 | +               if ($reader.ReadUInt16() -ne 0x8664) {
      |    229 | +                 throw "Binario nao e x64: $path"
      |    230 | +               }
      |    231 | +             }
      |    232 | +             finally {
      |    233 | +               $reader.Dispose()
      |    234 | +               $stream.Dispose()
      |    235 | +             }
      |    236 | +           }
      |    237 | + 
      |    238 | +           $portableBinaries = @(Get-ChildItem -LiteralPath $stage -Recurse -File |
      |    239 | +             Where-Object { $_.Extension -in '.exe', '.dll' })
      |    240 | +           foreach ($binary in $portableBinaries) {
      |    241 | +             Assert-X64PortableExecutable $binary.FullName
      |    242 | +           }
      |    243 | + 
      |    244 | +           $pluginCount = @(Get-ChildItem -LiteralPath (Join-Path $stage 'plugins') `
      |    245 | +             -Recurse -File -Filter '*.dll').Count
      |    246 | +           if ($pluginCount -lt 100) {
      |    247 | +             throw "Pacote VLC incompleto: somente $pluginCount plugins."
      |    248 | +           }
      |    249 | + 
      |    250 | +           @(
      |    251 | +             'TurboRama EmulationStation - Windows x64 Release'
      |    252 | +             "Branch: $env:GITHUB_REF_NAME"
      |    253 | +             "Commit da fonte: $env:GITHUB_SHA"
      |    254 | +             "Commit de win32-libs: $env:WIN32_LIBS_COMMIT"
      |    255 | +             "Runner: $env:ImageOS $env:ImageVersion"
      |    256 | +             "Gerado em UTC: $([DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm:ss'))"
      |    257 | +             'Build sem assinatura digital.'
      |    258 | +           ) | Set-Content -LiteralPath (Join-Path $stage 'BUILD-INFO.txt') `
      |    259 | +             -Encoding utf8NoBOM
      |    260 | + 
      |    261 | +           @'
      |    262 | +           @echo off
      |    263 | +           setlocal
      |    264 | +           cd /d "%~dp0"
      |    265 | +           if not exist "%~dp0_perfil_teste" mkdir "%~dp0_perfil_teste"
      |    266 | +           "%~dp0emulationstation.exe" --home "%~dp0_perfil_teste" --windowed --resolution 1280 720 --debug
      |    267 | +           set "exit_code=%ERRORLEVEL%"
      |    268 | +           echo.
      |    269 | +           echo TurboRama terminou com codigo %exit_code%.
      |    270 | +           pause
      |    271 | +           exit /b %exit_code%
      |    272 | +           '@ | Set-Content -LiteralPath (Join-Path $stage 'TESTAR-ISOLADO.cmd') `
      |    273 | +             -Encoding ascii
      |    274 | + 
      |    275 | +           function Invoke-SmokeTest([string]$argument) {
      |    276 | +             $process = Start-Process `
      |    277 | +               -FilePath (Join-Path $stage 'emulationstation.exe') `
      |    278 | +               -WorkingDirectory $stage `
      |    279 | +               -ArgumentList $argument `
      |    280 | +               -PassThru
      |    281 | +             if (-not $process.WaitForExit(30000)) {
      |    282 | +               Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
      |    283 | +               throw "Teste $argument excedeu 30 segundos."
      |    284 | +             }
      |    285 | +             if ($process.ExitCode -ne 0) {
      |    286 | +               throw "Teste $argument falhou com codigo $($process.ExitCode)."
      |    287 | +             }
      |    288 | +           }
      |    289 | + 
      |    290 | +           Invoke-SmokeTest '--help'
      |    291 | +           Invoke-SmokeTest '--protected-decorations-self-test'
      |    292 | + 
      |    293 | +           $manifest = Join-Path $stage 'SHA256SUMS.txt'
      |    294 | +           Get-ChildItem -LiteralPath $stage -Recurse -File |
      |    295 | +             Sort-Object FullName |
      |    296 | +             ForEach-Object {
      |    297 | +               $relative = [IO.Path]::GetRelativePath($stage, $_.FullName).Replace('\', '/')
      |    298 | +               $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
      |    299 | +               "$hash *$relative"
      |    300 | +             } | Set-Content -LiteralPath $manifest -Encoding ascii
      |    301 | + 
      |    302 | +           Compress-Archive -LiteralPath $stage -DestinationPath $zip `
      |    303 | +             -CompressionLevel NoCompression
      |    304 | +           if (-not (Test-Path -LiteralPath $zip -PathType Leaf)) {
      |    305 | +             throw 'ZIP final nao foi criado.'
      |    306 | +           }
      |    307 | + 
      |    308 | +           $zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
      |    309 | +           $zipHashFile = $zip + '.sha256'
      |    310 | +           "$zipHash *$([IO.Path]::GetFileName($zip))" |
      |    311 | +             Set-Content -LiteralPath $zipHashFile -Encoding ascii
      |    312 | + 
      |    313 | +           "ZIP_PATH=$zip" | Add-Content -LiteralPath $env:GITHUB_ENV -Encoding utf8
      |    314 | +           "ZIP_HASH_PATH=$zipHashFile" | Add-Content -LiteralPath $env:GITHUB_ENV -Encoding utf8
      |    315 | +           "ZIP_SHA256=$zipHash" | Add-Content -LiteralPath $env:GITHUB_ENV -Encoding utf8
      |    316 | +           "PACKAGE_FILE_COUNT=$(@(Get-ChildItem -LiteralPath $stage -Recurse -File).Count)" |
      |    317 | +             Add-Content -LiteralPath $env:GITHUB_ENV -Encoding utf8
      |    318 | + 
      |    319 | +           "Pacote: $zip"
      |    320 | +           "SHA-256: $zipHash"
      |    321 | +           "Binarios x64 verificados: $($portableBinaries.Count)"
      |    322 | +           "Plugins VLC: $pluginCount"
      |    323 | + 
      |    324 | +       - name: Publicar ZIP em um release separado
      |    325 | +         env:
      |    326 | +           GH_TOKEN: ${{ github.token }}
      |    327 | +         run: |
      |    328 | +           $ErrorActionPreference = 'Stop'
      |    329 | +           $notes = Join-Path $env:RUNNER_TEMP 'release-notes.md'
      |    330 | +           @"
      |    331 | +           Compilacao automatica Release x64 da branch $env:GITHUB_REF_NAME.
      |    332 | + 
      |    333 | +           - Commit da fonte: $env:GITHUB_SHA
      |    334 | +           - Commit de win32-libs: $env:WIN32_LIBS_COMMIT
      |    335 | +           - Arquivos no pacote: $env:PACKAGE_FILE_COUNT
      |    336 | +           - SHA-256 do ZIP: $env:ZIP_SHA256
      |    337 | +           - Runner: Windows Server 2022 / Visual Studio 2022
      |    338 | + 
      |    339 | +           O executavel nao possui assinatura digital. Use `TESTAR-ISOLADO.cmd`
      |    340 | +           para testar sem alterar o perfil normal do EmulationStation.
      |    341 | +           "@ | Set-Content -LiteralPath $notes -Encoding utf8NoBOM
      |    342 | + 
      |    343 | +           & gh release view $env:RELEASE_TAG `
      |    344 | +             --repo $env:GITHUB_REPOSITORY *> $null
      |    345 | +           $releaseExists = $LASTEXITCODE -eq 0
      |    346 | + 
      |    347 | +           if ($releaseExists) {
      |    348 | +             & gh release upload $env:RELEASE_TAG `
      |    349 | +               $env:ZIP_PATH $env:ZIP_HASH_PATH `
      |    350 | +               --repo $env:GITHUB_REPOSITORY --clobber
      |    351 | +             if ($LASTEXITCODE -ne 0) {
      |    352 | +               throw 'Falha ao atualizar os arquivos do release.'
      |    353 | +             }
      |    354 | + 
      |    355 | +             & gh api --method PATCH `
      |    356 | +               "repos/$env:GITHUB_REPOSITORY/git/refs/tags/$env:RELEASE_TAG" `
      |    357 | +               -f "sha=$env:GITHUB_SHA" -F force=true *> $null
      |    358 | +             if ($LASTEXITCODE -ne 0) {
      |    359 | +               throw 'Falha ao atualizar o commit da tag do release.'
      |    360 | +             }
      |    361 | + 
      |    362 | +             & gh release edit $env:RELEASE_TAG `
      |    363 | +               --repo $env:GITHUB_REPOSITORY `
      |    364 | +               --title "PIX-SERVIDOR-CONTADOR - Windows x64" `
      |    365 | +               --notes-file $notes --prerelease
      |    366 | +           }
      |    367 | +           else {
      |    368 | +             & gh release create $env:RELEASE_TAG `
      |    369 | +               $env:ZIP_PATH $env:ZIP_HASH_PATH `
      |    370 | +               --repo $env:GITHUB_REPOSITORY `
      |    371 | +               --target $env:GITHUB_SHA `
      |    372 | +               --title "PIX-SERVIDOR-CONTADOR - Windows x64" `
      |    373 | +               --notes-file $notes --prerelease
      |    374 | +           }
      |    375 | +           if ($LASTEXITCODE -ne 0) {
      |    376 | +             throw 'Falha ao publicar o release.'
      |    377 | +           }
      |    378 | + 
      |    379 | +           $releaseUrl = (& gh release view $env:RELEASE_TAG `
      |    380 | +             --repo $env:GITHUB_REPOSITORY --json url --jq '.url').Trim()
      |    381 | +           @(
      |    382 | +             '## Compilacao concluida'
      |    383 | +             ''
      |    384 | +             "- Release: [$env:RELEASE_TAG]($releaseUrl)"
      |    385 | +             "- SHA-256: ``$env:ZIP_SHA256``"
      |    386 | +             "- Arquivos no pacote: $env:PACKAGE_FILE_COUNT"
      |    387 | +           ) | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8
```

Conferência: 1 trechos, 387 linhas adicionadas e 0 removidas.
