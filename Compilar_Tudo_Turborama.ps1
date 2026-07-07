#Requires -Version 5.1
param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [switch]$SkipClean,

    [switch]$NoPause
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = [System.IO.Path]::GetFullPath($Root).Trim()

$Projects = [ordered]@{
    TurboRama = @{
        Name       = "TurboRama Launcher"
        Solution   = Join-Path $Root "TURBORAMA BINARIOS EXE\RetroBat.sln"
        ProjectDir = Join-Path $Root "TURBORAMA BINARIOS EXE\RetroBat"
        ExeName    = "TurboRama.exe"
        OutputRel  = "bin\$Configuration"
    }
    InstallerHost = @{
        Name       = "InstallerHost"
        Solution   = Join-Path $Root "TURBORAMA INSTALER HOST\InstallerHost.sln"
        ProjectDir = Join-Path $Root "TURBORAMA INSTALER HOST\InstallerHost"
        ExeName    = "InstallerHost.exe"
        OutputRel  = "bin\$Configuration"
    }
    RetroBuild = @{
        Name       = "RetroBuild"
        Solution   = Join-Path $Root "TURBORTAMA RETROBUILDER\RetroBuild.sln"
        ProjectDir = Join-Path $Root "TURBORTAMA RETROBUILDER\RetroBuild"
        ExeName    = "RetroBuild.exe"
        OutputRel  = "bin\$Configuration"
    }
}

function Write-Step([string]$msg) {
    Write-Host ""
    Write-Host "================================================" -ForegroundColor Cyan
    Write-Host " $msg" -ForegroundColor Cyan
    Write-Host "================================================" -ForegroundColor Cyan
}

function Write-Ok([string]$msg) {
    Write-Host "[OK] $msg" -ForegroundColor Green
}

function Write-Info([string]$msg) {
    Write-Host "[INFO] $msg" -ForegroundColor Yellow
}

function Write-Err([string]$msg) {
    Write-Host "[ERRO] $msg" -ForegroundColor Red
}

function Get-MsBuildPath {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) {
        throw "vswhere.exe nao encontrado. Instale o Visual Studio com suporte a .NET Framework."
    }

    $msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($msbuild) -or -not (Test-Path $msbuild)) {
        throw "MSBuild.exe nao encontrado."
    }

    return $msbuild
}

function Stop-LockingProcesses {
    foreach ($name in @("InstallerHost", "RetroBuild", "TurboRama", "devenv", "MSBuild")) {
        Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
            try {
                Write-Info "Fechando processo: $($_.ProcessName) (PID $($_.Id))"
                Stop-Process -Id $_.Id -Force -ErrorAction Stop
            }
            catch {
                Write-Info "Nao foi possivel fechar $($_.ProcessName): $($_.Exception.Message)"
            }
        }
    }
    Start-Sleep -Seconds 1
}

function Remove-SafePath([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) {
        return
    }

    try {
        if ((Get-Item -LiteralPath $path) -is [System.IO.DirectoryInfo]) {
            Get-ChildItem -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object {
                if (-not $_.PSIsContainer) {
                    $_.Attributes = "Normal"
                }
            }
            Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop
        }
        else {
            $item = Get-Item -LiteralPath $path -Force
            $item.Attributes = "Normal"
            Remove-Item -LiteralPath $path -Force -ErrorAction Stop
        }
        Write-Ok "Removido: $path"
    }
    catch {
        Write-Info "Nao removido (em uso?): $path"
    }
}

function Clear-ProjectBuildArtifacts([hashtable]$project) {
    $dirs = @(
        (Join-Path $project.ProjectDir "bin"),
        (Join-Path $project.ProjectDir "obj"),
        (Join-Path $project.ProjectDir "bind")
    )

    foreach ($dir in $dirs) {
        Remove-SafePath $dir
    }

    $solutionDir = Split-Path $project.Solution -Parent
    Remove-SafePath (Join-Path $solutionDir ".vs")
}

function Clear-RetroBuildOutputs {
    $retroDir = $Projects.RetroBuild.ProjectDir
    $patterns = @("turborama-v*.zip", "TurboRama-v*-setup.exe", "*-setup.exe.pkg.*", "*.sha256.txt", "InstallerHost.exe")

    foreach ($pattern in $patterns) {
        Get-ChildItem -LiteralPath $retroDir -Filter $pattern -File -ErrorAction SilentlyContinue | ForEach-Object {
            Remove-SafePath $_.FullName
        }
    }

    Get-ChildItem -LiteralPath $retroDir -Filter "*.pkg.*" -File -ErrorAction SilentlyContinue | ForEach-Object {
        Remove-SafePath $_.FullName
    }
}

function Clear-InternetBlocks([string]$basePath) {
    if (-not (Test-Path $basePath)) {
        return
    }

    Get-ChildItem -LiteralPath $basePath -Recurse -File -Force -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            Unblock-File -LiteralPath $_.FullName -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $_.FullName -Stream Zone.Identifier -ErrorAction SilentlyContinue
        }
        catch {
        }
    }
}

function Invoke-ProjectBuild([string]$msbuild, [hashtable]$project) {
    if (-not (Test-Path $project.Solution)) {
        throw "Solution nao encontrada: $($project.Solution)"
    }

    Write-Step "Compilando $($project.Name)"

    $args = @(
        $project.Solution,
        "/t:Rebuild",
        "/p:Configuration=$Configuration",
        "/p:Platform=Any CPU",
        "/m",
        "/v:minimal",
        "/nologo"
    )

    & $msbuild @args 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao compilar $($project.Name). Codigo: $LASTEXITCODE"
    }

    $outputExe = Join-Path $project.ProjectDir (Join-Path $project.OutputRel $project.ExeName)
    if (-not (Test-Path -LiteralPath $outputExe)) {
        throw "Executavel nao encontrado apos build: $outputExe"
    }

    Write-Ok "$($project.ExeName) gerado em: $outputExe"
}

function New-TurboRama7z {
    $sevenZip = "C:\Program Files\7-Zip\7z.exe"
    if (-not (Test-Path -LiteralPath $sevenZip)) {
        Write-Info "7-Zip nao encontrado. TurboRama.7z nao foi atualizado."
        return
    }

    $turboRamaExe = Join-Path $Root "TURBORAMA BINARIOS EXE\RetroBat\bin\$Configuration\TurboRama.exe"
    if (-not (Test-Path -LiteralPath $turboRamaExe)) {
        throw "TurboRama.exe nao encontrado para empacotar: $turboRamaExe"
    }
    $targetDir = [System.IO.Path]::GetFullPath((Join-Path (Join-Path $Root "TURBORAMA BINARIOS") "TurboramaBinarios"))
    if (-not (Test-Path -LiteralPath $targetDir)) {
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    }

    $archive = Join-Path $targetDir "TurboRama.7z"
    if (Test-Path -LiteralPath $archive) {
        Remove-Item -LiteralPath $archive -Force
    }

    $tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("turborama7z_" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
    $tempExe = Join-Path $tempDir "TurboRama.exe"
    Copy-Item -LiteralPath $turboRamaExe -Destination $tempExe -Force

    try {
        $processInfo = New-Object System.Diagnostics.ProcessStartInfo
        $processInfo.FileName = $sevenZip
        $processInfo.WorkingDirectory = $tempDir
        $processInfo.Arguments = 'a -t7z "' + $archive + '" TurboRama.exe -mx=9 -y'
        $processInfo.UseShellExecute = $false
        $processInfo.CreateNoWindow = $true

        $process = [System.Diagnostics.Process]::Start($processInfo)
        $process.WaitForExit()

        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $archive)) {
            throw "Falha ao criar TurboRama.7z (codigo 7z: $($process.ExitCode))"
        }
    }
    finally {
        Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Ok "TurboRama.7z atualizado em: $archive"
}

function Copy-InstallerHostToRetroBuild {
    $installerHostExe = Join-Path $Root "TURBORAMA INSTALER HOST\InstallerHost\bin\$Configuration\InstallerHost.exe"
    if (-not (Test-Path -LiteralPath $installerHostExe)) {
        throw "InstallerHost.exe nao encontrado para copiar: $installerHostExe"
    }

    $destinations = @(
        (Join-Path $Root "TURBORTAMA RETROBUILDER\RetroBuild\bin\$Configuration\InstallerHost.exe"),
        (Join-Path $Root "TURBORTAMA RETROBUILDER\RetroBuild\InstallerHost.exe")
    )

    foreach ($dest in $destinations) {
        $destDir = Split-Path $dest -Parent
        if (-not (Test-Path -LiteralPath $destDir)) {
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        }
        Copy-Item -LiteralPath $installerHostExe -Destination $dest -Force
        Write-Ok "InstallerHost.exe copiado para: $dest"
    }
}

try {
    Write-Step "TURBORAMA - COMPILACAO AUTOMATICA COMPLETA"
    Write-Host "Raiz: $Root"
    Write-Host "Configuracao: $Configuration"

    $msbuild = Get-MsBuildPath
    Write-Ok "MSBuild: $msbuild"

    if (-not $SkipClean) {
        Write-Step "Limpando compilacoes antigas"
        Stop-LockingProcesses

        foreach ($entry in $Projects.GetEnumerator()) {
            Write-Info "Limpando $($entry.Value.Name)..."
            Clear-ProjectBuildArtifacts $entry.Value
            Clear-InternetBlocks $entry.Value.ProjectDir
        }

        Clear-RetroBuildOutputs
        Write-Ok "Limpeza concluida."
    }
    else {
        Write-Info "Limpeza ignorada (-SkipClean)."
    }

    Invoke-ProjectBuild $msbuild $Projects.TurboRama

    $prereqScript = Join-Path $Root "TURBORAMA INSTALER HOST\Baixar_Prerequisitos_Instalador.ps1"
    $prereqMarker = Join-Path $Root "TURBORAMA INSTALER HOST\InstallerHost\resources\prerequisites\vc_redist.x64.exe"
    if (-not (Test-Path -LiteralPath $prereqMarker)) {
        Write-Info "Pre-requisitos do instalador nao encontrados. Baixando pacote comercial..."
        & powershell -NoProfile -ExecutionPolicy Bypass -File $prereqScript
    }
    else {
        Write-Ok "Pre-requisitos do instalador ja presentes."
    }

    Invoke-ProjectBuild $msbuild $Projects.InstallerHost
    Invoke-ProjectBuild $msbuild $Projects.RetroBuild

    Write-Step "Pos-processamento"
    Write-Info "Gerando TurboRama.7z..."
    New-TurboRama7z
    Write-Info "Copiando InstallerHost.exe para RetroBuild..."
    Copy-InstallerHostToRetroBuild

    Write-Step "COMPILACAO FINALIZADA COM SUCESSO"
    Write-Host ""
    Write-Host "Executaveis gerados:" -ForegroundColor Green
    Write-Host "  TurboRama.exe      -> $(Join-Path $Root 'TURBORAMA BINARIOS EXE\RetroBat\bin\Release\TurboRama.exe')"
    Write-Host "  InstallerHost.exe  -> $(Join-Path $Root 'TURBORAMA INSTALER HOST\InstallerHost\bin\Release\InstallerHost.exe')"
    Write-Host "  RetroBuild.exe     -> $(Join-Path $Root 'TURBORTAMA RETROBUILDER\RetroBuild\bin\Release\RetroBuild.exe')"
    Write-Host "  TurboRama.7z       -> $(Join-Path $Root 'TURBORAMA BINARIOS\TurboramaBinarios\TurboRama.7z')"
    Write-Host ""
    Write-Host "Proximo passo (manual no RetroBuild.exe):" -ForegroundColor Yellow
    Write-Host "  1 - Download and configure"
    Write-Host "  2 - Create archive"
    Write-Host "  3 - Create installer"
    Write-Host ""
}
catch {
    Write-Err $_.Exception.Message
    if ($_.InvocationInfo) {
        Write-Err ("Linha: " + $_.InvocationInfo.ScriptLineNumber + " -> " + $_.InvocationInfo.Line.Trim())
    }
    if (-not $NoPause) {
        Read-Host "Pressione Enter para sair"
    }
    exit 1
}

if (-not $NoPause) {
    Read-Host "Pressione Enter para sair"
}