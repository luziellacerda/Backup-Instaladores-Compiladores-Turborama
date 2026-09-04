#Requires -Version 5.1
<#
.SYNOPSIS
    TurboRama EmulationStation - Programa de compilacao completo.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Configuracao
# ---------------------------------------------------------------------------
$Script:Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Script:BuildDir = Join-Path $Root 'build'
$Script:OutputExe = Join-Path $Root 'bin\x64\Release\emulationstation.exe'
$Script:TmpDir = Join-Path $BuildDir 'tmp'
$Script:ThemeXml = Join-Path $Root 'embedded-theme\TURBORAMA\theme.xml'
$Script:EmbeddedThemeBin = Join-Path $BuildDir 'es-app\generated\embedded_theme.bin'
$Script:PackScript = Join-Path $Root 'tools\Pack-EmbeddedTheme.ps1'
$Script:LogFile = Join-Path $BuildDir 'compilar.log'
$Script:TurboPcDest = 'G:\TURBOPCINSTALL\build\sistema\emulationstation\emulationstation.exe'

$Script:Steps = @(
    [pscustomobject]@{ Id = 1; Name = 'Verificar ambiente';      Weight = 8  }
    [pscustomobject]@{ Id = 2; Name = 'Limpar cache';            Weight = 12 }
    [pscustomobject]@{ Id = 3; Name = 'Configurar CMake';        Weight = 15 }
    [pscustomobject]@{ Id = 4; Name = 'Empacotar tema embutido'; Weight = 20 }
    [pscustomobject]@{ Id = 5; Name = 'Compilar executavel';     Weight = 45 }
)

$Script:StartTime = Get-Date
$Script:CompletedWeight = 0
$Script:TotalWeight = 0
foreach ($step in $Steps) { $Script:TotalWeight += $step.Weight }

# ---------------------------------------------------------------------------
# Utilitarios de interface
# ---------------------------------------------------------------------------
function Initialize-Console {
    try {
        $host.UI.RawUI.WindowTitle = 'TurboRama EmulationStation - Compilar'
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    }
    catch {
        # Ignorar se o host nao suportar.
    }
}

function Write-Log {
    param(
        [string]$Message,
        [ValidateSet('INFO', 'WARN', 'ERROR', 'OK')]
        [string]$Level = 'INFO'
    )

    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $line = "[$timestamp] [$Level] $Message"

    $logDir = Split-Path $LogFile -Parent
    if (-not (Test-Path $logDir)) {
        New-Item -ItemType Directory -Path $logDir -Force | Out-Null
    }
    Add-Content -Path $LogFile -Value $line -Encoding UTF8
}

function Show-Banner {
    Clear-Host
    Write-Host ''
    Write-Host '  ============================================================' -ForegroundColor Cyan
    Write-Host '       TurboRama EmulationStation - Compilador v2.0' -ForegroundColor Cyan
    Write-Host '  ============================================================' -ForegroundColor Cyan
    Write-Host ''
    Write-Host "  Projeto : $Root"
    Write-Host "  Saida   : $OutputExe"
    Write-Host "  Log     : $LogFile"
    Write-Host ''
}

function Update-BuildProgress {
    param(
        [int]$StepId,
        [int]$StepPercent = 100,
        [string]$Activity = '',
        [string]$Status = ''
    )

    $step = $Steps | Where-Object { $_.Id -eq $StepId } | Select-Object -First 1
    if (-not $step) { return }

    $priorWeight = 0
    foreach ($priorStep in $Steps) {
        if ($priorStep.Id -lt $StepId) { $priorWeight += $priorStep.Weight }
    }

    $currentWeight = [math]::Round(($step.Weight * ($StepPercent / 100.0)), 2)
    $overall = [math]::Min(100, [math]::Round((($priorWeight + $currentWeight) / $TotalWeight) * 100))

    $stepLabel = "Etapa $($step.Id)/$($Steps.Count) - $($step.Name)"
    if ($Activity) { $stepLabel = "$stepLabel | $Activity" }
    if (-not $Status) { $Status = "$overall% concluido" }

    Write-Progress -Id 0 -Activity 'TurboRama Build' -Status $Status -PercentComplete $overall
    Write-Progress -Id 1 -ParentId 0 -Activity $stepLabel -Status $Status -PercentComplete $StepPercent
}

function Complete-BuildProgress {
    Write-Progress -Id 1 -Activity 'Concluido' -Completed
    Write-Progress -Id 0 -Activity 'Concluido' -Completed
}

function Write-StepHeader {
    param(
        [int]$StepId,
        [string]$Detail = ''
    )

    $step = $Steps | Where-Object { $_.Id -eq $StepId } | Select-Object -First 1
    Write-Host ''
    Write-Host "  [$($step.Id)/$($Steps.Count)] $($step.Name)" -ForegroundColor Yellow
    if ($Detail) {
        Write-Host "        $Detail" -ForegroundColor DarkGray
    }
}

function Write-StepOk {
    param([string]$Message)
    Write-Host "        [OK] $Message" -ForegroundColor Green
    Write-Log $Message 'OK'
}

function Write-StepInfo {
    param([string]$Message)
    Write-Host "        $Message" -ForegroundColor Gray
    Write-Log $Message 'INFO'
}

function Write-StepWarn {
    param([string]$Message)
    Write-Host "        [AVISO] $Message" -ForegroundColor DarkYellow
    Write-Log $Message 'WARN'
}

function Write-StepError {
    param([string]$Message)
    Write-Host "        [ERRO] $Message" -ForegroundColor Red
    Write-Log $Message 'ERROR'
}

function Format-FileSize {
    param([long]$Bytes)

    if ($Bytes -ge 1GB) { return '{0:N2} GB' -f ($Bytes / 1GB) }
    if ($Bytes -ge 1MB) { return '{0:N2} MB' -f ($Bytes / 1MB) }
    if ($Bytes -ge 1KB) { return '{0:N2} KB' -f ($Bytes / 1KB) }
    return "$Bytes bytes"
}

function Get-FolderSize {
    param([string]$Path)

    if (-not (Test-Path $Path)) { return 0 }

    $size = (Get-ChildItem -Path $Path -Recurse -Force -File -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum

    if ($null -eq $size) { return 0 }
    return [long]$size
}

function Format-ProcessArgument {
    param([string]$Value)

    if ($Value -match '[\s"]') {
        return '"' + ($Value -replace '"', '""') + '"'
    }
    return $Value
}

function Invoke-BuildCommand {
    param(
        [string]$Command,
        [string[]]$Arguments,
        [int]$StepId,
        [scriptblock]$OnLine = $null,
        [string]$WorkingDirectory = $Root
    )

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $Command
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $psi.Arguments = ($Arguments | ForEach-Object { Format-ProcessArgument $_ }) -join ' '

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $psi
    [void]$process.Start()

    $stdout = $process.StandardOutput
    $stderr = $process.StandardError

    while (-not $process.HasExited) {
        while (-not $stdout.EndOfStream) {
            $line = $stdout.ReadLine()
            if ($line) {
                Write-Log $line 'INFO'
                if ($OnLine) { & $OnLine $line }
            }
        }
        Start-Sleep -Milliseconds 80
    }

    while (-not $stdout.EndOfStream) {
        $line = $stdout.ReadLine()
        if ($line) {
            Write-Log $line 'INFO'
            if ($OnLine) { & $OnLine $line }
        }
    }

    $errorText = $stderr.ReadToEnd()
    if ($errorText) {
        foreach ($line in ($errorText -split "`r?`n")) {
            if ($line.Trim()) { Write-Log $line 'WARN' }
        }
    }

    $process.WaitForExit()
    return $process.ExitCode
}

function Find-VsWhereTool {
    param([string]$FindPattern)

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) { return $null }

    $result = & $vswhere -latest -requires Microsoft.Component.MSBuild -find $FindPattern 2>$null |
        Select-Object -First 1

    if ($result -and (Test-Path $result)) { return $result }
    return $null
}

function Resolve-BuildTools {
    $tools = [ordered]@{
        CMake  = $null
        MSBuild = $null
    }

    $tools.CMake = Find-VsWhereTool 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
    if (-not $tools.CMake) {
        $cmakeCmd = Get-Command cmake -ErrorAction SilentlyContinue
        if ($cmakeCmd) { $tools.CMake = $cmakeCmd.Source }
    }

    $tools.MSBuild = Find-VsWhereTool 'MSBuild\Current\Bin\MSBuild.exe'
    if (-not $tools.MSBuild) {
        $candidates = @(
            Join-Path $env:ProgramFiles 'Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
            Join-Path $env:ProgramFiles 'Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe'
        )
        foreach ($candidate in $candidates) {
            if (Test-Path $candidate) {
                $tools.MSBuild = $candidate
                break
            }
        }
    }

    return $tools
}

function Test-BuildEnvironment {
    param($Tools)

    Update-BuildProgress -StepId 1 -StepPercent 10 -Activity 'Checando arquivos do projeto'
    if (-not (Test-Path $ThemeXml)) {
        throw "Tema nao encontrado: $ThemeXml"
    }

    $themeInfo = Get-Item $ThemeXml
    Write-StepOk "Tema encontrado: $($themeInfo.DirectoryName)"
    Write-StepInfo "theme.xml atualizado em $($themeInfo.LastWriteTime.ToString('dd/MM/yyyy HH:mm:ss'))"
    Write-StepInfo 'Coloque sempre o tema novo em: embedded-theme\TURBORAMA\'

    Update-BuildProgress -StepId 1 -StepPercent 35 -Activity 'Checando empacotador local do tema'
    if (-not (Test-Path -LiteralPath $PackScript -PathType Leaf)) {
        throw "Empacotador local do tema nao encontrado: $PackScript"
    }
    Write-StepOk 'Empacotador do tema: PowerShell/.NET (Python nao necessario)'

    Update-BuildProgress -StepId 1 -StepPercent 60 -Activity 'Checando CMake'
    if (-not $Tools.CMake) {
        throw 'CMake nao encontrado. Instale Visual Studio com suporte a CMake/C++.'
    }
    Write-StepOk "CMake: $($Tools.CMake)"

    Update-BuildProgress -StepId 1 -StepPercent 85 -Activity 'Checando MSBuild'
    if (-not $Tools.MSBuild) {
        throw 'MSBuild nao encontrado. Instale Visual Studio com "Desenvolvimento para C++".'
    }
    Write-StepOk "MSBuild: $($Tools.MSBuild)"

    Update-BuildProgress -StepId 1 -StepPercent 100 -Activity 'Ambiente validado'
}

function Get-RuntimeCachePaths {
    $paths = @(
        Join-Path $env:USERPROFILE '.emulationstation\.runtime'
    )

    $extraRoots = @(
        'G:\TURBOPCINSTALL'
        'G:\emulationstation'
        'D:\emulationstation'
    )

    foreach ($extraRoot in $extraRoots) {
        if (-not (Test-Path $extraRoot)) { continue }
        $paths += Join-Path $extraRoot '.emulationstation\.runtime'
        $paths += Join-Path $extraRoot 'configs\emulationstation\.runtime'
        $paths += Join-Path $extraRoot 'system\configs\emulationstation\.runtime'
    }

    return $paths | Select-Object -Unique
}

function Clear-BuildCache {
    $targets = @(
        [pscustomobject]@{ Path = $BuildDir; Label = 'build'; IsFile = $false }
        [pscustomobject]@{ Path = (Join-Path $Root 'bin'); Label = 'bin'; IsFile = $false }
        [pscustomobject]@{ Path = $EmbeddedThemeBin; Label = 'embedded_theme.bin'; IsFile = $true }
    )

    $runtimeCaches = Get-RuntimeCachePaths | Where-Object { Test-Path $_ }
    foreach ($cachePath in $runtimeCaches) {
        $targets += [pscustomobject]@{ Path = $cachePath; Label = "runtime cache ($cachePath)"; IsFile = $false }
    }

    $removed = 0
    $total = $targets.Count
    $index = 0

    foreach ($target in $targets) {
        $index++
        $percent = [math]::Round(($index / $total) * 100)
        Update-BuildProgress -StepId 2 -StepPercent $percent -Activity "Removendo $($target.Label)"

        if (-not (Test-Path $target.Path)) {
            Write-StepInfo "$($target.Label) - ja ausente"
            continue
        }

        if ($target.IsFile) {
            $size = (Get-Item $target.Path).Length
            Remove-Item $target.Path -Force
            Write-StepOk "$($target.Label) removido ($(Format-FileSize $size))"
        }
        else {
            $size = Get-FolderSize $target.Path
            Remove-Item $target.Path -Recurse -Force
            Write-StepOk "$($target.Label) removido ($(Format-FileSize $size))"
        }
        $removed++
    }

    if ($removed -eq 0) {
        Write-StepInfo 'Nenhum cache antigo encontrado'
    }
}

function Invoke-CMakeConfigure {
    param(
        [string]$CMakePath
    )

    $cmakeProgress = 5
    $onLine = {
        param($Line)
        $script:cmakeProgress = [math]::Min(95, $script:cmakeProgress + 2)
        Update-BuildProgress -StepId 3 -StepPercent $script:cmakeProgress -Activity $Line
    }.GetNewClosure()

    $exitCode = Invoke-BuildCommand -Command $CMakePath -Arguments @(
        '-S', $Root,
        '-B', $BuildDir,
        '-A', 'x64',
        '-DTURBORAMA_ENABLE_COMMERCIAL_SERVICES=OFF'
    ) -StepId 3 -OnLine $onLine

    if ($exitCode -ne 0) {
        throw "Falha na configuracao CMake (codigo $exitCode). Veja o log: $LogFile"
    }

    Update-BuildProgress -StepId 3 -StepPercent 100 -Activity 'CMake configurado'
    Write-StepOk 'Projeto configurado para x64 Release'
}

function Invoke-PackTheme {
    Update-BuildProgress -StepId 4 -StepPercent 15 -Activity 'Lendo arquivos do tema'

    $powerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    if (-not (Test-Path -LiteralPath $powerShell -PathType Leaf)) {
        throw 'Windows PowerShell 5.1 nao foi encontrado para empacotar o tema.'
    }
    $exitCode = Invoke-BuildCommand -Command $powerShell -Arguments @(
        '-NoLogo','-NoProfile','-ExecutionPolicy','Bypass','-File',$PackScript,
        '-Source',(Join-Path $Root 'embedded-theme'),'-Output',$EmbeddedThemeBin
    ) -StepId 4 -OnLine {
        param($Line)
        if ($Line -match 'Packed|Output|files') {
            Update-BuildProgress -StepId 4 -StepPercent 70 -Activity $Line
        }
    }

    if ($exitCode -ne 0) {
        throw "Falha ao empacotar o tema (codigo $exitCode)."
    }

    if (-not (Test-Path $EmbeddedThemeBin)) {
        throw 'Arquivo embedded_theme.bin nao foi gerado.'
    }

    $size = (Get-Item $EmbeddedThemeBin).Length
    Update-BuildProgress -StepId 4 -StepPercent 100 -Activity 'Tema empacotado'
    Write-StepOk "embedded_theme.bin gerado ($(Format-FileSize $size))"
    Write-StepInfo 'O tema embutido sera extraido do .exe na primeira execucao'
}

function Invoke-MsBuildCompile {
    param(
        [string]$MsBuildPath
    )

    if (-not (Test-Path $TmpDir)) {
        New-Item -ItemType Directory -Path $TmpDir -Force | Out-Null
    }

    $env:TMP = $TmpDir
    $env:TEMP = $TmpDir
    $env:_CL_ = '/Zm300'

    $projectFile = Join-Path $BuildDir 'es-app\emulationstation.vcxproj'
    if (-not (Test-Path $projectFile)) {
        throw "Projeto nao encontrado: $projectFile"
    }

    $expectedProjects = @(
        'id3v2'
        'libcheevos'
        'nanosvg'
        'pugixml-static'
        'es-core'
        'emulationstation'
    )
    $builtProjects = New-Object 'System.Collections.Generic.HashSet[string]'
    $compilePercent = 5

    $onLine = {
        param($Line)
        if ($Line -match '->') {
            foreach ($project in $expectedProjects) {
                if ($Line -match [regex]::Escape($project) -and $builtProjects.Add($project)) {
                    $count = $builtProjects.Count
                    $total = $expectedProjects.Count
                    $script:compilePercent = [math]::Min(98, [math]::Round(5 + (($count / $total) * 90)))
                    Update-BuildProgress -StepId 5 -StepPercent $script:compilePercent -Activity "$project compilado ($count/$total)"
                    Write-StepInfo "$project -> OK"
                }
            }
        }
        elseif ($Line -match 'error (C|LNK)|: error ') {
            Write-StepError $Line
        }
    }.GetNewClosure()

    Write-StepInfo 'Compilacao em andamento. Esta etapa pode levar varios minutos...'

    $exitCode = Invoke-BuildCommand -Command $MsBuildPath -Arguments @(
        $projectFile,
        '/p:Configuration=Release',
        '/p:Platform=x64',
        '/m:1',
        '/v:minimal'
    ) -StepId 5 -OnLine $onLine

    if ($exitCode -ne 0) {
        throw "Compilacao falhou (codigo $exitCode). Veja o log: $LogFile"
    }

    if (-not (Test-Path $OutputExe)) {
        throw "Executavel nao encontrado apos compilacao: $OutputExe"
    }

    Update-BuildProgress -StepId 5 -StepPercent 100 -Activity 'Compilacao finalizada'
    Write-StepOk 'emulationstation.exe gerado com sucesso'
}

function Show-Summary {
    param(
        [bool]$Success
    )

    Complete-BuildProgress

    $elapsed = (Get-Date) - $StartTime
    $elapsedText = if ($elapsed.TotalHours -ge 1) {
        $elapsed.ToString('h\h\ m\m\ s\s')
    }
    else {
        $elapsed.ToString('m\m\ s\s')
    }

    Write-Host ''
    Write-Host '  ============================================================' -ForegroundColor Cyan

    if ($Success) {
        Write-Host '                    COMPILACAO CONCLUIDA' -ForegroundColor Green
        Write-Host '  ============================================================' -ForegroundColor Cyan
        Write-Host ''
        Write-Host '  Executavel:' -ForegroundColor White
        Write-Host "    $OutputExe" -ForegroundColor Gray

        $exe = Get-Item $OutputExe
        Write-Host ''
        Write-Host "  Tamanho : $(Format-FileSize $exe.Length)"
        Write-Host "  Data    : $($exe.LastWriteTime.ToString('dd/MM/yyyy HH:mm:ss'))"
        Write-Host "  Duracao : $elapsedText"
        Write-Host "  Log     : $LogFile"
        Write-Host ''

        $copy = Read-Host '  Copiar para TurboPC (G:\TURBOPCINSTALL\build\sistema\emulationstation)? [S/N]'
        if ($copy -match '^(s|S)$') {
            try {
                $destDir = Split-Path $TurboPcDest -Parent
                if (-not (Test-Path $destDir)) {
                    throw "Pasta de destino nao encontrada: $destDir"
                }
                Copy-Item -Path $OutputExe -Destination $TurboPcDest -Force
                Write-Host ''
                Write-StepOk 'Copiado para TurboPC com sucesso'
            }
            catch {
                Write-StepWarn "Nao foi possivel copiar para TurboPC: $($_.Exception.Message)"
            }
        }
    }
    else {
        Write-Host '                    COMPILACAO FALHOU' -ForegroundColor Red
        Write-Host '  ============================================================' -ForegroundColor Cyan
        Write-Host ''
        Write-Host "  Duracao : $elapsedText"
        Write-Host "  Log     : $LogFile"
        Write-Host ''
        Write-Host '  Verifique o log para detalhes do erro.' -ForegroundColor DarkYellow
    }

    Write-Host ''
    Read-Host '  Pressione Enter para sair'
}

# ---------------------------------------------------------------------------
# Fluxo principal
# ---------------------------------------------------------------------------
function Start-Build {
    Initialize-Console
    Show-Banner

    $logDir = Split-Path $LogFile -Parent
    if (Test-Path $logDir) {
        Remove-Item $LogFile -Force -ErrorAction SilentlyContinue
    }
    Write-Log 'Inicio da compilacao' 'INFO'

    $tools = Resolve-BuildTools

    try {
        Write-StepHeader -StepId 1 -Detail 'Validando dependencias e ferramentas'
        Test-BuildEnvironment -Tools $tools

        Write-StepHeader -StepId 2 -Detail 'Removendo cache para evitar falhas'
        Clear-BuildCache

        Write-StepHeader -StepId 3 -Detail 'Gerando arquivos de build (x64)'
        Invoke-CMakeConfigure -CMakePath $tools.CMake

        Write-StepHeader -StepId 4 -Detail 'Criando embedded_theme.bin'
        Invoke-PackTheme

        Write-StepHeader -StepId 5 -Detail 'Gerando emulationstation.exe (Release x64)'
        Invoke-MsBuildCompile -MsBuildPath $tools.MSBuild

        Show-Summary -Success $true
        exit 0
    }
    catch {
        Write-StepError $_.Exception.Message
        Write-Log $_.Exception.Message 'ERROR'
        if ($_.ScriptStackTrace) {
            Write-Log $_.ScriptStackTrace 'ERROR'
        }
        Show-Summary -Success $false
        exit 1
    }
}

Start-Build
