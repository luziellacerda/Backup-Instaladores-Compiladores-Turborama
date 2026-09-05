# 02-cliente: TurboramaEmulationStation/tools/tests/Test-NoCommercialServicesBuild.ps1

[Índice dos anexos](README.md) · [Guia de leitura](../../00-COMO-LER.md)

Teste automatizado: preparação, execução e asserções com dados sintéticos.

- Antes: `76b214874973fe24017823401216896f3d7a6f40`.
- Depois: `947dad45f5e9cd556cce6f15045a5dd6119bdf95`.
- [Arquivo resultante](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/tools/tests/Test-NoCommercialServicesBuild.ps1).

A coluna **ANTES** aponta para a revisão anterior; **DEPOIS** aponta para a revisão resultante. `+` adiciona, `-` remove e espaço conserva contexto. Numeração vazia indica linha inexistente naquela revisão. Nenhuma linha adicionada/removida desta comparação foi omitida; o contexto é de três linhas, não uma reprodução integral do arquivo. Leia a intenção, o fluxo e os riscos nos capítulos e confira o código literal abaixo.

## Trecho 1: antes 0, depois 1

Arquivo novo nesta comparação; não existe na revisão anterior. [Ver depois](https://github.com/luziellacerda/Backup-Instaladores-Compiladores-Turborama/blob/947dad45f5e9cd556cce6f15045a5dd6119bdf95/TurboramaEmulationStation/tools/tests/Test-NoCommercialServicesBuild.ps1#L1)

```text
ANTES | DEPOIS |   CÓDIGO
      |      1 | + param(
      |      2 | +     [Parameter(Mandatory = $true)]
      |      3 | +     [string]$BuildDirectory,
      |      4 | + 
      |      5 | +     [Parameter(Mandatory = $true)]
      |      6 | +     [string]$Executable
      |      7 | + )
      |      8 | + 
      |      9 | + $ErrorActionPreference = 'Stop'
      |     10 | + Set-StrictMode -Version Latest
      |     11 | + 
      |     12 | + function Assert([bool]$Condition, [string]$Message) {
      |     13 | +     if (-not $Condition) {
      |     14 | +         throw $Message
      |     15 | +     }
      |     16 | + }
      |     17 | + 
      |     18 | + $buildRoot = [IO.Path]::GetFullPath($BuildDirectory)
      |     19 | + $exePath = [IO.Path]::GetFullPath($Executable)
      |     20 | + $projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
      |     21 | + $cachePath = Join-Path $buildRoot 'CMakeCache.txt'
      |     22 | + $appProjectPath = Join-Path $buildRoot 'es-app\emulationstation.vcxproj'
      |     23 | + $coreProjectPath = Join-Path $buildRoot 'es-core\es-core.vcxproj'
      |     24 | + 
      |     25 | + Assert (Test-Path -LiteralPath $cachePath -PathType Leaf) "CMakeCache ausente: $cachePath"
      |     26 | + Assert (Test-Path -LiteralPath $appProjectPath -PathType Leaf) "Projeto MSBuild ausente: $appProjectPath"
      |     27 | + Assert (Test-Path -LiteralPath $coreProjectPath -PathType Leaf) "Projeto MSBuild ausente: $coreProjectPath"
      |     28 | + Assert (Test-Path -LiteralPath $exePath -PathType Leaf) "Executavel ausente: $exePath"
      |     29 | + 
      |     30 | + $cache = Get-Content -LiteralPath $cachePath -Raw
      |     31 | + Assert ($cache -match '(?m)^TURBORAMA_ENABLE_COMMERCIAL_SERVICES:BOOL=OFF\s*$') `
      |     32 | +     'O cache CMake nao identifica o perfil sem servicos comerciais.'
      |     33 | + Assert ($cache -match '(?m)^TURBORAMA_RELEASE_HARDENING:BOOL=ON\s*$') `
      |     34 | +     'O cache CMake nao preserva o hardening geral da compilacao Release.'
      |     35 | + 
      |     36 | + $appProject = Get-Content -LiteralPath $appProjectPath -Raw
      |     37 | + $coreProject = Get-Content -LiteralPath $coreProjectPath -Raw
      |     38 | + Assert ($appProject.IndexOf('TURBORAMA_NO_COMMERCIAL_SERVICES=1', [StringComparison]::Ordinal) -ge 0) `
      |     39 | +     'O perfil sem servicos nao foi aplicado ao executavel.'
      |     40 | + Assert ($coreProject.IndexOf('TURBORAMA_NO_COMMERCIAL_SERVICES=1', [StringComparison]::Ordinal) -lt 0) `
      |     41 | +     'A macro do perfil sem servicos vazou para a biblioteca es-core.'
      |     42 | + 
      |     43 | + $forbiddenSources = @(
      |     44 | +     'CreditManager.cpp',
      |     45 | +     'PixBridge.cpp',
      |     46 | +     'PixAgentManager.cpp',
      |     47 | +     'GuiCreditPlayerSelect.cpp',
      |     48 | +     'GuiCreditOperatorPanel.cpp',
      |     49 | +     'GuiPixPurchase.cpp',
      |     50 | +     'GuiPixOwnerSettings.cpp'
      |     51 | + )
      |     52 | + foreach ($source in $forbiddenSources) {
      |     53 | +     Assert ($appProject.IndexOf($source, [StringComparison]::OrdinalIgnoreCase) -lt 0) `
      |     54 | +         "Fonte comercial ainda presente no projeto gerado: $source"
      |     55 | + }
      |     56 | + 
      |     57 | + $requiredAppSources = @(
      |     58 | +     'FileData.cpp',
      |     59 | +     'MainMenuAuth.cpp',
      |     60 | +     'SystemView.cpp'
      |     61 | + )
      |     62 | + foreach ($source in $requiredAppSources) {
      |     63 | +     Assert ($appProject.IndexOf($source, [StringComparison]::OrdinalIgnoreCase) -ge 0) `
      |     64 | +         "Fonte de otimizacao ausente do projeto do aplicativo: $source"
      |     65 | + }
      |     66 | + 
      |     67 | + $requiredCoreSources = @(
      |     68 | +     'Settings.cpp',
      |     69 | +     'Window.cpp',
      |     70 | +     'CarouselComponent.cpp',
      |     71 | +     'VideoVlcComponent.cpp',
      |     72 | +     'TextureData.cpp',
      |     73 | +     'TextureDataManager.cpp'
      |     74 | + )
      |     75 | + foreach ($source in $requiredCoreSources) {
      |     76 | +     Assert ($coreProject.IndexOf($source, [StringComparison]::OrdinalIgnoreCase) -ge 0) `
      |     77 | +         "Fonte de otimizacao ausente do projeto es-core: $source"
      |     78 | + }
      |     79 | + 
      |     80 | + # These implementation signatures were introduced by the carousel/video memory
      |     81 | + # optimization commit. File names and menu labels alone existed before it and
      |     82 | + # would not detect an accidental revert of the actual cache/pool logic.
      |     83 | + $optimizationSourceSignatures = @{
      |     84 | +     'es-app\src\FileData.cpp' = @(
      |     85 | +         'sCarouselVideoCacheGeneration',
      |     86 | +         'resolveCarouselVideoPath(bool forceRefresh)'
      |     87 | +     )
      |     88 | +     'es-app\src\views\SystemView.cpp' = @(
      |     89 | +         'mFrontCarouselSyncValid',
      |     90 | +         'syncFrontCarouselVideos()'
      |     91 | +     )
      |     92 | +     'es-core\src\components\CarouselComponent.cpp' = @(
      |     93 | +         'mCellVideoPool',
      |     94 | +         'getCellVideoPoolLimit()',
      |     95 | +         'trimCellVideoPool()'
      |     96 | +     )
      |     97 | +     'es-core\src\components\VideoVlcComponent.cpp' = @(
      |     98 | +         'MediaPlayerReleaseQueue',
      |     99 | +         'sVideoBufferBudgetBytes',
      |    100 | +         'getBufferPoolCacheLimitBytes',
      |    101 | +         'trimBufferPoolLocked'
      |    102 | +     )
      |    103 | +     'es-core\src\Settings.cpp' = @(
      |    104 | +         'mIntMap["MaxVideoRAM"] = 768;',
      |    105 | +         'mIntMap["MaxAsyncQueue"] = 12;',
      |    106 | +         'mBoolMap["EnforceVideoLimit"] = true;'
      |    107 | +     )
      |    108 | + }
      |    109 | + foreach ($relativePath in $optimizationSourceSignatures.Keys) {
      |    110 | +     $sourcePath = Join-Path $projectRoot $relativePath
      |    111 | +     Assert (Test-Path -LiteralPath $sourcePath -PathType Leaf) `
      |    112 | +         "Fonte obrigatoria de otimizacao ausente: $relativePath"
      |    113 | +     $sourceText = Get-Content -LiteralPath $sourcePath -Raw
      |    114 | +     foreach ($signature in $optimizationSourceSignatures[$relativePath]) {
      |    115 | +         Assert ($sourceText.IndexOf($signature, [StringComparison]::Ordinal) -ge 0) `
      |    116 | +             "Implementacao de otimizacao ausente de $relativePath`: $signature"
      |    117 | +     }
      |    118 | + }
      |    119 | + 
      |    120 | + $releaseGroupMatch = [regex]::Match(
      |    121 | +     $appProject,
      |    122 | +     '<ItemDefinitionGroup Condition="[^"]*Release\|x64[^"]*">.*?</ItemDefinitionGroup>',
      |    123 | +     [Text.RegularExpressions.RegexOptions]::Singleline)
      |    124 | + Assert ($releaseGroupMatch.Success) 'Configuracao Release x64 ausente do projeto do aplicativo.'
      |    125 | + $releaseGroup = $releaseGroupMatch.Value
      |    126 | + $requiredReleaseProperties = @(
      |    127 | +     '<Optimization>MaxSpeed</Optimization>',
      |    128 | +     '<WholeProgramOptimization>true</WholeProgramOptimization>',
      |    129 | +     '<ControlFlowGuard>Guard</ControlFlowGuard>',
      |    130 | +     '<BufferSecurityCheck>true</BufferSecurityCheck>',
      |    131 | +     '<FunctionLevelLinking>true</FunctionLevelLinking>',
      |    132 | +     '<LinkTimeCodeGeneration>UseLinkTimeCodeGeneration</LinkTimeCodeGeneration>',
      |    133 | +     '<OptimizeReferences>true</OptimizeReferences>',
      |    134 | +     '<EnableCOMDATFolding>true</EnableCOMDATFolding>',
      |    135 | +     '<CETCompat>true</CETCompat>',
      |    136 | +     '<DataExecutionPrevention>true</DataExecutionPrevention>',
      |    137 | +     '<RandomizedBaseAddress>true</RandomizedBaseAddress>'
      |    138 | + )
      |    139 | + foreach ($property in $requiredReleaseProperties) {
      |    140 | +     Assert ($releaseGroup.IndexOf($property, [StringComparison]::Ordinal) -ge 0) `
      |    141 | +         "Propriedade de otimizacao/hardening ausente da configuracao Release: $property"
      |    142 | + }
      |    143 | + 
      |    144 | + $dependencyRoot = Join-Path $projectRoot 'win32-libs'
      |    145 | + $runtimeDirectories = @(
      |    146 | +     (Join-Path $dependencyRoot 'FreeImage\x64'),
      |    147 | +     (Join-Path $dependencyRoot 'curl\x64\bin'),
      |    148 | +     (Join-Path $dependencyRoot 'libvlc\x64'),
      |    149 | +     (Join-Path $dependencyRoot 'SDL2\x64'),
      |    150 | +     (Join-Path $dependencyRoot 'SDL2_mixer\x64'),
      |    151 | +     (Join-Path $dependencyRoot 'SDL2_mixer\x64\optional')
      |    152 | + ) | Where-Object { Test-Path -LiteralPath $_ -PathType Container }
      |    153 | + 
      |    154 | + $savedPath = [Environment]::GetEnvironmentVariable('Path', 'Process')
      |    155 | + $disabledCommercialCommands = @(
      |    156 | +     '--credit-warning-overlay-self-test',
      |    157 | +     '--pix-agent-manager-self-test',
      |    158 | +     '--pix-agent-trust-self-test',
      |    159 | +     '--pix-agent-start-once',
      |    160 | +     '--pix-verify-event',
      |    161 | +     '--pix-test-qr-cache',
      |    162 | +     '--pix-process-once',
      |    163 | +     '--pix-create-request'
      |    164 | + )
      |    165 | + try {
      |    166 | +     $env:Path = (($runtimeDirectories + @($savedPath)) -join ';')
      |    167 | +     $profileTest = Start-Process -FilePath $exePath `
      |    168 | +         -ArgumentList '--no-commercial-services-self-test' `
      |    169 | +         -WorkingDirectory (Split-Path -Parent $exePath) `
      |    170 | +         -WindowStyle Hidden -Wait -PassThru
      |    171 | +     Assert ($profileTest.ExitCode -eq 0) `
      |    172 | +         "O executavel recusou o perfil sem servicos (codigo $($profileTest.ExitCode))."
      |    173 | + 
      |    174 | +     $authenticationTest = Start-Process -FilePath $exePath `
      |    175 | +         -ArgumentList '--main-menu-auth-self-test' `
      |    176 | +         -WorkingDirectory (Split-Path -Parent $exePath) `
      |    177 | +         -WindowStyle Hidden -Wait -PassThru
      |    178 | +     Assert ($authenticationTest.ExitCode -eq 0) `
      |    179 | +         "A protecao independente do menu START falhou (codigo $($authenticationTest.ExitCode))."
      |    180 | + 
      |    181 | +     foreach ($command in $disabledCommercialCommands) {
      |    182 | +         $commandTest = Start-Process -FilePath $exePath `
      |    183 | +             -ArgumentList $command `
      |    184 | +             -WorkingDirectory (Split-Path -Parent $exePath) `
      |    185 | +             -WindowStyle Hidden -Wait -PassThru
      |    186 | +         Assert ($commandTest.ExitCode -eq 34) `
      |    187 | +             "O comando comercial legado nao foi rejeitado: $command (codigo $($commandTest.ExitCode))."
      |    188 | +     }
      |    189 | + }
      |    190 | + finally {
      |    191 | +     $env:Path = $savedPath
      |    192 | + }
      |    193 | + 
      |    194 | + # Search the PE as bytes. This catches accidentally linked code even when its
      |    195 | + # menu entry is hidden. The implementation streams the file and does not load
      |    196 | + # the large executable into PowerShell memory.
      |    197 | + if (-not ('TurboRama.BinaryPattern' -as [type])) {
      |    198 | +     Add-Type -TypeDefinition @'
      |    199 | + using System;
      |    200 | + using System.Collections.Generic;
      |    201 | + using System.IO;
      |    202 | + 
      |    203 | + namespace TurboRama
      |    204 | + {
      |    205 | +     public static class BinaryPattern
      |    206 | +     {
      |    207 | +         public static string FindAny(string path, string[] patterns)
      |    208 | +         {
      |    209 | +             if (patterns == null || patterns.Length == 0)
      |    210 | +                 return null;
      |    211 | + 
      |    212 | +             int longest = 1;
      |    213 | +             foreach (string pattern in patterns)
      |    214 | +                 longest = Math.Max(longest, pattern.Length);
      |    215 | + 
      |    216 | +             const int chunkSize = 4 * 1024 * 1024;
      |    217 | +             byte[] buffer = new byte[chunkSize + longest - 1];
      |    218 | +             int retained = 0;
      |    219 | +             using (FileStream stream = File.OpenRead(path))
      |    220 | +             {
      |    221 | +                 int read;
      |    222 | +                 while ((read = stream.Read(buffer, retained, chunkSize)) > 0)
      |    223 | +                 {
      |    224 | +                     int total = retained + read;
      |    225 | +                     string text = System.Text.Encoding.ASCII.GetString(buffer, 0, total);
      |    226 | +                     foreach (string pattern in patterns)
      |    227 | +                         if (text.IndexOf(pattern, StringComparison.Ordinal) >= 0)
      |    228 | +                             return pattern;
      |    229 | + 
      |    230 | +                     retained = Math.Min(longest - 1, total);
      |    231 | +                     Buffer.BlockCopy(buffer, total - retained, buffer, 0, retained);
      |    232 | +                 }
      |    233 | +             }
      |    234 | +             return null;
      |    235 | +         }
      |    236 | + 
      |    237 | +         public static string[] FindMissing(string path, string[] patterns)
      |    238 | +         {
      |    239 | +             HashSet<string> missing = new HashSet<string>(patterns, StringComparer.Ordinal);
      |    240 | +             if (missing.Count == 0)
      |    241 | +                 return new string[0];
      |    242 | + 
      |    243 | +             int longest = 1;
      |    244 | +             foreach (string pattern in missing)
      |    245 | +                 longest = Math.Max(longest, pattern.Length);
      |    246 | + 
      |    247 | +             const int chunkSize = 4 * 1024 * 1024;
      |    248 | +             byte[] buffer = new byte[chunkSize + longest - 1];
      |    249 | +             int retained = 0;
      |    250 | +             using (FileStream stream = File.OpenRead(path))
      |    251 | +             {
      |    252 | +                 int read;
      |    253 | +                 while (missing.Count > 0 && (read = stream.Read(buffer, retained, chunkSize)) > 0)
      |    254 | +                 {
      |    255 | +                     int total = retained + read;
      |    256 | +                     string text = System.Text.Encoding.ASCII.GetString(buffer, 0, total);
      |    257 | +                     List<string> found = new List<string>();
      |    258 | +                     foreach (string pattern in missing)
      |    259 | +                         if (text.IndexOf(pattern, StringComparison.Ordinal) >= 0)
      |    260 | +                             found.Add(pattern);
      |    261 | +                     foreach (string pattern in found)
      |    262 | +                         missing.Remove(pattern);
      |    263 | + 
      |    264 | +                     retained = Math.Min(longest - 1, total);
      |    265 | +                     Buffer.BlockCopy(buffer, total - retained, buffer, 0, retained);
      |    266 | +                 }
      |    267 | +             }
      |    268 | + 
      |    269 | +             string[] result = new string[missing.Count];
      |    270 | +             missing.CopyTo(result);
      |    271 | +             Array.Sort(result, StringComparer.Ordinal);
      |    272 | +             return result;
      |    273 | +         }
      |    274 | +     }
      |    275 | + }
      |    276 | + '@
      |    277 | + }
      |    278 | + 
      |    279 | + $forbiddenBinaryMarkers = @(
      |    280 | +     'CreditManager',
      |    281 | +     'arcade_players.dat',
      |    282 | +     'TurboRamaPixAgent',
      |    283 | +     'PIX_AGENT_MANAGER_TEST',
      |    284 | +     'COMPRAR TEMPO COM PIX',
      |    285 | +     'PAGAMENTO PIX',
      |    286 | +     'CONTABILIDADE LOCADORA',
      |    287 | +     'F10 disponivel somente para credito'
      |    288 | + )
      |    289 | + $foundMarker = [TurboRama.BinaryPattern]::FindAny($exePath, $forbiddenBinaryMarkers)
      |    290 | + Assert ([string]::IsNullOrEmpty($foundMarker)) `
      |    291 | +     "Marcador de servico comercial encontrado no executavel: $foundMarker"
      |    292 | + 
      |    293 | + $requiredOptimizationMarkers = @(
      |    294 | +     'RAM CACHE LIMIT',
      |    295 | +     'ASYNC IMAGE QUEUE SIZE',
      |    296 | +     'OPTIMIZE IMAGES VRAM USE',
      |    297 | +     'OPTIMIZE VIDEO VRAM USAGE',
      |    298 | +     'USE FILESYSTEM CACHE',
      |    299 | +     'MAX CONCURRENT VIDEOS',
      |    300 | +     'PRELOAD UI ELEMENTS ON BOOT',
      |    301 | +     'THREADED LOADING',
      |    302 | +     'ASYNC IMAGE LOADING'
      |    303 | + )
      |    304 | + $requiredNonCommercialMarkers = @(
      |    305 | +     'pbkdf2-sha256$',
      |    306 | +     'main_menu_auth.cfg',
      |    307 | +     'MAIN_MENU_AUTH_TEST=%s',
      |    308 | +     'SENHA MENU START',
      |    309 | +     'SENHA PAINEL F11',
      |    310 | +     'ALTERAR SENHA DO MENU START',
      |    311 | +     'ABRIR TURBO SISTEMA...',
      |    312 | +     'TROCAR DE USUARIO...',
      |    313 | +     'ENCERRAR PROCESSO...'
      |    314 | + )
      |    315 | + $requiredBinaryMarkers = $requiredOptimizationMarkers + $requiredNonCommercialMarkers
      |    316 | + $missingMarkers = [TurboRama.BinaryPattern]::FindMissing($exePath, $requiredBinaryMarkers)
      |    317 | + Assert ($missingMarkers.Count -eq 0) `
      |    318 | +     "Marcadores obrigatorios ausentes do executavel: $($missingMarkers -join ', ')"
      |    319 | + 
      |    320 | + Write-Host 'NO_COMMERCIAL_SERVICES_TEST=OK'
      |    321 | + Write-Host "Executable=$exePath"
      |    322 | + Write-Host "ExcludedSources=$($forbiddenSources.Count)"
      |    323 | + Write-Host "RequiredOptimizationSources=$($requiredAppSources.Count + $requiredCoreSources.Count)"
      |    324 | + Write-Host "RejectedBinaryMarkers=$($forbiddenBinaryMarkers.Count)"
      |    325 | + Write-Host "PreservedOptimizationMarkers=$($requiredOptimizationMarkers.Count)"
      |    326 | + Write-Host "PreservedNonCommercialMarkers=$($requiredNonCommercialMarkers.Count)"
      |    327 | + Write-Host "OptimizationImplementationSignatures=$(($optimizationSourceSignatures.Values | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum)"
      |    328 | + Write-Host "RejectedCommercialCommands=$($disabledCommercialCommands.Count)"
```

Conferência: 1 trechos, 328 linhas adicionadas e 0 removidas.
