param(
    [Parameter(Mandatory = $true)]
    [string]$BuildDirectory,

    [Parameter(Mandatory = $true)]
    [string]$Executable
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
}

$buildRoot = [IO.Path]::GetFullPath($BuildDirectory)
$exePath = [IO.Path]::GetFullPath($Executable)
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$cachePath = Join-Path $buildRoot 'CMakeCache.txt'
$appProjectPath = Join-Path $buildRoot 'es-app\emulationstation.vcxproj'
$coreProjectPath = Join-Path $buildRoot 'es-core\es-core.vcxproj'

Assert (Test-Path -LiteralPath $cachePath -PathType Leaf) "CMakeCache ausente: $cachePath"
Assert (Test-Path -LiteralPath $appProjectPath -PathType Leaf) "Projeto MSBuild ausente: $appProjectPath"
Assert (Test-Path -LiteralPath $coreProjectPath -PathType Leaf) "Projeto MSBuild ausente: $coreProjectPath"
Assert (Test-Path -LiteralPath $exePath -PathType Leaf) "Executavel ausente: $exePath"

$cache = Get-Content -LiteralPath $cachePath -Raw
Assert ($cache -match '(?m)^TURBORAMA_ENABLE_COMMERCIAL_SERVICES:BOOL=OFF\s*$') `
    'O cache CMake nao identifica o perfil sem servicos comerciais.'
Assert ($cache -match '(?m)^TURBORAMA_RELEASE_HARDENING:BOOL=ON\s*$') `
    'O cache CMake nao preserva o hardening geral da compilacao Release.'

$appProject = Get-Content -LiteralPath $appProjectPath -Raw
$coreProject = Get-Content -LiteralPath $coreProjectPath -Raw
Assert ($appProject.IndexOf('TURBORAMA_NO_COMMERCIAL_SERVICES=1', [StringComparison]::Ordinal) -ge 0) `
    'O perfil sem servicos nao foi aplicado ao executavel.'
Assert ($coreProject.IndexOf('TURBORAMA_NO_COMMERCIAL_SERVICES=1', [StringComparison]::Ordinal) -lt 0) `
    'A macro do perfil sem servicos vazou para a biblioteca es-core.'

$forbiddenSources = @(
    'CreditManager.cpp',
    'PixBridge.cpp',
    'PixAgentManager.cpp',
    'GuiCreditPlayerSelect.cpp',
    'GuiCreditOperatorPanel.cpp',
    'GuiPixPurchase.cpp',
    'GuiPixOwnerSettings.cpp'
)
foreach ($source in $forbiddenSources) {
    Assert ($appProject.IndexOf($source, [StringComparison]::OrdinalIgnoreCase) -lt 0) `
        "Fonte comercial ainda presente no projeto gerado: $source"
}

$requiredAppSources = @(
    'FileData.cpp',
    'MainMenuAuth.cpp',
    'SystemView.cpp'
)
foreach ($source in $requiredAppSources) {
    Assert ($appProject.IndexOf($source, [StringComparison]::OrdinalIgnoreCase) -ge 0) `
        "Fonte de otimizacao ausente do projeto do aplicativo: $source"
}

$requiredCoreSources = @(
    'Settings.cpp',
    'Window.cpp',
    'CarouselComponent.cpp',
    'VideoVlcComponent.cpp',
    'TextureData.cpp',
    'TextureDataManager.cpp'
)
foreach ($source in $requiredCoreSources) {
    Assert ($coreProject.IndexOf($source, [StringComparison]::OrdinalIgnoreCase) -ge 0) `
        "Fonte de otimizacao ausente do projeto es-core: $source"
}

# These implementation signatures were introduced by the carousel/video memory
# optimization commit. File names and menu labels alone existed before it and
# would not detect an accidental revert of the actual cache/pool logic.
$optimizationSourceSignatures = @{
    'es-app\src\FileData.cpp' = @(
        'sCarouselVideoCacheGeneration',
        'resolveCarouselVideoPath(bool forceRefresh)'
    )
    'es-app\src\views\SystemView.cpp' = @(
        'mFrontCarouselSyncValid',
        'syncFrontCarouselVideos()'
    )
    'es-core\src\components\CarouselComponent.cpp' = @(
        'mCellVideoPool',
        'getCellVideoPoolLimit()',
        'trimCellVideoPool()'
    )
    'es-core\src\components\VideoVlcComponent.cpp' = @(
        'MediaPlayerReleaseQueue',
        'sVideoBufferBudgetBytes',
        'getBufferPoolCacheLimitBytes',
        'trimBufferPoolLocked'
    )
    'es-core\src\Settings.cpp' = @(
        'mIntMap["MaxVideoRAM"] = 768;',
        'mIntMap["MaxAsyncQueue"] = 12;',
        'mBoolMap["EnforceVideoLimit"] = true;'
    )
}
foreach ($relativePath in $optimizationSourceSignatures.Keys) {
    $sourcePath = Join-Path $projectRoot $relativePath
    Assert (Test-Path -LiteralPath $sourcePath -PathType Leaf) `
        "Fonte obrigatoria de otimizacao ausente: $relativePath"
    $sourceText = Get-Content -LiteralPath $sourcePath -Raw
    foreach ($signature in $optimizationSourceSignatures[$relativePath]) {
        Assert ($sourceText.IndexOf($signature, [StringComparison]::Ordinal) -ge 0) `
            "Implementacao de otimizacao ausente de $relativePath`: $signature"
    }
}

$releaseGroupMatch = [regex]::Match(
    $appProject,
    '<ItemDefinitionGroup Condition="[^"]*Release\|x64[^"]*">.*?</ItemDefinitionGroup>',
    [Text.RegularExpressions.RegexOptions]::Singleline)
Assert ($releaseGroupMatch.Success) 'Configuracao Release x64 ausente do projeto do aplicativo.'
$releaseGroup = $releaseGroupMatch.Value
$requiredReleaseProperties = @(
    '<Optimization>MaxSpeed</Optimization>',
    '<WholeProgramOptimization>true</WholeProgramOptimization>',
    '<ControlFlowGuard>Guard</ControlFlowGuard>',
    '<BufferSecurityCheck>true</BufferSecurityCheck>',
    '<FunctionLevelLinking>true</FunctionLevelLinking>',
    '<LinkTimeCodeGeneration>UseLinkTimeCodeGeneration</LinkTimeCodeGeneration>',
    '<OptimizeReferences>true</OptimizeReferences>',
    '<EnableCOMDATFolding>true</EnableCOMDATFolding>',
    '<CETCompat>true</CETCompat>',
    '<DataExecutionPrevention>true</DataExecutionPrevention>',
    '<RandomizedBaseAddress>true</RandomizedBaseAddress>'
)
foreach ($property in $requiredReleaseProperties) {
    Assert ($releaseGroup.IndexOf($property, [StringComparison]::Ordinal) -ge 0) `
        "Propriedade de otimizacao/hardening ausente da configuracao Release: $property"
}

$dependencyRoot = Join-Path $projectRoot 'win32-libs'
$runtimeDirectories = @(
    (Join-Path $dependencyRoot 'FreeImage\x64'),
    (Join-Path $dependencyRoot 'curl\x64\bin'),
    (Join-Path $dependencyRoot 'libvlc\x64'),
    (Join-Path $dependencyRoot 'SDL2\x64'),
    (Join-Path $dependencyRoot 'SDL2_mixer\x64'),
    (Join-Path $dependencyRoot 'SDL2_mixer\x64\optional')
) | Where-Object { Test-Path -LiteralPath $_ -PathType Container }

$savedPath = [Environment]::GetEnvironmentVariable('Path', 'Process')
$disabledCommercialCommands = @(
    '--credit-warning-overlay-self-test',
    '--pix-agent-manager-self-test',
    '--pix-agent-trust-self-test',
    '--pix-agent-start-once',
    '--pix-verify-event',
    '--pix-test-qr-cache',
    '--pix-process-once',
    '--pix-create-request'
)
try {
    $env:Path = (($runtimeDirectories + @($savedPath)) -join ';')
    $profileTest = Start-Process -FilePath $exePath `
        -ArgumentList '--no-commercial-services-self-test' `
        -WorkingDirectory (Split-Path -Parent $exePath) `
        -WindowStyle Hidden -Wait -PassThru
    Assert ($profileTest.ExitCode -eq 0) `
        "O executavel recusou o perfil sem servicos (codigo $($profileTest.ExitCode))."

    $authenticationTest = Start-Process -FilePath $exePath `
        -ArgumentList '--main-menu-auth-self-test' `
        -WorkingDirectory (Split-Path -Parent $exePath) `
        -WindowStyle Hidden -Wait -PassThru
    Assert ($authenticationTest.ExitCode -eq 0) `
        "A protecao independente do menu START falhou (codigo $($authenticationTest.ExitCode))."

    foreach ($command in $disabledCommercialCommands) {
        $commandTest = Start-Process -FilePath $exePath `
            -ArgumentList $command `
            -WorkingDirectory (Split-Path -Parent $exePath) `
            -WindowStyle Hidden -Wait -PassThru
        Assert ($commandTest.ExitCode -eq 34) `
            "O comando comercial legado nao foi rejeitado: $command (codigo $($commandTest.ExitCode))."
    }
}
finally {
    $env:Path = $savedPath
}

# Search the PE as bytes. This catches accidentally linked code even when its
# menu entry is hidden. The implementation streams the file and does not load
# the large executable into PowerShell memory.
if (-not ('TurboRama.BinaryPattern' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;

namespace TurboRama
{
    public static class BinaryPattern
    {
        public static string FindAny(string path, string[] patterns)
        {
            if (patterns == null || patterns.Length == 0)
                return null;

            int longest = 1;
            foreach (string pattern in patterns)
                longest = Math.Max(longest, pattern.Length);

            const int chunkSize = 4 * 1024 * 1024;
            byte[] buffer = new byte[chunkSize + longest - 1];
            int retained = 0;
            using (FileStream stream = File.OpenRead(path))
            {
                int read;
                while ((read = stream.Read(buffer, retained, chunkSize)) > 0)
                {
                    int total = retained + read;
                    string text = System.Text.Encoding.ASCII.GetString(buffer, 0, total);
                    foreach (string pattern in patterns)
                        if (text.IndexOf(pattern, StringComparison.Ordinal) >= 0)
                            return pattern;

                    retained = Math.Min(longest - 1, total);
                    Buffer.BlockCopy(buffer, total - retained, buffer, 0, retained);
                }
            }
            return null;
        }

        public static string[] FindMissing(string path, string[] patterns)
        {
            HashSet<string> missing = new HashSet<string>(patterns, StringComparer.Ordinal);
            if (missing.Count == 0)
                return new string[0];

            int longest = 1;
            foreach (string pattern in missing)
                longest = Math.Max(longest, pattern.Length);

            const int chunkSize = 4 * 1024 * 1024;
            byte[] buffer = new byte[chunkSize + longest - 1];
            int retained = 0;
            using (FileStream stream = File.OpenRead(path))
            {
                int read;
                while (missing.Count > 0 && (read = stream.Read(buffer, retained, chunkSize)) > 0)
                {
                    int total = retained + read;
                    string text = System.Text.Encoding.ASCII.GetString(buffer, 0, total);
                    List<string> found = new List<string>();
                    foreach (string pattern in missing)
                        if (text.IndexOf(pattern, StringComparison.Ordinal) >= 0)
                            found.Add(pattern);
                    foreach (string pattern in found)
                        missing.Remove(pattern);

                    retained = Math.Min(longest - 1, total);
                    Buffer.BlockCopy(buffer, total - retained, buffer, 0, retained);
                }
            }

            string[] result = new string[missing.Count];
            missing.CopyTo(result);
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }
    }
}
'@
}

$forbiddenBinaryMarkers = @(
    'CreditManager',
    'arcade_players.dat',
    'TurboRamaPixAgent',
    'PIX_AGENT_MANAGER_TEST',
    'COMPRAR TEMPO COM PIX',
    'PAGAMENTO PIX',
    'CONTABILIDADE LOCADORA',
    'F10 disponivel somente para credito'
)
$foundMarker = [TurboRama.BinaryPattern]::FindAny($exePath, $forbiddenBinaryMarkers)
Assert ([string]::IsNullOrEmpty($foundMarker)) `
    "Marcador de servico comercial encontrado no executavel: $foundMarker"

$requiredOptimizationMarkers = @(
    'RAM CACHE LIMIT',
    'ASYNC IMAGE QUEUE SIZE',
    'OPTIMIZE IMAGES VRAM USE',
    'OPTIMIZE VIDEO VRAM USAGE',
    'USE FILESYSTEM CACHE',
    'MAX CONCURRENT VIDEOS',
    'PRELOAD UI ELEMENTS ON BOOT',
    'THREADED LOADING',
    'ASYNC IMAGE LOADING'
)
$requiredNonCommercialMarkers = @(
    'pbkdf2-sha256$',
    'main_menu_auth.cfg',
    'MAIN_MENU_AUTH_TEST=%s',
    'SENHA MENU START',
    'SENHA PAINEL F11',
    'ALTERAR SENHA DO MENU START',
    'ABRIR TURBO SISTEMA...',
    'TROCAR DE USUARIO...',
    'ENCERRAR PROCESSO...'
)
$requiredBinaryMarkers = $requiredOptimizationMarkers + $requiredNonCommercialMarkers
$missingMarkers = [TurboRama.BinaryPattern]::FindMissing($exePath, $requiredBinaryMarkers)
Assert ($missingMarkers.Count -eq 0) `
    "Marcadores obrigatorios ausentes do executavel: $($missingMarkers -join ', ')"

Write-Host 'NO_COMMERCIAL_SERVICES_TEST=OK'
Write-Host "Executable=$exePath"
Write-Host "ExcludedSources=$($forbiddenSources.Count)"
Write-Host "RequiredOptimizationSources=$($requiredAppSources.Count + $requiredCoreSources.Count)"
Write-Host "RejectedBinaryMarkers=$($forbiddenBinaryMarkers.Count)"
Write-Host "PreservedOptimizationMarkers=$($requiredOptimizationMarkers.Count)"
Write-Host "PreservedNonCommercialMarkers=$($requiredNonCommercialMarkers.Count)"
Write-Host "OptimizationImplementationSignatures=$(($optimizationSourceSignatures.Values | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum)"
Write-Host "RejectedCommercialCommands=$($disabledCommercialCommands.Count)"
