# 02-cliente: .github/workflows/compilar-cliente-sem-servicos-windows.yml

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Pipeline GitHub: filtros, dependências, construção, testes, pacote e publicação.

- Antes: `76b214874973fe24017823401216896f3d7a6f40`.
- Depois: `947dad45f5e9cd556cce6f15045a5dd6119bdf95`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/.github/workflows/compilar-cliente-sem-servicos-windows.yml).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 0, depois 1

Arquivo novo nesta comparação; não existe na revisão anterior. [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/.github/workflows/compilar-cliente-sem-servicos-windows.yml#L1)

```text
ANTES | DEPOIS |   CÓDIGO
      |      1 | + name: CLIENTE SEM SERVICOS - EmulationStation Windows x64
      |      2 | + 
      |      3 | + "on":
      |      4 | +   push:
      |      5 | +     branches:
      |      6 | +       - CLIENTE-SEM-SERVICOS-20260904-1818
      |      7 | +     paths:
      |      8 | +       - .github/workflows/compilar-cliente-sem-servicos-windows.yml
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
      |     22 | +   RELEASE_TAG: build-CLIENTE-SEM-SERVICOS-20260904-1818
      |     23 | +   PACKAGE_BASENAME: TurboramaEmulationStation-Cliente-Sem-Servicos-Windows-x64
      |     24 | +   _CL_: /Zm300
      |     25 | + 
      |     26 | + jobs:
      |     27 | +   build:
      |     28 | +     if: github.ref_name == 'CLIENTE-SEM-SERVICOS-20260904-1818'
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
      |    109 | +             -DTURBORAMA_ENABLE_COMMERCIAL_SERVICES=OFF `
      |    110 | +             -DTURBORAMA_RELEASE_HARDENING=ON `
      |    111 | +             -DCMAKE_FIND_PACKAGE_PREFER_CONFIG=FALSE `
      |    112 | +             -DCMAKE_SYSTEM_VERSION=10.0.26100.0
      |    113 | +           if ($LASTEXITCODE -ne 0) {
      |    114 | +             throw "Configuracao CMake falhou com codigo $LASTEXITCODE."
      |    115 | +           }
      |    116 | + 
      |    117 | +       - name: Compilar Release x64
      |    118 | +         working-directory: TurboramaEmulationStation
      |    119 | +         run: |
      |    120 | +           $ErrorActionPreference = 'Stop'
      |    121 | +           cmake --build build-github `
      |    122 | +             --config Release `
      |    123 | +             --target emulationstation `
      |    124 | +             --parallel 1
      |    125 | +           if ($LASTEXITCODE -ne 0) {
      |    126 | +             throw "Compilacao falhou com codigo $LASTEXITCODE."
      |    127 | +           }
      |    128 | + 
      |    129 | +           $exe = '.\bin\x64\Release\emulationstation.exe'
      |    130 | +           if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
      |    131 | +             throw "Executavel nao foi gerado em $exe."
      |    132 | +           }
      |    133 | + 
      |    134 | +       - name: Confirmar servicos removidos e otimizacoes preservadas
      |    135 | +         working-directory: TurboramaEmulationStation
      |    136 | +         run: |
      |    137 | +           $ErrorActionPreference = 'Stop'
      |    138 | +           & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" `
      |    139 | +             -NoLogo -NoProfile -ExecutionPolicy Bypass `
      |    140 | +             -File .\tools\tests\Test-NoCommercialServicesBuild.ps1 `
      |    141 | +             -BuildDirectory .\build-github `
      |    142 | +             -Executable .\bin\x64\Release\emulationstation.exe
      |    143 | +           if ($LASTEXITCODE -ne 0) {
      |    144 | +             throw "Validacao do perfil sem servicos falhou com codigo $LASTEXITCODE."
      |    145 | +           }
      |    146 | + 
      |    147 | +       - name: Montar e validar pacote portatil
      |    148 | +         run: |
      |    149 | +           $ErrorActionPreference = 'Stop'
      |    150 | +           Set-StrictMode -Version Latest
      |    151 | + 
      |    152 | +           $project = Join-Path $env:GITHUB_WORKSPACE $env:PROJECT_DIR
      |    153 | +           $deps = Join-Path $project 'win32-libs'
      |    154 | +           $stage = Join-Path $env:RUNNER_TEMP $env:PACKAGE_BASENAME
      |    155 | +           $zip = Join-Path $env:RUNNER_TEMP ($env:PACKAGE_BASENAME + '.zip')
      |    156 | + 
      |    157 | +           if (Test-Path -LiteralPath $stage) {
      |    158 | +             Remove-Item -LiteralPath $stage -Recurse -Force
      |    159 | +           }
      |    160 | +           if (Test-Path -LiteralPath $zip) {
      |    161 | +             Remove-Item -LiteralPath $zip -Force
      |    162 | +           }
      |    163 | +           New-Item -ItemType Directory -Path $stage | Out-Null
      |    164 | + 
      |    165 | +           $runtime = [ordered]@{
      |    166 | +             (Join-Path $project 'bin\x64\Release\emulationstation.exe') = 'emulationstation.exe'
      |    167 | +             (Join-Path $deps 'FreeImage\x64\FreeImage.dll') = 'FreeImage.dll'
      |    168 | +             (Join-Path $deps 'curl\x64\bin\libcurl.dll') = 'libcurl.dll'
      |    169 | +             (Join-Path $deps 'libvlc\x64\libvlc.dll') = 'libvlc.dll'
      |    170 | +             (Join-Path $deps 'libvlc\x64\libvlccore.dll') = 'libvlccore.dll'
      |    171 | +             (Join-Path $deps 'SDL2\x64\SDL2.dll') = 'SDL2.dll'
      |    172 | +             (Join-Path $deps 'SDL2_mixer\x64\SDL2_mixer.dll') = 'SDL2_mixer.dll'
      |    173 | +             (Join-Path $deps 'SDL2_mixer\x64\optional\libmodplug-1.dll') = 'libmodplug-1.dll'
      |    174 | +             (Join-Path $deps 'SDL2_mixer\x64\optional\libogg-0.dll') = 'libogg-0.dll'
      |    175 | +             (Join-Path $deps 'SDL2_mixer\x64\optional\libopus-0.dll') = 'libopus-0.dll'
      |    176 | +             (Join-Path $deps 'SDL2_mixer\x64\optional\libopusfile-0.dll') = 'libopusfile-0.dll'
      |    177 | +           }
      |    178 | + 
      |    179 | +           foreach ($item in $runtime.GetEnumerator()) {
      |    180 | +             if (-not (Test-Path -LiteralPath $item.Key -PathType Leaf)) {
      |    181 | +               throw "Runtime obrigatorio ausente: $($item.Key)"
      |    182 | +             }
      |    183 | +             Copy-Item -LiteralPath $item.Key `
      |    184 | +               -Destination (Join-Path $stage $item.Value) -Force
      |    185 | +           }
      |    186 | + 
      |    187 | +           Copy-Item -LiteralPath (Join-Path $deps 'libvlc\x64\plugins') `
      |    188 | +             -Destination (Join-Path $stage 'plugins') -Recurse -Force
      |    189 | +           Copy-Item -LiteralPath (Join-Path $project 'resources') `
      |    190 | +             -Destination (Join-Path $stage 'resources') -Recurse -Force
      |    191 | +           Copy-Item -LiteralPath (Join-Path $project 'bin\x64\Release\screensaver_videos') `
      |    192 | +             -Destination (Join-Path $stage 'screensaver_videos') -Recurse -Force
      |    193 | + 
      |    194 | +           # Inclui os runtimes MSVC/OpenMP para o ZIP funcionar sem instalador.
      |    195 | +           $vswhere = Join-Path ${env:ProgramFiles(x86)} `
      |    196 | +             'Microsoft Visual Studio\Installer\vswhere.exe'
      |    197 | +           $vsRoot = (& $vswhere -latest -version '[17.0,18.0)' -products * `
      |    198 | +             -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
      |    199 | +             -property installationPath | Select-Object -First 1).Trim()
      |    200 | +           if (-not $vsRoot) {
      |    201 | +             throw 'Visual Studio 2022 com C++ nao foi localizado no runner.'
      |    202 | +           }
      |    203 | + 
      |    204 | +           $redistRoot = Join-Path $vsRoot 'VC\Redist\MSVC'
      |    205 | +           $redistVersion = Get-ChildItem -LiteralPath $redistRoot -Directory |
      |    206 | +             Where-Object { $_.Name -match '^\d+(\.\d+){2,3}$' } |
      |    207 | +             Sort-Object { [version]$_.Name } -Descending |
      |    208 | +             Select-Object -First 1
      |    209 | +           if (-not $redistVersion) {
      |    210 | +             throw 'Runtime redistribuivel do MSVC nao foi localizado.'
      |    211 | +           }
      |    212 | + 
      |    213 | +           $crt = Join-Path $redistVersion.FullName 'x64\Microsoft.VC143.CRT'
      |    214 | +           $crtFiles = @(Get-ChildItem -LiteralPath $crt -File -Filter '*.dll')
      |    215 | +           if ($crtFiles.Count -lt 3) {
      |    216 | +             throw "Conjunto CRT incompleto em $crt."
      |    217 | +           }
      |    218 | +           $crtFiles | Copy-Item -Destination $stage -Force
      |    219 | + 
      |    220 | +           $openMp = Join-Path $redistVersion.FullName `
      |    221 | +             'x64\Microsoft.VC143.OpenMP\vcomp140.dll'
      |    222 | +           if (-not (Test-Path -LiteralPath $openMp -PathType Leaf)) {
      |    223 | +             throw "Runtime OpenMP ausente: $openMp"
      |    224 | +           }
      |    225 | +           Copy-Item -LiteralPath $openMp -Destination $stage -Force
      |    226 | + 
      |    227 | +           function Assert-X64PortableExecutable([string]$path) {
      |    228 | +             $stream = [IO.File]::OpenRead($path)
      |    229 | +             $reader = [IO.BinaryReader]::new($stream)
      |    230 | +             try {
      |    231 | +               if ($stream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5A4D) {
      |    232 | +                 throw "Cabecalho PE invalido: $path"
      |    233 | +               }
      |    234 | +               $stream.Position = 0x3C
      |    235 | +               $peOffset = $reader.ReadInt32()
      |    236 | +               if ($peOffset -lt 0 -or $peOffset -gt ($stream.Length - 6)) {
      |    237 | +                 throw "Offset PE invalido: $path"
      |    238 | +               }
      |    239 | +               $stream.Position = $peOffset
      |    240 | +               if ($reader.ReadUInt32() -ne 0x00004550) {
      |    241 | +                 throw "Assinatura PE invalida: $path"
      |    242 | +               }
      |    243 | +               if ($reader.ReadUInt16() -ne 0x8664) {
      |    244 | +                 throw "Binario nao e x64: $path"
      |    245 | +               }
      |    246 | +             }
      |    247 | +             finally {
      |    248 | +               $reader.Dispose()
      |    249 | +               $stream.Dispose()
      |    250 | +             }
      |    251 | +           }
      |    252 | + 
      |    253 | +           $portableBinaries = @(Get-ChildItem -LiteralPath $stage -Recurse -File |
      |    254 | +             Where-Object { $_.Extension -in '.exe', '.dll' })
      |    255 | +           foreach ($binary in $portableBinaries) {
      |    256 | +             Assert-X64PortableExecutable $binary.FullName
      |    257 | +           }
      |    258 | + 
      |    259 | +           $pluginCount = @(Get-ChildItem -LiteralPath (Join-Path $stage 'plugins') `
      |    260 | +             -Recurse -File -Filter '*.dll').Count
      |    261 | +           if ($pluginCount -lt 100) {
      |    262 | +             throw "Pacote VLC incompleto: somente $pluginCount plugins."
      |    263 | +           }
      |    264 | + 
      |    265 | +           @(
      |    266 | +             'TurboRama EmulationStation - Cliente sem servicos - Windows x64 Release'
      |    267 | +             'Perfil: sem PIX, pagamentos, creditos, contabilidade, locadora ou controle comercial de tempo'
      |    268 | +             "Branch: $env:GITHUB_REF_NAME"
      |    269 | +             "Commit da fonte: $env:GITHUB_SHA"
      |    270 | +             "Commit de win32-libs: $env:WIN32_LIBS_COMMIT"
      |    271 | +             "Runner: $env:ImageOS $env:ImageVersion"
      |    272 | +             "Gerado em UTC: $([DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm:ss'))"
      |    273 | +             'Build sem assinatura digital.'
      |    274 | +           ) | Set-Content -LiteralPath (Join-Path $stage 'BUILD-INFO.txt') `
      |    275 | +             -Encoding utf8NoBOM
      |    276 | + 
      |    277 | +           @'
      |    278 | +           @echo off
      |    279 | +           setlocal
      |    280 | +           cd /d "%~dp0"
      |    281 | +           if not exist "%~dp0_perfil_teste" mkdir "%~dp0_perfil_teste"
      |    282 | +           "%~dp0emulationstation.exe" --home "%~dp0_perfil_teste" --windowed --resolution 1280 720 --debug
      |    283 | +           set "exit_code=%ERRORLEVEL%"
      |    284 | +           echo.
      |    285 | +           echo TurboRama terminou com codigo %exit_code%.
      |    286 | +           pause
      |    287 | +           exit /b %exit_code%
      |    288 | +           '@ | Set-Content -LiteralPath (Join-Path $stage 'TESTAR-ISOLADO.cmd') `
      |    289 | +             -Encoding ascii
      |    290 | + 
      |    291 | +           function Invoke-SmokeTest([string]$argument) {
      |    292 | +             $process = Start-Process `
      |    293 | +               -FilePath (Join-Path $stage 'emulationstation.exe') `
      |    294 | +               -WorkingDirectory $stage `
      |    295 | +               -ArgumentList $argument `
      |    296 | +               -PassThru
      |    297 | +             if (-not $process.WaitForExit(30000)) {
      |    298 | +               Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
      |    299 | +               throw "Teste $argument excedeu 30 segundos."
      |    300 | +             }
      |    301 | +             if ($process.ExitCode -ne 0) {
      |    302 | +               throw "Teste $argument falhou com codigo $($process.ExitCode)."
      |    303 | +             }
      |    304 | +           }
      |    305 | + 
      |    306 | +           Invoke-SmokeTest '--help'
      |    307 | +           Invoke-SmokeTest '--protected-decorations-self-test'
      |    308 | +           Invoke-SmokeTest '--no-commercial-services-self-test'
      |    309 | + 
      |    310 | +           $manifest = Join-Path $stage 'SHA256SUMS.txt'
      |    311 | +           Get-ChildItem -LiteralPath $stage -Recurse -File |
      |    312 | +             Sort-Object FullName |
      |    313 | +             ForEach-Object {
      |    314 | +               $relative = [IO.Path]::GetRelativePath($stage, $_.FullName).Replace('\', '/')
      |    315 | +               $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
      |    316 | +               "$hash *$relative"
      |    317 | +             } | Set-Content -LiteralPath $manifest -Encoding ascii
      |    318 | + 
      |    319 | +           Compress-Archive -LiteralPath $stage -DestinationPath $zip `
      |    320 | +             -CompressionLevel NoCompression
      |    321 | +           if (-not (Test-Path -LiteralPath $zip -PathType Leaf)) {
      |    322 | +             throw 'ZIP final nao foi criado.'
      |    323 | +           }
      |    324 | + 
      |    325 | +           $zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
      |    326 | +           $zipHashFile = $zip + '.sha256'
      |    327 | +           "$zipHash *$([IO.Path]::GetFileName($zip))" |
      |    328 | +             Set-Content -LiteralPath $zipHashFile -Encoding ascii
      |    329 | + 
      |    330 | +           "ZIP_PATH=$zip" | Add-Content -LiteralPath $env:GITHUB_ENV -Encoding utf8
      |    331 | +           "ZIP_HASH_PATH=$zipHashFile" | Add-Content -LiteralPath $env:GITHUB_ENV -Encoding utf8
      |    332 | +           "ZIP_SHA256=$zipHash" | Add-Content -LiteralPath $env:GITHUB_ENV -Encoding utf8
      |    333 | +           "PACKAGE_FILE_COUNT=$(@(Get-ChildItem -LiteralPath $stage -Recurse -File).Count)" |
      |    334 | +             Add-Content -LiteralPath $env:GITHUB_ENV -Encoding utf8
      |    335 | + 
      |    336 | +           "Pacote: $zip"
      |    337 | +           "SHA-256: $zipHash"
      |    338 | +           "Binarios x64 verificados: $($portableBinaries.Count)"
      |    339 | +           "Plugins VLC: $pluginCount"
      |    340 | + 
      |    341 | +       - name: Publicar ZIP em um release separado
      |    342 | +         env:
      |    343 | +           GH_TOKEN: ${{ github.token }}
      |    344 | +         run: |
      |    345 | +           $ErrorActionPreference = 'Stop'
      |    346 | +           $notes = Join-Path $env:RUNNER_TEMP 'release-notes.md'
      |    347 | +           @"
      |    348 | +           Compilacao automatica Release x64 para cliente sem servicos comerciais.
      |    349 | + 
      |    350 | +           - Commit da fonte: $env:GITHUB_SHA
      |    351 | +           - Commit de win32-libs: $env:WIN32_LIBS_COMMIT
      |    352 | +           - Arquivos no pacote: $env:PACKAGE_FILE_COUNT
      |    353 | +           - SHA-256 do ZIP: $env:ZIP_SHA256
      |    354 | +           - Runner: Windows Server 2022 / Visual Studio 2022
      |    355 | +           - Removidos da compilacao: PIX, pagamentos, creditos, contabilidade,
      |    356 | +             locadora, cronometro e controle comercial de tempo
      |    357 | + 
      |    358 | +           O executavel nao possui assinatura digital. Use `TESTAR-ISOLADO.cmd`
      |    359 | +           para testar sem alterar o perfil normal do EmulationStation.
      |    360 | +           "@ | Set-Content -LiteralPath $notes -Encoding utf8NoBOM
      |    361 | + 
      |    362 | +           & gh release view $env:RELEASE_TAG `
      |    363 | +             --repo $env:GITHUB_REPOSITORY *> $null
      |    364 | +           $releaseExists = $LASTEXITCODE -eq 0
      |    365 | + 
      |    366 | +           if ($releaseExists) {
      |    367 | +             & gh release upload $env:RELEASE_TAG `
      |    368 | +               $env:ZIP_PATH $env:ZIP_HASH_PATH `
      |    369 | +               --repo $env:GITHUB_REPOSITORY --clobber
      |    370 | +             if ($LASTEXITCODE -ne 0) {
      |    371 | +               throw 'Falha ao atualizar os arquivos do release.'
      |    372 | +             }
      |    373 | + 
      |    374 | +             & gh api --method PATCH `
      |    375 | +               "repos/$env:GITHUB_REPOSITORY/git/refs/tags/$env:RELEASE_TAG" `
      |    376 | +               -f "sha=$env:GITHUB_SHA" -F force=true *> $null
      |    377 | +             if ($LASTEXITCODE -ne 0) {
      |    378 | +               throw 'Falha ao atualizar o commit da tag do release.'
      |    379 | +             }
      |    380 | + 
      |    381 | +             & gh release edit $env:RELEASE_TAG `
      |    382 | +               --repo $env:GITHUB_REPOSITORY `
      |    383 | +               --title "CLIENTE SEM SERVICOS - Windows x64" `
      |    384 | +               --notes-file $notes --prerelease=false --latest=false
      |    385 | +           }
      |    386 | +           else {
      |    387 | +             & gh release create $env:RELEASE_TAG `
      |    388 | +               $env:ZIP_PATH $env:ZIP_HASH_PATH `
      |    389 | +               --repo $env:GITHUB_REPOSITORY `
      |    390 | +               --target $env:GITHUB_SHA `
      |    391 | +               --title "CLIENTE SEM SERVICOS - Windows x64" `
      |    392 | +               --notes-file $notes --latest=false
      |    393 | +           }
      |    394 | +           if ($LASTEXITCODE -ne 0) {
      |    395 | +             throw 'Falha ao publicar o release.'
      |    396 | +           }
      |    397 | + 
      |    398 | +           $releaseUrl = (& gh release view $env:RELEASE_TAG `
      |    399 | +             --repo $env:GITHUB_REPOSITORY --json url --jq '.url').Trim()
      |    400 | +           @(
      |    401 | +             '## Compilacao concluida'
      |    402 | +             ''
      |    403 | +             "- Release: [$env:RELEASE_TAG]($releaseUrl)"
      |    404 | +             "- SHA-256: ``$env:ZIP_SHA256``"
      |    405 | +             "- Arquivos no pacote: $env:PACKAGE_FILE_COUNT"
      |    406 | +           ) | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8
```

Conferência: 1 trechos, 406 linhas adicionadas e 0 removidas.
