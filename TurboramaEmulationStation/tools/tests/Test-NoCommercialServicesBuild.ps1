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
$projectPath = Join-Path $buildRoot 'es-app\emulationstation.vcxproj'

Assert (Test-Path -LiteralPath $cachePath -PathType Leaf) "CMakeCache ausente: $cachePath"
Assert (Test-Path -LiteralPath $projectPath -PathType Leaf) "Projeto MSBuild ausente: $projectPath"
Assert (Test-Path -LiteralPath $exePath -PathType Leaf) "Executavel ausente: $exePath"

$cache = Get-Content -LiteralPath $cachePath -Raw
Assert ($cache -match '(?m)^TURBORAMA_ENABLE_COMMERCIAL_SERVICES:BOOL=OFF\s*$') `
    'O cache CMake nao identifica o perfil sem servicos comerciais.'

$project = Get-Content -LiteralPath $projectPath -Raw
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
    Assert ($project.IndexOf($source, [StringComparison]::OrdinalIgnoreCase) -lt 0) `
        "Fonte comercial ainda presente no projeto gerado: $source"
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
try {
    $env:Path = (($runtimeDirectories + @($savedPath)) -join ';')
    $profileTest = Start-Process -FilePath $exePath `
        -ArgumentList '--no-commercial-services-self-test' `
        -WorkingDirectory (Split-Path -Parent $exePath) `
        -WindowStyle Hidden -Wait -PassThru
    Assert ($profileTest.ExitCode -eq 0) `
        "O executavel recusou o perfil sem servicos (codigo $($profileTest.ExitCode))."
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
    }
}
'@
}

$forbiddenBinaryMarkers = @(
    'CreditManager',
    'arcade_credit',
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

Write-Host 'NO_COMMERCIAL_SERVICES_TEST=OK'
Write-Host "Executable=$exePath"
Write-Host "ExcludedSources=$($forbiddenSources.Count)"
Write-Host "RejectedBinaryMarkers=$($forbiddenBinaryMarkers.Count)"
