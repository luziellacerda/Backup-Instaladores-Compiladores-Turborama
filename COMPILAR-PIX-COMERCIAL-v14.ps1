#Requires -Version 5.1
<#
.SYNOPSIS
  Compila e empacota o TurboRama PIX Comercial v14 sem depender do GPT.

.DESCRIPTION
  O fluxo trabalha somente dentro do repositorio. Nao altera a instalacao em
  D:\emulationstation. O Access Token e os arquivos secret.dat nunca sao lidos,
  copiados ou incluidos no pacote.
#>
param(
    [switch]$UsarEmulationStationExistente,
    [switch]$Limpar,
    [switch]$TestarInstalador,
    [switch]$SemPausa
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$RepoRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$WorkspaceRoot = Split-Path (Split-Path $RepoRoot -Parent) -Parent
$ProjectRoot = Join-Path $RepoRoot 'TurboramaEmulationStation'
$WorkRoot = Join-Path $ProjectRoot 'build-pix-commercial-v14'
$OutputRoot = Join-Path $ProjectRoot 'PIX-COMERCIAL\GERADO-v14'
$BundleRoot = Join-Path $WorkRoot 'bundle'
$ArchiveRoot = Join-Path $WorkRoot 'archive-update'
$AgentOutput = Join-Path $WorkRoot 'agent-output'
$NativeOutput = Join-Path $WorkRoot 'native-output'
$NugetCache = Join-Path $WorkRoot 'nuget-packages'
$DotnetHome = Join-Path $WorkRoot 'dotnet-home'
$LogFile = Join-Path $OutputRoot 'COMPILACAO-v14.log'
$ProjectAgent = Join-Path $ProjectRoot 'tools\TurboRamaPixAgent\TurboRamaPixAgent.csproj'
$EditorSource = Join-Path $ProjectRoot 'tools\TurboRamaPixCredentialEditor'
$InstallerSource = Join-Path $ProjectRoot 'tools\TurboRamaCommercialInstaller'
$PackScript = Join-Path $InstallerSource 'Build-TurboRamaPackage.ps1'
$EsBuild = Join-Path ([IO.Path]::GetTempPath()) 'TRPX14-ES'
$EsExe = Join-Path $ProjectRoot 'bin\x64\Release\emulationstation.exe'
$FinalInstaller = Join-Path $OutputRoot 'INSTALAR-TURBORAMA-PIX-COMERCIAL-v14.exe'
$FinalEditor = Join-Path $OutputRoot 'CONFIGURAR-ACCESS-TOKEN-PIX.exe'

function Write-Stage([string]$Message) {
    Write-Host ''
    Write-Host ('=' * 68) -ForegroundColor Cyan
    Write-Host "  $Message" -ForegroundColor Cyan
    Write-Host ('=' * 68) -ForegroundColor Cyan
    Add-Content -LiteralPath $LogFile -Value "`r`n=== $Message ===" -Encoding UTF8
}

function Write-Ok([string]$Message) {
    Write-Host "[OK] $Message" -ForegroundColor Green
    Add-Content -LiteralPath $LogFile -Value "[OK] $Message" -Encoding UTF8
}

function Write-InfoLine([string]$Message) {
    Write-Host "[INFO] $Message" -ForegroundColor Gray
    Add-Content -LiteralPath $LogFile -Value "[INFO] $Message" -Encoding UTF8
}

function Write-Failure([string]$Message) {
    Write-Host "[ERRO] $Message" -ForegroundColor Red
    if (Test-Path -LiteralPath (Split-Path -Parent $LogFile)) {
        Add-Content -LiteralPath $LogFile -Value "[ERRO] $Message" -Encoding UTF8
    }
}

function Assert-File([string]$Path, [string]$Label) {
    if (-not [IO.File]::Exists((Convert-ToLongPath $Path))) {
        throw "$Label nao encontrado: $Path"
    }
}

function Assert-Directory([string]$Path, [string]$Label) {
    if (-not [IO.Directory]::Exists((Convert-ToLongPath $Path))) {
        throw "$Label nao encontrado: $Path"
    }
}

function Remove-SafeBuildDirectory([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $project = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\') + '\'
    if (-not $full.StartsWith($project, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Limpeza recusada fora do projeto: $full"
    }
    $leaf = Split-Path -Leaf $full
    if ($leaf -notmatch '^(build-pix-commercial-v14|build-pix-commercial-v14-es|GERADO-v14)$') {
        throw "Limpeza recusada para pasta nao reconhecida: $full"
    }
    [IO.Directory]::Delete((Convert-ToLongPath $full), $true)
}

function Remove-ProjectDirectoryTree([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $project = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\') + '\'
    if (-not $full.StartsWith($project, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Exclusao recusada fora do projeto: $full"
    }
    [IO.Directory]::Delete((Convert-ToLongPath $full), $true)
}

function Remove-TemporaryEsBuild {
    if (-not (Test-Path -LiteralPath $EsBuild)) { return }
    $full = [IO.Path]::GetFullPath($EsBuild).TrimEnd('\')
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $full.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $full) -ne 'TRPX14-ES') {
        throw "Limpeza CMake recusada fora do temporario controlado: $full"
    }
    [IO.Directory]::Delete((Convert-ToLongPath $full), $true)
}

function Invoke-Tool {
    param(
        [Parameter(Mandatory=$true)][string]$File,
        [Parameter(Mandatory=$true)][AllowEmptyCollection()][string[]]$Arguments,
        [string]$WorkingDirectory = $ProjectRoot
    )
    Write-InfoLine ("Executando: " + (Split-Path -Leaf $File) + ' ' + ($Arguments -join ' '))
    Push-Location $WorkingDirectory
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $File @Arguments 2>&1 | Tee-Object -FilePath $LogFile -Append | Out-Host
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
        Pop-Location
    }
    if ($code -ne 0) {
        throw "Comando falhou com codigo ${code}: $File"
    }
}

function Convert-ToLongPath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if ($full.StartsWith('\\?\')) { return $full }
    if ($full.StartsWith('\\')) { return '\\?\UNC\' + $full.TrimStart('\') }
    return '\\?\' + $full
}

function Copy-DirectoryContents([string]$Source, [string]$Destination) {
    Assert-Directory $Source 'Pasta de origem para copia'
    $sourceRoot = [IO.Path]::GetFullPath($Source).TrimEnd('\')
    [IO.Directory]::CreateDirectory((Convert-ToLongPath $Destination)) | Out-Null
    Get-ChildItem -LiteralPath $sourceRoot -Recurse -Directory -Force | ForEach-Object {
        $relative = $_.FullName.Substring($sourceRoot.Length).TrimStart('\')
        [IO.Directory]::CreateDirectory((Convert-ToLongPath (Join-Path $Destination $relative))) | Out-Null
    }
    Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Force | ForEach-Object {
        $relative = $_.FullName.Substring($sourceRoot.Length).TrimStart('\')
        $target = Join-Path $Destination $relative
        [IO.Directory]::CreateDirectory((Convert-ToLongPath (Split-Path -Parent $target))) | Out-Null
        $longSource = Convert-ToLongPath $_.FullName
        $longTarget = Convert-ToLongPath $target
        [IO.File]::Copy($longSource, $longTarget, $true)
    }
}

function Find-VsWhere {
    $candidate = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    Assert-File $candidate 'Localizador do Visual Studio (vswhere.exe)'
    return $candidate
}

function Find-VsTool([string]$Pattern) {
    $vswhere = Find-VsWhere
    $found = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find $Pattern 2>$null | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($found) -or -not (Test-Path -LiteralPath $found)) {
        throw "Ferramenta do Visual Studio nao encontrada: $Pattern. Instale Desenvolvimento para Desktop com C++."
    }
    return $found
}

function Import-VisualStudioEnvironment([string]$VsDevCmd) {
    $lines = & $env:ComSpec /s /c "`"$VsDevCmd`" -no_logo -arch=x64 -host_arch=x64 >nul && set"
    if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel preparar o ambiente C++ x64.' }
    $visualStudioPath = $null
    foreach ($line in $lines) {
        $position = $line.IndexOf('=')
        if ($position -le 0) { continue }
        $name = $line.Substring(0, $position)
        $value = $line.Substring($position + 1)
        if ($name -ceq 'PATH') {
            $visualStudioPath = $value
            continue
        }
        if ($name -ieq 'Path') { continue }
        Set-Item -Path "Env:$name" -Value $value
    }
    if ([string]::IsNullOrWhiteSpace($visualStudioPath)) { throw 'PATH do Visual Studio nao foi retornado.' }
    $env:Path = $visualStudioPath
}

function New-EmbeddedTheme {
    $embeddedRoot = Join-Path $ProjectRoot 'embedded-theme'
    Assert-Directory $embeddedRoot 'Pasta do tema embutido'
    $themeDirectory = $embeddedRoot
    if (-not (Test-Path -LiteralPath (Join-Path $themeDirectory 'theme.xml') -PathType Leaf)) {
        $themeDirectory = Get-ChildItem -LiteralPath $embeddedRoot -Directory |
            Sort-Object Name |
            Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'theme.xml') -PathType Leaf } |
            Select-Object -First 1 -ExpandProperty FullName
    }
    if ([string]::IsNullOrWhiteSpace($themeDirectory)) {
        throw "theme.xml nao encontrado em $embeddedRoot"
    }

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $temporaryZip = Join-Path $WorkRoot 'embedded-theme.zip.new'
    $output = Join-Path $ProjectRoot 'es-app\src\embedded_theme.bin'
    if (Test-Path -LiteralPath $temporaryZip) { Remove-Item -LiteralPath $temporaryZip -Force }
    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Force }

    $zipStream = [IO.File]::Open($temporaryZip, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $archive = New-Object IO.Compression.ZipArchive($zipStream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            $files = Get-ChildItem -LiteralPath $themeDirectory -Recurse -File | Sort-Object FullName
            foreach ($file in $files) {
                $relative = $file.FullName.Substring($themeDirectory.Length).TrimStart('\','/') -replace '\\','/'
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $zipStream.Dispose() }

    $key = [byte[]](0xB3,0x57,0x9E,0x24,0xC8,0x6A,0x11,0xFD,0x45,0x8B,0xD2,0x37,0xE9,0x02,0xAC,0x71)
    if (-not ('TurboRamaBuild.XorTransformer' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.IO;
namespace TurboRamaBuild
{
    public static class XorTransformer
    {
        public static void Transform(string inputPath, string outputPath, byte[] key)
        {
            using (var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
            using (var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.SequentialScan))
            {
                var buffer = new byte[1024 * 1024];
                long position = 0;
                int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (int index = 0; index < count; index++)
                        buffer[index] = (byte)(buffer[index] ^ key[(position + index) % key.Length]);
                    output.Write(buffer, 0, count);
                    position += count;
                }
                output.Flush(true);
            }
        }
    }
}
'@
    }
    try {
        [TurboRamaBuild.XorTransformer]::Transform($temporaryZip, $output, $key)
    }
    finally {
        Remove-Item -LiteralPath $temporaryZip -Force -ErrorAction SilentlyContinue
    }
    Assert-File $output 'Tema embutido gerado'
    Write-Ok "Tema embutido gerado pelo PowerShell: $output"
}

function Resolve-Standalone7za([string]$SevenZip) {
    $candidates = @(
        (Join-Path $InstallerSource 'third-party\7za.exe'),
        (Join-Path $BundleRoot '7za.exe'),
        (Join-Path $WorkspaceRoot 'outputs\TURBORAMA-PIX-COMERCIAL-v14\bundle\7za.exe')
    )
    $command = Get-Command 7za.exe -ErrorAction SilentlyContinue
    if ($command) { $candidates += $command.Source }
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return [IO.Path]::GetFullPath($candidate)
        }
    }

    Write-InfoLine '7za.exe independente nao encontrado; baixando o pacote oficial 7-Zip Extra 24.09.'
    $cache = Join-Path $WorkRoot 'third-party-7zip'
    [IO.Directory]::CreateDirectory($cache) | Out-Null
    $download = Join-Path $cache '7z2409-extra.7z'
    if (-not (Test-Path -LiteralPath $download)) {
        Invoke-WebRequest -UseBasicParsing -Uri 'https://www.7-zip.org/a/7z2409-extra.7z' -OutFile $download
    }
    Invoke-Tool -File $SevenZip -Arguments @('x', '-y', "-o$cache", $download) -WorkingDirectory $cache
    $extracted = Get-ChildItem -LiteralPath $cache -Recurse -Filter 7za.exe -File | Select-Object -First 1
    if (-not $extracted) {
        throw '7za.exe nao foi extraido. Instale o 7-Zip ou copie 7za.exe para tools\TurboRamaCommercialInstaller\third-party.'
    }
    return $extracted.FullName
}

function Compile-NativeProgram {
    param(
        [string]$SourceDirectory,
        [string]$BaseName,
        [string[]]$Libraries,
        [string]$Cl,
        [string]$Rc
    )
    $resource = Join-Path $NativeOutput "$BaseName.res"
    $output = Join-Path $NativeOutput "$BaseName.exe"
    Invoke-Tool -File $Rc -Arguments @('/nologo', "/fo$resource", "$BaseName.rc") -WorkingDirectory $SourceDirectory
    $link = @('user32.lib') + $Libraries + @('/SUBSYSTEM:WINDOWS', "/OUT:$output")
    $arguments = @('/nologo', '/std:c++17', '/EHsc', '/O2', '/W4', "$BaseName.cpp", $resource, '/link') + $link
    Invoke-Tool -File $Cl -Arguments $arguments -WorkingDirectory $SourceDirectory
    Assert-File $output "$BaseName.exe compilado"
    return $output
}

function Copy-PrivateDotnetRuntime([string]$Destination, [string]$Dotnet) {
    $dotnetRoot = Split-Path -Parent $Dotnet
    $versions = Get-ChildItem -LiteralPath (Join-Path $dotnetRoot 'shared\Microsoft.NETCore.App') -Directory |
        Where-Object { $_.Name -match '^8\.' } |
        Sort-Object { [version]$_.Name } -Descending
    $runtime = $versions | Select-Object -First 1
    if (-not $runtime) { throw '.NET Runtime 8 x64 nao encontrado.' }
    $fxr = Join-Path $dotnetRoot ("host\fxr\" + $runtime.Name)
    Assert-Directory $fxr 'Host FXR do .NET 8'
    [IO.Directory]::CreateDirectory($Destination) | Out-Null
    Copy-Item -LiteralPath $Dotnet -Destination (Join-Path $Destination 'dotnet.exe') -Force
    Copy-DirectoryContents -Source $fxr -Destination (Join-Path $Destination ("host\fxr\" + $runtime.Name))
    Copy-DirectoryContents -Source $runtime.FullName -Destination (Join-Path $Destination ("shared\Microsoft.NETCore.App\" + $runtime.Name))
    Write-Ok "Runtime .NET privado incluido: $($runtime.Name)"
    return $runtime.Name
}

function Test-PackageFooter {
    param(
        [string]$Package,
        [string]$Installer,
        [string]$SevenZip,
        [string]$Payload
    )
    $footerSize = 16 + 4 + (8 * 3) + (32 * 3)
    $stream = [IO.File]::OpenRead($Package)
    try {
        if ($stream.Length -le $footerSize) { throw 'Pacote final menor que o rodape de integridade.' }
        [void]$stream.Seek(-$footerSize, [IO.SeekOrigin]::End)
        $reader = New-Object IO.BinaryReader($stream, [Text.Encoding]::ASCII, $true)
        try {
            $magic = [Text.Encoding]::ASCII.GetString($reader.ReadBytes(16)).TrimEnd([char]0)
            $version = $reader.ReadUInt32()
            $sizes = @($reader.ReadUInt64(), $reader.ReadUInt64(), $reader.ReadUInt64())
            $hashes = @($reader.ReadBytes(32), $reader.ReadBytes(32), $reader.ReadBytes(32))
        }
        finally { $reader.Dispose() }
    }
    finally { $stream.Dispose() }
    if ($magic -ne 'TRPIXV14PACKAGE' -or $version -ne 14) { throw 'Assinatura interna v14 invalida.' }
    $parts = @($Installer, $SevenZip, $Payload)
    for ($index = 0; $index -lt $parts.Count; $index++) {
        $item = Get-Item -LiteralPath $parts[$index]
        if ([uint64]$item.Length -ne $sizes[$index]) { throw "Tamanho interno invalido: $($item.Name)" }
        $actual = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash
        $expected = -join ($hashes[$index] | ForEach-Object { $_.ToString('X2') })
        if ($actual -ne $expected) { throw "SHA-256 interno invalido: $($item.Name)" }
    }
    Write-Ok 'Rodape, tamanhos e tres hashes internos do instalador foram verificados.'
}

try {
    if (-not (Test-Path -LiteralPath $OutputRoot)) { [IO.Directory]::CreateDirectory($OutputRoot) | Out-Null }
    Set-Content -LiteralPath $LogFile -Value "TurboRama PIX Comercial v14 - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Encoding UTF8

    Write-Stage '1/8 - VERIFICAR FERRAMENTAS E FONTES'
    Assert-Directory $ProjectRoot 'Projeto TurboramaEmulationStation'
    Assert-File $ProjectAgent 'Projeto do agente PIX'
    Assert-File $PackScript 'Empacotador v14'
    $dotnet = (Get-Command dotnet.exe -ErrorAction Stop).Source
    $sevenZip = if (Test-Path -LiteralPath 'C:\Program Files\7-Zip\7z.exe') { 'C:\Program Files\7-Zip\7z.exe' } else { (Get-Command 7z.exe -ErrorAction Stop).Source }
    $cmake = Find-VsTool 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
    $ninja = Find-VsTool 'Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe'
    $vsDevCmd = Find-VsTool 'Common7\Tools\VsDevCmd.bat'
    Import-VisualStudioEnvironment $vsDevCmd
    $cl = (Get-Command cl.exe -ErrorAction Stop).Source
    $rc = (Get-Command rc.exe -ErrorAction Stop).Source
    Write-Ok 'Visual Studio C++/CMake, .NET 8 e 7-Zip encontrados.'

    if ($Limpar) {
        Write-Stage 'LIMPEZA SEGURA DOS ARQUIVOS GERADOS'
        Remove-SafeBuildDirectory $WorkRoot
        Remove-TemporaryEsBuild
        Remove-SafeBuildDirectory $OutputRoot
        [IO.Directory]::CreateDirectory($OutputRoot) | Out-Null
        Set-Content -LiteralPath $LogFile -Value "TurboRama PIX Comercial v14 - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Encoding UTF8
    }
    foreach ($directory in @($WorkRoot, $BundleRoot, $ArchiveRoot, $AgentOutput, $NativeOutput, $OutputRoot, $DotnetHome, $NugetCache)) {
        [IO.Directory]::CreateDirectory($directory) | Out-Null
    }

    Write-Stage '2/8 - COMPILAR EMULATIONSTATION x64 RELEASE'
    if ($UsarEmulationStationExistente) {
        Assert-File $EsExe 'EmulationStation existente solicitado'
        Write-InfoLine 'Build do EmulationStation foi reutilizado por opcao de teste.'
    }
    else {
        New-EmbeddedTheme
        $cacheFile = Join-Path $EsBuild 'CMakeCache.txt'
        if (Test-Path -LiteralPath $cacheFile) {
            $expectedHome = 'CMAKE_HOME_DIRECTORY:INTERNAL=' + ($ProjectRoot -replace '\\','/')
            $cacheHome = Get-Content -LiteralPath $cacheFile | Where-Object { $_ -like 'CMAKE_HOME_DIRECTORY:INTERNAL=*' } | Select-Object -First 1
            $cacheGenerator = Get-Content -LiteralPath $cacheFile | Where-Object { $_ -like 'CMAKE_GENERATOR:INTERNAL=*' } | Select-Object -First 1
            if ($cacheHome -ne $expectedHome -or $cacheGenerator -ne 'CMAKE_GENERATOR:INTERNAL=Ninja') {
                Write-InfoLine 'Cache CMake pertence a outra pasta/gerador; recriando o intermediario curto.'
                Remove-TemporaryEsBuild
            }
        }
        Invoke-Tool -File $cmake -Arguments @(
            '-S', $ProjectRoot,
            '-B', $EsBuild,
            '-G', 'Ninja',
            '-DCMAKE_BUILD_TYPE=Release',
            ('-DCMAKE_MAKE_PROGRAM=' + ($ninja -replace '\\','/')),
            ('-DCMAKE_C_COMPILER=' + ($cl -replace '\\','/')),
            ('-DCMAKE_CXX_COMPILER=' + ($cl -replace '\\','/')),
            ('-DCMAKE_RC_COMPILER=' + ($rc -replace '\\','/'))
        )
        $parallel = [Math]::Max(1, [Environment]::ProcessorCount)
        Invoke-Tool -File $cmake -Arguments @('--build', $EsBuild, '--target', 'emulationstation', '--parallel', $parallel.ToString())
        $EsExe = Join-Path $ProjectRoot 'bin\emulationstation.exe'
        Assert-File $EsExe 'emulationstation.exe'
    }
    Write-Ok "EmulationStation pronto: $EsExe"

    Write-Stage '3/8 - COMPILAR E TESTAR AGENTE PIX'
    $env:DOTNET_CLI_HOME = $DotnetHome
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $offlineNuget = Join-Path $WorkspaceRoot 'NUGET-COMMERCIAL'
    $effectiveNuget = if (Test-Path -LiteralPath (Join-Path $offlineNuget 'qrcoder\1.8.0\qrcoder.1.8.0.nupkg')) { $offlineNuget } else { $NugetCache }
    $env:NUGET_PACKAGES = $effectiveNuget
    if ($effectiveNuget -eq $offlineNuget) { Write-InfoLine 'Cache NuGet local encontrado; a compilacao pode continuar mesmo com nuget.org indisponivel.' }
    Invoke-Tool -File $dotnet -Arguments @('restore', $ProjectAgent, '--packages', $effectiveNuget, '--ignore-failed-sources', '-p:NuGetAudit=false')
    Invoke-Tool -File $dotnet -Arguments @('build', $ProjectAgent, '-c', 'Release', '--no-restore', '-o', $AgentOutput, '-p:NuGetAudit=false')
    if (-not (Test-Path -LiteralPath (Join-Path $AgentOutput 'appsettings.json'))) {
        Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $ProjectAgent) 'appsettings.example.json') -Destination (Join-Path $AgentOutput 'appsettings.json') -Force
    }
    $selfTest = Join-Path $WorkRoot 'agent-self-test'
    if (Test-Path -LiteralPath $selfTest) { Remove-ProjectDirectoryTree $selfTest }
    [IO.Directory]::CreateDirectory($selfTest) | Out-Null
    Invoke-Tool -File $dotnet -Arguments @((Join-Path $AgentOutput 'TurboRamaPixAgent.dll'), '--self-test', '--bridge', $selfTest)
    Write-Ok 'Autoteste interno do agente PIX aprovado.'

    Write-Stage '4/8 - COMPILAR EDITOR E INSTALADORES NATIVOS'
    $editorExe = Compile-NativeProgram -SourceDirectory $EditorSource -BaseName 'TurboRamaPixCredentialEditor' -Libraries @('gdi32.lib','crypt32.lib','comdlg32.lib','shell32.lib') -Cl $cl -Rc $rc
    $installerExe = Compile-NativeProgram -SourceDirectory $InstallerSource -BaseName 'TurboRamaInstaller' -Libraries @('shlwapi.lib') -Cl $cl -Rc $rc
    $bootstrapperExe = Compile-NativeProgram -SourceDirectory $InstallerSource -BaseName 'TurboRamaBootstrapper' -Libraries @('bcrypt.lib') -Cl $cl -Rc $rc
    Write-Ok 'Editor externo, instalador e bootstrapper compilados.'

    Write-Stage '5/8 - PREPARAR CONTEUDO SEM CREDENCIAIS'
    if (Test-Path -LiteralPath $ArchiveRoot) { Remove-ProjectDirectoryTree $ArchiveRoot }
    [IO.Directory]::CreateDirectory((Join-Path $ArchiveRoot 'pix-agent')) | Out-Null
    Copy-Item -LiteralPath $EsExe -Destination (Join-Path $ArchiveRoot 'emulationstation.exe') -Force
    Copy-Item -LiteralPath $editorExe -Destination (Join-Path $ArchiveRoot 'CONFIGURAR-ACCESS-TOKEN-PIX.exe') -Force
    Copy-DirectoryContents -Source $AgentOutput -Destination (Join-Path $ArchiveRoot 'pix-agent')
    $runtimeVersion = Copy-PrivateDotnetRuntime -Destination (Join-Path $ArchiveRoot 'pix-agent\runtime') -Dotnet $dotnet
    foreach ($required in @(
        'emulationstation.exe',
        'CONFIGURAR-ACCESS-TOKEN-PIX.exe',
        'pix-agent\TurboRamaPixAgent.dll',
        'pix-agent\TurboRamaPixAgent.runtimeconfig.json',
        'pix-agent\QRCoder.dll',
        'pix-agent\runtime\dotnet.exe',
        ("pix-agent\runtime\shared\Microsoft.NETCore.App\" + $runtimeVersion + '\System.Private.CoreLib.dll')
    )) {
        Assert-File (Join-Path $ArchiveRoot $required) "Conteudo obrigatorio ($required)"
    }
    $stagedFileCount = (Get-ChildItem -LiteralPath $ArchiveRoot -Recurse -File).Count
    if ($stagedFileCount -lt 100) { throw "Conteudo incompleto: somente $stagedFileCount arquivos foram preparados." }
    Write-InfoLine "$stagedFileCount arquivos validados no conteudo do instalador."
    $forbidden = Get-ChildItem -LiteralPath $ArchiveRoot -Recurse -File | Where-Object {
        $_.Name -like 'secret.dat*' -or $_.Name -eq 'bridge.key' -or $_.Name -eq 'owner-settings.json' -or $_.Name -eq '.agent.lock'
    }
    if ($forbidden) { throw 'Empacotamento recusado: foi encontrado arquivo privado no conteudo.' }
    Write-Ok 'Conteudo preparado sem Access Token, cadastro, creditos, ROMs ou temas do cliente.'

    Write-Stage '6/8 - CRIAR PAYLOAD E INSTALADOR EXE UNICO'
    $standalone7za = Resolve-Standalone7za $sevenZip
    $bundled7za = Join-Path $BundleRoot '7za.exe'
    if (-not ([IO.Path]::GetFullPath($standalone7za).Equals([IO.Path]::GetFullPath($bundled7za), [StringComparison]::OrdinalIgnoreCase))) {
        Copy-Item -LiteralPath $standalone7za -Destination $bundled7za -Force
    }
    Copy-Item -LiteralPath $installerExe -Destination (Join-Path $BundleRoot 'TurboRamaInstaller.exe') -Force
    Copy-Item -LiteralPath $bootstrapperExe -Destination (Join-Path $BundleRoot 'TurboRamaBootstrapper.exe') -Force
    $payload = Join-Path $BundleRoot 'payload.7z'
    if (Test-Path -LiteralPath $payload) { Remove-Item -LiteralPath $payload -Force }
    Invoke-Tool -File $sevenZip -Arguments @('a', '-t7z', $payload, '.\*', '-mx=9', '-mmt=on', '-y') -WorkingDirectory $ArchiveRoot
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $PackScript `
        -Bootstrapper (Join-Path $BundleRoot 'TurboRamaBootstrapper.exe') `
        -Installer (Join-Path $BundleRoot 'TurboRamaInstaller.exe') `
        -SevenZip (Join-Path $BundleRoot '7za.exe') `
        -Payload $payload `
        -Output $FinalInstaller
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao montar o instalador EXE unico.' }
    Copy-Item -LiteralPath $editorExe -Destination $FinalEditor -Force

    Write-Stage '7/8 - VERIFICAR INTEGRIDADE E CONTEUDO'
    Test-PackageFooter -Package $FinalInstaller -Installer (Join-Path $BundleRoot 'TurboRamaInstaller.exe') -SevenZip (Join-Path $BundleRoot '7za.exe') -Payload $payload
    $hash = (Get-FileHash -LiteralPath $FinalInstaller -Algorithm SHA256).Hash
    Set-Content -LiteralPath (Join-Path $OutputRoot 'CHECKSUMS-SHA256.txt') -Value "$hash  INSTALAR-TURBORAMA-PIX-COMERCIAL-v14.exe" -Encoding ASCII
    Invoke-Tool -File (Join-Path $BundleRoot '7za.exe') -Arguments @('t', $payload) -WorkingDirectory $BundleRoot
    Write-Ok 'Arquivo 7z interno testado sem erros.'

    if ($TestarInstalador) {
        Write-Stage '8/8 - TESTE ISOLADO DO INSTALADOR'
        $testTarget = Join-Path ([IO.Path]::GetTempPath()) 'TurboRamaPixV14Smoke'
        $testFull = [IO.Path]::GetFullPath($testTarget).TrimEnd('\')
        $tempFull = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
        if (-not $testFull.StartsWith($tempFull, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $testFull) -ne 'TurboRamaPixV14Smoke') {
            throw "Pasta de teste temporaria recusada: $testFull"
        }
        if (Test-Path -LiteralPath $testTarget) { [IO.Directory]::Delete((Convert-ToLongPath $testTarget), $true) }
        [IO.Directory]::CreateDirectory($testTarget) | Out-Null
        Copy-Item -LiteralPath $EsExe -Destination (Join-Path $testTarget 'emulationstation.exe') -Force
        $env:TURBORAMA_INSTALL_TARGET = $testTarget
        $env:TURBORAMA_INSTALLER_SILENT_TEST = '1'
        try {
            Write-InfoLine 'Executando o instalador e aguardando a conclusao...'
            $installProcess = Start-Process -FilePath $FinalInstaller -WorkingDirectory $OutputRoot -Wait -PassThru
            if ($installProcess.ExitCode -ne 0) { throw "Instalador isolado falhou com codigo $($installProcess.ExitCode)." }
        }
        finally {
            Remove-Item Env:TURBORAMA_INSTALL_TARGET -ErrorAction SilentlyContinue
            Remove-Item Env:TURBORAMA_INSTALLER_SILENT_TEST -ErrorAction SilentlyContinue
        }
        foreach ($required in @('emulationstation.exe','CONFIGURAR-ACCESS-TOKEN-PIX.exe','pix-agent\TurboRamaPixAgent.dll','pix-agent\runtime\dotnet.exe','.emulationstation\pix\installation-v14.log')) {
            Assert-File (Join-Path $testTarget $required) "Arquivo instalado ($required)"
        }
        Write-Ok 'Instalacao real em pasta isolada aprovada.'
    }
    else {
        Write-Stage '8/8 - TESTE DE INSTALACAO ISOLADA OPCIONAL'
        Write-InfoLine 'Use -TestarInstalador para extrair e validar uma instalacao completa em pasta de teste.'
    }

    $size = (Get-Item -LiteralPath $FinalInstaller).Length
    $report = @(
        'TURBORAMA PIX COMERCIAL v14 - COMPILACAO APROVADA',
        "Data: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
        "Instalador: $FinalInstaller",
        "Tamanho: $size bytes",
        "SHA256: $hash",
        'Agente PIX: compilado e autoteste aprovado',
        'Editor de Access Token: compilado',
        'Rodape e hashes internos: aprovados',
        'Payload 7z: teste de integridade aprovado',
        "Teste isolado do instalador: $(if ($TestarInstalador) { 'aprovado' } else { 'nao solicitado' })",
        'Credenciais privadas incluidas: NAO'
    )
    Set-Content -LiteralPath (Join-Path $OutputRoot 'RELATORIO-COMPILACAO-v14.txt') -Value $report -Encoding UTF8

    Write-Host ''
    Write-Host 'COMPILACAO CONCLUIDA COM SUCESSO' -ForegroundColor Green
    Write-Host "Instalador: $FinalInstaller" -ForegroundColor White
    Write-Host "Editor:     $FinalEditor" -ForegroundColor White
    Write-Host "SHA-256:    $hash" -ForegroundColor White
    exit 0
}
catch {
    Write-Failure $_.Exception.Message
    if ($_.ScriptStackTrace) { Write-Failure $_.ScriptStackTrace }
    Write-Host "Log: $LogFile" -ForegroundColor Yellow
    if (-not $SemPausa) { Read-Host 'Pressione Enter para fechar' | Out-Null }
    exit 1
}
