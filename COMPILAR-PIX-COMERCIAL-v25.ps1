#Requires -Version 5.1
param(
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
$WorkRoot = Join-Path $WorkspaceRoot 'work\TRPX25-PACK'
$EsBuild = Join-Path $WorkspaceRoot 'work\TRPX25-ES'
$AgentOutput = Join-Path $WorkRoot 'agent-output'
$NativeOutput = Join-Path $WorkRoot 'native-output'
$ArchiveRoot = Join-Path $WorkRoot 'archive-update'
$BundleRoot = Join-Path $WorkRoot 'bundle'
$OutputRoot = Join-Path $ProjectRoot 'PIX-COMERCIAL\GERADO-v25'
$AgentProject = Join-Path $ProjectRoot 'tools\TurboRamaPixAgent\TurboRamaPixAgent.csproj'
$InstallerSource = Join-Path $ProjectRoot 'tools\TurboRamaCommercialInstaller'
$PackScript = Join-Path $InstallerSource 'Build-TurboRamaPackage.ps1'
$FinalInstaller = Join-Path $OutputRoot 'INSTALAR-TURBORAMA-PIX-COMERCIAL-v25-ULTRA-FINAL.exe'
$LogFile = Join-Path $OutputRoot 'COMPILACAO-v25.log'

function Stage([string]$Text) {
    Write-Host "`n====================================================================" -ForegroundColor Cyan
    Write-Host "  $Text" -ForegroundColor Cyan
    Write-Host '====================================================================' -ForegroundColor Cyan
    Add-Content -LiteralPath $LogFile -Value "`r`n=== $Text ===" -Encoding UTF8
}

function Require-File([string]$Path, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Label nao encontrado: $Path" }
}

function Require-Directory([string]$Path, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { throw "$Label nao encontrado: $Path" }
}

function Assert-GeneratedPath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $allowed = @(
        ([IO.Path]::GetFullPath($WorkRoot).TrimEnd('\')),
        ([IO.Path]::GetFullPath($EsBuild).TrimEnd('\')),
        ([IO.Path]::GetFullPath($OutputRoot).TrimEnd('\'))
    )
    if (-not ($allowed -contains $full)) { throw "Limpeza recusada fora das pastas geradas: $full" }
}

function Reset-GeneratedDirectory([string]$Path) {
    Assert-GeneratedPath $Path
    if (Test-Path -LiteralPath $Path) { [IO.Directory]::Delete([IO.Path]::GetFullPath($Path), $true) }
    [IO.Directory]::CreateDirectory($Path) | Out-Null
}

function Run([string]$File, [string[]]$Arguments, [string]$Directory = $ProjectRoot) {
    Add-Content -LiteralPath $LogFile -Value ("Executando: " + (Split-Path -Leaf $File) + ' ' + ($Arguments -join ' ')) -Encoding UTF8
    Push-Location $Directory
    $previousErrorAction = $ErrorActionPreference
    try {
        # Ferramentas nativas como CMake escrevem mensagens informativas no
        # stderr mesmo quando retornam sucesso. No Windows PowerShell 5.1,
        # ErrorActionPreference=Stop transformava essas linhas em excecao e
        # interrompia uma compilacao valida antes de ler o exit code real.
        $ErrorActionPreference = 'Continue'
        & $File @Arguments 2>&1 | Tee-Object -FilePath $LogFile -Append | Out-Host
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorAction
        Pop-Location
    }
    if ($code -ne 0) { throw "Comando retornou codigo ${code}: $File" }
}

function Find-VsTool([string]$Pattern) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    Require-File $vswhere 'vswhere.exe'
    $tool = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find $Pattern 2>$null | Select-Object -First 1
    Require-File $tool "Ferramenta Visual Studio ($Pattern)"
    return $tool
}

function Import-VsEnvironment([string]$VsDevCmd) {
    $lines = & $env:ComSpec /s /c "`"$VsDevCmd`" -no_logo -arch=x64 -host_arch=x64 >nul && set"
    if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel preparar o compilador C++ x64.' }
    $vsPath = $null
    foreach ($line in $lines) {
        $separator = $line.IndexOf('=')
        if ($separator -le 0) { continue }
        $name = $line.Substring(0, $separator)
        $value = $line.Substring($separator + 1)
        if ($name -ceq 'PATH') { $vsPath = $value; continue }
        if ($name -ieq 'Path') { continue }
        Set-Item -Path "Env:$name" -Value $value
    }
    if ([string]::IsNullOrWhiteSpace($vsPath)) { throw 'PATH do Visual Studio nao foi retornado.' }
    $env:Path = $vsPath
}

function Copy-Tree([string]$Source, [string]$Destination) {
    Require-Directory $Source 'Pasta para copia'
    [IO.Directory]::CreateDirectory($Destination) | Out-Null
    & robocopy.exe $Source $Destination /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Falha ao copiar $Source" }
}

function Compile-Native([string]$SourceDirectory, [string]$BaseName, [string]$OutputName, [string[]]$Libraries) {
    $resource = Join-Path $NativeOutput ($BaseName + '.res')
    $object = Join-Path $NativeOutput ($BaseName + '.obj')
    $output = Join-Path $NativeOutput $OutputName
    Run $script:Rc @('/nologo', "/fo$resource", ($BaseName + '.rc')) $SourceDirectory
    $arguments = @('/nologo','/std:c++17','/utf-8','/EHsc','/O2','/W4',"/Fo:$object","/Fe:$output",($BaseName + '.cpp'),$resource) + $Libraries + @('/link','/SUBSYSTEM:WINDOWS')
    Run $script:Cl $arguments $SourceDirectory
    Require-File $output $OutputName
    return $output
}

function Copy-PrivateDotnet([string]$Dotnet, [string]$Destination) {
    $root = Split-Path -Parent $Dotnet
    $runtime = Get-ChildItem -LiteralPath (Join-Path $root 'shared\Microsoft.NETCore.App') -Directory |
        Where-Object Name -match '^8\.' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if (-not $runtime) { throw '.NET Runtime 8 x64 nao encontrado.' }
    $fxr = Join-Path $root ('host\fxr\' + $runtime.Name)
    [IO.Directory]::CreateDirectory($Destination) | Out-Null
    Copy-Item -LiteralPath $Dotnet -Destination (Join-Path $Destination 'dotnet.exe') -Force
    Copy-Tree $fxr (Join-Path $Destination ('host\fxr\' + $runtime.Name))
    Copy-Tree $runtime.FullName (Join-Path $Destination ('shared\Microsoft.NETCore.App\' + $runtime.Name))
}

function Resolve-Standalone7za([string]$SevenZip) {
    $vendored = Join-Path $InstallerSource 'vendor\7za.exe'
    if (Test-Path -LiteralPath $vendored -PathType Leaf) { return $vendored }
    $candidate = Get-ChildItem -LiteralPath $ProjectRoot -Recurse -Filter 7za.exe -File -ErrorAction SilentlyContinue |
        Where-Object FullName -like '*build-pix-commercial*\bundle\7za.exe' | Select-Object -First 1
    if ($candidate) { return $candidate.FullName }
    $cache = Join-Path $WorkRoot '7zip-extra'
    [IO.Directory]::CreateDirectory($cache) | Out-Null
    $download = Join-Path $cache '7z2409-extra.7z'
    if (-not (Test-Path -LiteralPath $download)) {
        Invoke-WebRequest -UseBasicParsing -Uri 'https://www.7-zip.org/a/7z2409-extra.7z' -OutFile $download
    }
    Run $SevenZip @('x','-y',"-o$cache",$download) $cache
    $candidate = Get-ChildItem -LiteralPath $cache -Recurse -Filter 7za.exe -File | Select-Object -First 1
    if (-not $candidate) { throw '7za.exe independente nao foi localizado.' }
    return $candidate.FullName
}

try {
    Require-Directory $ProjectRoot 'Projeto TurboRama'
    Require-File $AgentProject 'Projeto do agente PIX'
    Require-File $PackScript 'Empacotador comercial'
    [IO.Directory]::CreateDirectory($OutputRoot) | Out-Null
    Set-Content -LiteralPath $LogFile -Value "TurboRama PIX v25 - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Encoding UTF8

    Stage '1/8 - FERRAMENTAS'
    $dotnet = (Get-Command dotnet.exe -ErrorAction Stop).Source
    $sevenZip = if (Test-Path -LiteralPath 'C:\Program Files\7-Zip\7z.exe') { 'C:\Program Files\7-Zip\7z.exe' } else { (Get-Command 7z.exe -ErrorAction Stop).Source }
    $cmake = Find-VsTool 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
    $ninja = Find-VsTool 'Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe'
    $vsDevCmd = Find-VsTool 'Common7\Tools\VsDevCmd.bat'
    Import-VsEnvironment $vsDevCmd
    $script:Cl = (Get-Command cl.exe -ErrorAction Stop).Source
    $script:Rc = (Get-Command rc.exe -ErrorAction Stop).Source

    if ($Limpar) {
        Reset-GeneratedDirectory $WorkRoot
        Reset-GeneratedDirectory $EsBuild
        Reset-GeneratedDirectory $OutputRoot
        Set-Content -LiteralPath $LogFile -Value "TurboRama PIX v25 - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Encoding UTF8
    }
    foreach ($directory in @($WorkRoot,$AgentOutput,$NativeOutput,$ArchiveRoot,$BundleRoot,$OutputRoot,$EsBuild)) { [IO.Directory]::CreateDirectory($directory) | Out-Null }

    Stage '2/8 - EMULATIONSTATION'
    Run $cmake @('-S',$ProjectRoot,'-B',$EsBuild,'-G','Ninja','-DCMAKE_BUILD_TYPE=Release',('-DCMAKE_MAKE_PROGRAM=' + ($ninja -replace '\\','/')),('-DCMAKE_C_COMPILER=' + ($script:Cl -replace '\\','/')),('-DCMAKE_CXX_COMPILER=' + ($script:Cl -replace '\\','/')),('-DCMAKE_RC_COMPILER=' + ($script:Rc -replace '\\','/')))
    Run $cmake @('--build',$EsBuild,'--target','emulationstation','--parallel',([Math]::Max(1,[Environment]::ProcessorCount).ToString()))
    $esExe = Join-Path $ProjectRoot 'bin\emulationstation.exe'
    Require-File $esExe 'emulationstation.exe'

    Stage '3/8 - AGENTE PIX E AUTOTESTES'
    $env:DOTNET_CLI_HOME = Join-Path $WorkRoot 'dotnet-home'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $offlineNuget = Join-Path $WorkspaceRoot 'NUGET-COMMERCIAL'
    if (Test-Path -LiteralPath $offlineNuget) { $env:NUGET_PACKAGES = $offlineNuget }
    Run $dotnet @('restore',$AgentProject,'--ignore-failed-sources','-p:NuGetAudit=false')
    Run $dotnet @('build',$AgentProject,'-c','Release','--no-restore','-o',$AgentOutput,'-p:NuGetAudit=false')
    if (-not (Test-Path -LiteralPath (Join-Path $AgentOutput 'appsettings.json'))) {
        Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $AgentProject) 'appsettings.example.json') -Destination (Join-Path $AgentOutput 'appsettings.json') -Force
    }
    Run $dotnet @((Join-Path $AgentOutput 'TurboRamaPixAgent.dll'),'--self-test','--bridge',(Join-Path $WorkRoot 'agent-self-test'))

    Stage '4/8 - PROGRAMAS WINDOWS LZ GAMES'
    $ownerConfigurator = Compile-Native (Join-Path $ProjectRoot 'tools\TurboRamaPixOwnerConfigurator') 'TurboRamaPixOwnerConfigurator' 'CONFIGURAR-USER-TOKEN-PIX.exe' @('user32.lib','gdi32.lib','shell32.lib','comctl32.lib','advapi32.lib')
    $credentialEditor = Compile-Native (Join-Path $ProjectRoot 'tools\TurboRamaPixCredentialEditor') 'TurboRamaPixCredentialEditor' 'CONFIGURAR-ACCESS-TOKEN-PIX.exe' @('user32.lib','gdi32.lib','crypt32.lib','comdlg32.lib','shell32.lib')
    $installer = Compile-Native $InstallerSource 'TurboRamaInstaller' 'TurboRamaInstaller.exe' @('user32.lib','shlwapi.lib')
    $bootstrapper = Compile-Native $InstallerSource 'TurboRamaBootstrapper' 'TurboRamaBootstrapper.exe' @('user32.lib','bcrypt.lib')
    $guiTest = Start-Process -FilePath $ownerConfigurator -ArgumentList '--self-test' -Wait -PassThru
    if ($guiTest.ExitCode -ne 0) { throw "Autoteste do configurador retornou $($guiTest.ExitCode)." }
    $credentialTest = Start-Process -FilePath $credentialEditor -ArgumentList '--self-test' -Wait -PassThru
    if ($credentialTest.ExitCode -ne 0) { throw "Autoteste do editor de credencial retornou $($credentialTest.ExitCode)." }

    Stage '5/8 - CONTEUDO SEM DADOS PRIVADOS'
    if (Test-Path -LiteralPath $ArchiveRoot) { [IO.Directory]::Delete($ArchiveRoot, $true) }
    [IO.Directory]::CreateDirectory((Join-Path $ArchiveRoot 'pix-agent')) | Out-Null
    Copy-Item -LiteralPath $esExe -Destination (Join-Path $ArchiveRoot 'emulationstation.exe') -Force
    Copy-Item -LiteralPath $ownerConfigurator -Destination (Join-Path $ArchiveRoot 'CONFIGURAR-USER-TOKEN-PIX.exe') -Force
    Copy-Item -LiteralPath $credentialEditor -Destination (Join-Path $ArchiveRoot 'CONFIGURAR-ACCESS-TOKEN-PIX.exe') -Force
    Copy-Tree $AgentOutput (Join-Path $ArchiveRoot 'pix-agent')
    Copy-PrivateDotnet $dotnet (Join-Path $ArchiveRoot 'pix-agent\runtime')
    $forbidden = Get-ChildItem -LiteralPath $ArchiveRoot -Recurse -File | Where-Object { $_.Name -like 'secret.dat*' -or $_.Name -in @('bridge.key','owner-settings.json','.agent.lock') }
    if ($forbidden) { throw 'Arquivo privado encontrado no pacote. Empacotamento cancelado.' }
    foreach ($required in @('emulationstation.exe','CONFIGURAR-USER-TOKEN-PIX.exe','CONFIGURAR-ACCESS-TOKEN-PIX.exe','pix-agent\TurboRamaPixAgent.dll','pix-agent\runtime\dotnet.exe')) {
        Require-File (Join-Path $ArchiveRoot $required) "Conteudo obrigatorio ($required)"
    }

    Stage '6/8 - INSTALADOR UNICO'
    $standalone7za = Resolve-Standalone7za $sevenZip
    Copy-Item -LiteralPath $standalone7za -Destination (Join-Path $BundleRoot '7za.exe') -Force
    Copy-Item -LiteralPath $installer -Destination (Join-Path $BundleRoot 'TurboRamaInstaller.exe') -Force
    Copy-Item -LiteralPath $bootstrapper -Destination (Join-Path $BundleRoot 'TurboRamaBootstrapper.exe') -Force
    $payload = Join-Path $BundleRoot 'payload-v25.7z'
    if (Test-Path -LiteralPath $payload) { [IO.File]::Delete($payload) }
    Run $sevenZip @('a','-t7z',$payload,'.\*','-mx=9','-mmt=on','-y') $ArchiveRoot
    Run powershell.exe @('-NoLogo','-NoProfile','-ExecutionPolicy','Bypass','-File',$PackScript,'-Bootstrapper',(Join-Path $BundleRoot 'TurboRamaBootstrapper.exe'),'-Installer',(Join-Path $BundleRoot 'TurboRamaInstaller.exe'),'-SevenZip',(Join-Path $BundleRoot '7za.exe'),'-Payload',$payload,'-Output',$FinalInstaller) $OutputRoot
    Copy-Item -LiteralPath $ownerConfigurator -Destination (Join-Path $OutputRoot 'CONFIGURAR-USER-TOKEN-PIX.exe') -Force
    Copy-Item -LiteralPath $credentialEditor -Destination (Join-Path $OutputRoot 'CONFIGURAR-ACCESS-TOKEN-PIX.exe') -Force

    Stage '7/8 - INTEGRIDADE'
    Run (Join-Path $BundleRoot '7za.exe') @('t',$payload) $BundleRoot
    $hash = (Get-FileHash -LiteralPath $FinalInstaller -Algorithm SHA256).Hash
    $checksumFiles = @(
        $FinalInstaller,
        (Join-Path $OutputRoot 'CONFIGURAR-USER-TOKEN-PIX.exe'),
        (Join-Path $OutputRoot 'CONFIGURAR-ACCESS-TOKEN-PIX.exe')
    )
    $checksumLines = foreach ($checksumFile in $checksumFiles) {
        "$(Get-FileHash -LiteralPath $checksumFile -Algorithm SHA256 | Select-Object -ExpandProperty Hash)  $(Split-Path -Leaf $checksumFile)"
    }
    Set-Content -LiteralPath (Join-Path $OutputRoot 'CHECKSUMS-SHA256.txt') -Value $checksumLines -Encoding ASCII
    $instructions = @'
TURBORAMA / LZ GAMES - CONFIGURAÇÃO COMERCIAL PIX v25

INSTALAÇÃO
1. Execute INSTALAR-TURBORAMA-PIX-COMERCIAL-v25-ULTRA-FINAL.exe.
2. Abra D:\emulationstation\CONFIGURAR-USER-TOKEN-PIX.exe.
3. Escolha Mercado Pago ou Outro banco / Adaptador.
4. Para Mercado Pago, cole somente o Access Token. Não use Public Key,
   Client ID, Client Secret nem ID da aplicação no lugar do Access Token.
5. Informe estabelecimento, caixa, CEP, número, referência e preços.
6. Clique em VALIDAR E ATIVAR PIX.

CEP E ENDEREÇO
- O proprietário informa somente CEP e número/complemento.
- O programa consulta fontes redundantes para obter rua, cidade, estado e a
  localização exigida internamente pela API do Mercado Pago.
- Não existe campo de latitude/longitude para o usuário preencher.
- O endereço confirmado fica em cache; falhas temporárias podem ser retomadas
  sem digitar novamente o cadastro.
- Uma localização recusada pelo provedor não é reutilizada indefinidamente.
- OpenStreetMap/Nominatim é usado somente como último recurso, com cache e
  limite de requisições. Uma instância própria pode ser configurada pela
  variável TURBORAMA_PIX_NOMINATIM_BASE_URL.

CONTA, LOJA E PDV
- O User ID real é identificado pelo próprio Access Token.
- Loja e PDV existentes são reaproveitados sem duplicação.
- Ao trocar de conta ou mudar de teste para produção, o programa prepara Loja
  e PDV vinculados à nova credencial; recursos do sandbox não servem em produção.

SEGURANÇA
- O Access Token é protegido pelo Windows e não entra no instalador ou JSON.
- Os autotestes são locais e simulados; não criam cobrança nem movimentam dinheiro.
- Credenciais que já foram publicadas devem ser revogadas e substituídas.

DOCUMENTAÇÃO OFICIAL CONSULTADA
https://www.mercadopago.com.br/developers/pt/docs/qr-code/create-store-and-pos
https://www.mercadopago.com.br/developers/pt/docs/qr-code/go-to-production
https://docs.awesomeapi.com.br/api-cep/api-busca-de-enderecos
https://operations.osmfoundation.org/policies/nominatim/
'@
    Set-Content -LiteralPath (Join-Path $OutputRoot 'COMO-CONFIGURAR-O-PIX.txt') -Value $instructions -Encoding UTF8

    Stage '8/8 - INSTALACAO ISOLADA'
    if ($TestarInstalador) {
        $smoke = Join-Path $WorkRoot 'smoke-install'
        if (Test-Path -LiteralPath $smoke) { [IO.Directory]::Delete($smoke, $true) }
        [IO.Directory]::CreateDirectory($smoke) | Out-Null
        Copy-Item -LiteralPath $esExe -Destination (Join-Path $smoke 'emulationstation.exe') -Force
        $env:TURBORAMA_INSTALL_TARGET = $smoke
        $env:TURBORAMA_INSTALLER_SILENT_TEST = '1'
        try {
            $process = Start-Process -FilePath $FinalInstaller -WorkingDirectory $OutputRoot -Wait -PassThru
            if ($process.ExitCode -ne 0) { throw "Instalador retornou $($process.ExitCode)." }
        }
        finally {
            Remove-Item Env:TURBORAMA_INSTALL_TARGET -ErrorAction SilentlyContinue
            Remove-Item Env:TURBORAMA_INSTALLER_SILENT_TEST -ErrorAction SilentlyContinue
        }
        foreach ($required in @('emulationstation.exe','CONFIGURAR-USER-TOKEN-PIX.exe','CONFIGURAR-ACCESS-TOKEN-PIX.exe','pix-agent\TurboRamaPixAgent.dll','pix-agent\runtime\dotnet.exe','.emulationstation\pix\installation-v16.log')) {
            Require-File (Join-Path $smoke $required) "Arquivo instalado ($required)"
        }
        $installedTest = Start-Process -FilePath (Join-Path $smoke 'CONFIGURAR-USER-TOKEN-PIX.exe') -ArgumentList '--self-test' -Wait -PassThru
        if ($installedTest.ExitCode -ne 0) { throw 'Autoteste do configurador instalado falhou.' }
        $installedCredentialTest = Start-Process -FilePath (Join-Path $smoke 'CONFIGURAR-ACCESS-TOKEN-PIX.exe') -ArgumentList '--self-test' -Wait -PassThru
        if ($installedCredentialTest.ExitCode -ne 0) { throw 'Autoteste do editor de credencial instalado falhou.' }
        $installedBridge = Join-Path $smoke '.emulationstation\pix\self-test-isolado'
        Run (Join-Path $smoke 'pix-agent\runtime\dotnet.exe') @((Join-Path $smoke 'pix-agent\TurboRamaPixAgent.dll'),'--self-test','--bridge',$installedBridge) $smoke
    }
    else { Write-Host 'Use -TestarInstalador para validar uma instalacao completa.' -ForegroundColor Yellow }

    $report = @(
        'TURBORAMA PIX COMERCIAL v25 - COMPILACAO APROVADA',
        "Data: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
        "Instalador: $FinalInstaller",
        "SHA256: $hash",
        'Configurador de proprietario: compilado e testado',
        'Editor seguro de Access Token: compilado e testado',
        'User ID: reconhecido automaticamente pelo Access Token',
        'Loja e PDV: criacao/reaproveitamento idempotente testado',
        'Mercado Pago e adaptador bancario: contratos testados',
        'CEP: endereco e coordenadas reais resolvidos automaticamente por fontes redundantes e cache',
        'Testes de cobranca: somente simuladores locais; nenhuma cobranca real foi criada',
        'Credenciais privadas incluidas: NAO'
    )
    Set-Content -LiteralPath (Join-Path $OutputRoot 'RELATORIO-COMPILACAO-v25.txt') -Value $report -Encoding UTF8
    Write-Host "`nPRONTO: $FinalInstaller" -ForegroundColor Green
    Write-Host "SHA-256: $hash" -ForegroundColor White
    exit 0
}
catch {
    Write-Host "`nERRO: $($_.Exception.Message)" -ForegroundColor Red
    Add-Content -LiteralPath $LogFile -Value "ERRO: $($_.Exception.Message)`r`n$($_.ScriptStackTrace)" -Encoding UTF8 -ErrorAction SilentlyContinue
    Write-Host "Log: $LogFile" -ForegroundColor Yellow
    if (-not $SemPausa) { Read-Host 'Pressione ENTER para fechar' | Out-Null }
    exit 1
}
