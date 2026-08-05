[CmdletBinding()]
param(
    [string]$RepositoryRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}

function Assert-Matches {
    param([string]$Text, [string]$Pattern, [string]$Message)
    if ($Text -notmatch $Pattern) { throw $Message }
}

function Assert-DoesNotMatch {
    param([string]$Text, [string]$Pattern, [string]$Message)
    if ($Text -match $Pattern) { throw $Message }
}

function Assert-ThrowsLike {
    param([scriptblock]$Action, [string]$Pattern, [string]$Message)
    $failed = $false
    try { & $Action | Out-Null }
    catch {
        $failed = $true
        if ($_.Exception.Message -notmatch $Pattern) {
            throw "$Message Erro inesperado: $($_.Exception.Message)"
        }
    }
    if (-not $failed) { throw $Message }
}

function Invoke-GuardedLegacyProcess {
    param(
        [Parameter(Mandatory = $true)] [string]$Path,
        [switch]$Batch
    )

    $start = New-Object Diagnostics.ProcessStartInfo
    if ($Batch) {
        $start.FileName = if ([string]::IsNullOrWhiteSpace($env:ComSpec)) {
            (Get-Command cmd.exe -ErrorAction Stop).Source
        } else { $env:ComSpec }
        $start.Arguments = '/d /s /c ""{0}""' -f $Path.Replace('"', '""')
    }
    else {
        $start.FileName = (Get-Command powershell.exe -ErrorAction Stop).Source
        $start.Arguments = '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "{0}"' -f $Path.Replace('"', '\"')
    }
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    if (-not $process) { throw "Nao foi possivel executar o guard legado: $Path" }
    try {
        $process.StandardInput.Close()
        if (-not $process.WaitForExit(15000)) {
            try { $process.Kill() } catch { }
            [void]$process.WaitForExit(3000)
            throw "Guard legado excedeu o timeout sem encerrar: $Path"
        }
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output = $process.StandardOutput.ReadToEnd() + $process.StandardError.ReadToEnd()
        }
    }
    finally { $process.Dispose() }
}

function Test-MutexExists {
    param([string]$Name)
    $mutex = $null
    try {
        $mutex = [Threading.Mutex]::OpenExisting($Name)
        return $true
    }
    catch [Threading.WaitHandleCannotBeOpenedException] { return $false }
    finally { if ($mutex) { $mutex.Dispose() } }
}

$managerFile = Join-Path $RepositoryRoot 'es-app\src\PixAgentManager.cpp'
$ownerFile = Join-Path $RepositoryRoot 'tools\TurboRamaPixOwnerConfigurator\TurboRamaPixOwnerConfigurator.cpp'
$editorFile = Join-Path $RepositoryRoot 'tools\TurboRamaPixCredentialEditor\TurboRamaPixCredentialEditor.cpp'
$agentFile = Join-Path $RepositoryRoot 'tools\TurboRamaPixAgent\Program.cs'
$productionDirectory = Join-Path $RepositoryRoot 'tools\TurboRamaPixAgent\production'
$manager = Get-Content -LiteralPath $managerFile -Raw -Encoding UTF8
$owner = Get-Content -LiteralPath $ownerFile -Raw -Encoding UTF8
$editor = Get-Content -LiteralPath $editorFile -Raw -Encoding UTF8
$agent = Get-Content -LiteralPath $agentFile -Raw -Encoding UTF8

# O manager nunca pode voltar a descobrir ou encerrar todos os dotnet.exe pelo caminho.
Assert-DoesNotMatch $manager 'CreateToolhelp32Snapshot|Process32(First|Next)|findProcessByExactPath|findExpectedProcess' 'O manager voltou a enumerar processos pelo caminho.'
Assert-DoesNotMatch $editor 'CreateToolhelp32Snapshot|Process32(First|Next)|stopExact\s*\(' 'O editor voltou a possuir a rota que encerrava todo dotnet do runtime privado.'

foreach ($check in @(
    @('daemonSingletonMutex\s*=\s*L"Local\\\\TurboRamaPixAgent-Daemon-v1"', 'Mutex singleton ausente no manager.'),
    @('TurboRamaPixAgent-Daemon-v1-"\s*\+', 'Mutex por PID ausente no manager.'),
    @('processStartFileTimeUtc', 'FILETIME nao participa do status do manager.'),
    @('managerTokenHash', 'Hash do token nao participa do status do manager.'),
    @('CREATE_NO_WINDOW\s*\|\s*CREATE_SUSPENDED\s*\|\s*CREATE_UNICODE_ENVIRONMENT', 'O filho nao e criado suspenso com ambiente Unicode.'),
    @('--daemon --bridge', 'O manager nao solicita explicitamente o modo daemon.'),
    @('lookupDaemon\(launchedStatus,\s*process\.dwProcessId,\s*creationFileTime,\s*tokenHash\)', 'Start nao confirma PID/FILETIME/token do filho.'),
    @('agentIdentityStartupTimeoutMs\s*=\s*90000', 'Handshake do primeiro start nao possui a janela de 90 segundos.'),
    @('writer\.Key\("processStartFileTimeUtc"\)', 'Sentinel do manager nao e dirigido ao FILETIME.'),
    @('terminated\s*&&\s*WaitForSingleObject\(process,\s*3000\)\s*==\s*WAIT_OBJECT_0', 'TerminateProcess pode ser aceito sem WAIT_OBJECT_0.'),
    @('DaemonLookupResult::Unknown', 'Lookup do daemon nao possui estado Unknown.'),
    @('AgentStatusReadResult::Unknown', 'Falha de leitura do status nao e separada de ausencia.'),
    @('IDENTIDADE DO AGENTE INVALIDA', 'Status visual ainda pode anunciar identidade invalida como ativa.')
)) { Assert-Matches $manager $check[0] $check[1] }

foreach ($check in @(
    @('kDaemonSingletonMutex\s*=\s*L"Local\\\\TurboRamaPixAgent-Daemon-v1"', 'Configurador nao valida o mutex singleton.'),
    @('kDaemonIdentityStartupTimeoutMs\s*=\s*90000', 'Configurador nao espera o cold start seguro de 90 segundos.'),
    @('CREATE_NO_WINDOW\s*\|\s*CREATE_SUSPENDED\s*\|\s*CREATE_UNICODE_ENVIRONMENT', 'Configurador nao inicia daemon com handshake suspenso.'),
    @('stopOnlyPixAgent\(bridge,\s*executable,\s*stopError\)', 'Configurador nao exige resultado da parada autenticada.'),
    @('if\s*\(!stopOnlyPixAgent', 'Configurador avanca quando a parada nao foi confirmada.'),
    @('childExitConfirmed', 'Timeout do one-shot do configurador nao propaga confirmacao de saida.'),
    @('TerminateProcess\(process\.hProcess,\s*21\)[\s\S]{0,180}WaitForSingleObject\(process\.hProcess,\s*5000\)\s*==\s*WAIT_OBJECT_0', 'Timeout administrativo nao confirma a saida forcada.')
)) { Assert-Matches $owner $check[0] $check[1] }

Assert-Matches $editor 'kAgentAdministrativeTimeoutMs\s*=\s*90000' 'Editor ainda usa timeout curto para preparar a chave.'
Assert-Matches $editor 'exitConfirmed\s*=\s*terminated\s*&&\s*WaitForSingleObject\(process\.hProcess,\s*5000\)\s*==\s*WAIT_OBJECT_0' 'Editor nao confirma a saida do one-shot em timeout.'

foreach ($check in @(
    @('schemaVersion\s*=\s*2', 'Agente nao publica status schema 2.'),
    @('mode\s*=\s*"daemon"', 'Agente nao publica mode=daemon.'),
    @('processStartFileTimeUtc\s*=\s*identity\.ProcessStartFileTimeUtc', 'Agente nao publica FILETIME da identidade.'),
    @('managerTokenHash\s*=\s*identity\.ManagerTokenHash', 'Agente nao publica o hash do token.'),
    @('SingletonMutexName\s*=\s*@"Local\\TurboRamaPixAgent-Daemon-v1"', 'Agente nao possui mutex singleton.'),
    @('PidMutexPrefix\s*=\s*@"Local\\TurboRamaPixAgent-Daemon-v1-"', 'Agente nao possui mutex por PID.'),
    @('using var heartbeat = daemonIdentity is null \? null', 'One-shot ainda pode publicar agent-status.'),
    @('await using var stopMonitor = daemonIdentity is null \? null', 'One-shot ainda pode consumir o sentinel.'),
    @('ClassifyStopRequest\(payload, identity\)', 'Sentinel nao e comparado com a identidade corrente.'),
    @('payload\.Trim\(\)\.Equals\("installer-update"', 'Compatibilidade do sentinel do instalador foi removida.')
)) { Assert-Matches $agent $check[0] $check[1] }

$statusScriptNames = @(
    'VERIFICAR-PIX-COMERCIAL.ps1',
    'INSTALAR-PIX-COMERCIAL.ps1',
    'CORRIGIR-PASTA-PIX.ps1',
    'CONFIGURAR-MERCADOPAGO-TESTE.ps1'
)
foreach ($scriptName in $statusScriptNames) {
    $scriptPath = Join-Path $productionDirectory $scriptName
    $scriptText = Get-Content -LiteralPath $scriptPath -Raw -Encoding UTF8
    $tokens = $null
    $parseErrors = $null
    $scriptAst = [Management.Automation.Language.Parser]::ParseFile(
        $scriptPath, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -ne 0) {
        throw "$scriptName possui erro de sintaxe: $(($parseErrors.Message -join '; '))"
    }
    Assert-Matches $scriptText 'schemaVersion[\s\S]{0,240}-ne\s*2' "$scriptName nao exige status schema 2."
    Assert-Matches $scriptText 'mode\s+-isnot\s+\[string\][\s\S]{0,100}mode\s+-cne\s+''daemon''' "$scriptName nao exige mode string daemon."
    Assert-Matches $scriptText 'processStartFileTimeUtc' "$scriptName nao valida FILETIME."
    Assert-Matches $scriptText 'managerTokenHash\s+-isnot\s+\[string\][\s\S]{0,100}managerTokenHash\s+-cnotmatch' "$scriptName nao exige hash string estrito."
    Assert-Matches $scriptText 'TurboRamaPixAgent-Daemon-v1' "$scriptName nao abre os mutexes de identidade."

    # Extraia somente o helper e exercite os tipos antes que ele possa consultar
    # qualquer PID, caminho ou mutex do computador de teste.
    $validatorAst = $scriptAst.FindAll({
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -eq 'Assert-PixDaemonStatus'
    }, $true) | Select-Object -First 1
    if (-not $validatorAst) { throw "$scriptName nao define Assert-PixDaemonStatus." }
    Invoke-Expression $validatorAst.Extent.Text
    $validShape = [pscustomobject]@{
        schemaVersion = 2; mode = 'daemon'; processId = 424242
        processStartFileTimeUtc = [long]200; managerTokenHash = ('a' * 64)
        provider = 'mercadopago'; ready = $true; state = 'online'
        updatedAtUnixSeconds = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    }
    $badMode = $validShape.PSObject.Copy()
    $badMode.mode = [object[]]@('daemon')
    Assert-ThrowsLike {
        Assert-PixDaemonStatus -Status $badMode -ExpectedExecutablePaths @('C:\isolado\TurboRamaPixAgent.exe')
    } 'Contrato de identidade' "$scriptName aceitou mode como array."
    $badHash = $validShape.PSObject.Copy()
    $badHash.managerTokenHash = [object[]]@(('a' * 64))
    Assert-ThrowsLike {
        Assert-PixDaemonStatus -Status $badHash -ExpectedExecutablePaths @('C:\isolado\TurboRamaPixAgent.exe')
    } 'Identificador efemero' "$scriptName aceitou managerTokenHash como array."
}

$legacyScriptNames = @(
    'INSTALAR-PIX-COMERCIAL.ps1',
    'CORRIGIR-PASTA-PIX.ps1',
    'CONFIGURAR-MERCADOPAGO-TESTE.ps1'
)
$mutationCommands = @(
    'Start-Process', 'New-Item', 'Copy-Item', 'Set-Content', 'Stop-Process',
    'Register-ScheduledTask', 'Start-ScheduledTask'
)
foreach ($scriptName in $legacyScriptNames) {
    $scriptPath = Join-Path $productionDirectory $scriptName
    $scriptText = Get-Content -LiteralPath $scriptPath -Raw -Encoding UTF8
    $tokens = $null
    $parseErrors = $null
    $scriptAst = [Management.Automation.Language.Parser]::ParseFile(
        $scriptPath, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -ne 0) { throw "$scriptName possui erro de sintaxe no guard." }
    if ($scriptAst.ParamBlock) { throw "$scriptName criou parametro que pode contornar o guard legado." }

    $firstStatement = $scriptAst.EndBlock.Statements | Select-Object -First 1
    if (-not $firstStatement -or $firstStatement.Extent.Text -notmatch '^Write-Host') {
        throw "$scriptName nao inicia pelo aviso do guard legado."
    }
    $topLevelExit = $scriptAst.EndBlock.Statements |
        Where-Object { $_ -is [Management.Automation.Language.ExitStatementAst] } |
        Select-Object -First 1
    if (-not $topLevelExit -or $topLevelExit.Pipeline.Extent.Text -ne '25') {
        throw "$scriptName nao possui exit 25 incondicional no nivel superior."
    }
    $guardPrefix = $scriptText.Substring(0, $topLevelExit.Extent.EndOffset)
    Assert-Matches $guardPrefix 'FLUXO LEGADO DESATIVADO' "$scriptName nao explica que o fluxo antigo foi desativado."
    Assert-Matches $guardPrefix 'INSTALAR-TURBORAMA-PIX-COMERCIAL-v25-ULTRA-FINAL\.exe' "$scriptName nao aponta o instalador atual."
    Assert-Matches $guardPrefix 'CONFIGURAR-USER-TOKEN-PIX\.exe' "$scriptName nao aponta o configurador da kioskUser."
    Assert-Matches $guardPrefix 'Nao use Executar como administrador' "$scriptName nao proibe elevar o configurador."
    Assert-DoesNotMatch $guardPrefix '(?i)\$env:|legacy.{0,20}(enable|override)|bypass.{0,20}legacy' "$scriptName criou override do guard legado."

    $mutationLine = $scriptAst.FindAll({
        param($node)
        $node -is [Management.Automation.Language.CommandAst] -and
            $mutationCommands -contains $node.GetCommandName()
    }, $true) | ForEach-Object { $_.Extent.StartLineNumber } | Measure-Object -Minimum
    if ($mutationLine.Count -eq 0 -or $mutationLine.Minimum -le $topLevelExit.Extent.StartLineNumber) {
        throw "$scriptName pode elevar ou alterar o sistema antes do guard."
    }
    $credentialLine = $scriptAst.FindAll({
        param($node)
        $node -is [Management.Automation.Language.CommandAst] -and
            $node.Extent.Text -match '(--set-token|--mercadopago-setup|--check-provider)'
    }, $true) | ForEach-Object { $_.Extent.StartLineNumber } | Measure-Object -Minimum
    if ($credentialLine.Count -eq 0 -or $credentialLine.Minimum -le $topLevelExit.Extent.StartLineNumber) {
        throw "$scriptName pode acessar credencial antes do guard."
    }

    foreach ($target in @(
        @{ Path = $scriptPath; Batch = $false },
        @{ Path = [IO.Path]::ChangeExtension($scriptPath, '.cmd'); Batch = $true }
    )) {
        $result = Invoke-GuardedLegacyProcess -Path $target.Path -Batch:([bool]$target.Batch)
        if ($result.ExitCode -ne 25) {
            throw "$([IO.Path]::GetFileName($target.Path)) retornou $($result.ExitCode), esperado 25."
        }
        Assert-Matches $result.Output 'FLUXO LEGADO DESATIVADO' "$([IO.Path]::GetFileName($target.Path)) nao exibiu o guard."
        Assert-Matches $result.Output 'CONFIGURAR-USER-TOKEN-PIX\.exe' "$([IO.Path]::GetFileName($target.Path)) nao exibiu o fluxo atual."
    }
}

# Teste executavel: um modo administrativo usando o mesmo dotnet/DLL nao cria
# mutexes de daemon, nao publica status e nao consome um sentinel dirigido.
$agentProject = Join-Path $RepositoryRoot 'tools\TurboRamaPixAgent\TurboRamaPixAgent.csproj'
$agentDll = Join-Path $RepositoryRoot 'tools\TurboRamaPixAgent\bin\Release\net8.0-windows\TurboRamaPixAgent.dll'
if (-not (Test-Path -LiteralPath $agentDll -PathType Leaf)) {
    & dotnet build $agentProject -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'A compilacao isolada do agente PIX falhou.' }
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('turborama-pix-identity-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $statusFile = Join-Path $testRoot 'agent-status.json'
    $stopFile = Join-Path $testRoot 'agent-stop.request'
    Set-Content -LiteralPath $stopFile -Value '{"schemaVersion":1,"mode":"daemon","processId":999,"processStartFileTimeUtc":1,"managerTokenHash":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}' -NoNewline -Encoding UTF8

    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = (Get-Command dotnet).Source
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Arguments = ('"{0}" --self-test --bridge "{1}"' -f
        $agentDll.Replace('"', '\"'), $testRoot.Replace('"', '\"'))
    $process = [Diagnostics.Process]::Start($start)
    if (-not $process) { throw 'Nao foi possivel iniciar o one-shot de identidade.' }

    $fakeStatus = [ordered]@{
        schemaVersion = 2; mode = 'one-shot'; processId = $process.Id
        processStartFileTimeUtc = 1
        managerTokenHash = ('b' * 64); ready = $true; state = 'online'
        updatedAtUnixSeconds = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    } | ConvertTo-Json -Compress
    Set-Content -LiteralPath $statusFile -Value $fakeStatus -NoNewline -Encoding UTF8
    $statusHash = (Get-FileHash -LiteralPath $statusFile -Algorithm SHA256).Hash
    $stopHash = (Get-FileHash -LiteralPath $stopFile -Algorithm SHA256).Hash
    $pidMutex = "Local\TurboRamaPixAgent-Daemon-v1-$($process.Id)"
    $singletonExistedBefore = Test-MutexExists 'Local\TurboRamaPixAgent-Daemon-v1'
    while (-not $process.HasExited) {
        if (Test-MutexExists $pidMutex) { throw 'Um one-shot criou o mutex reservado ao daemon.' }
        if (-not $singletonExistedBefore -and (Test-MutexExists 'Local\TurboRamaPixAgent-Daemon-v1')) {
            throw 'Um one-shot criou o mutex singleton reservado ao daemon.'
        }
        Start-Sleep -Milliseconds 10
    }
    $process.WaitForExit()
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    if ($process.ExitCode -ne 0) { throw "Self-test do agente falhou ($($process.ExitCode)): $stderr" }
    if ($stdout -notmatch 'SELF-TEST PIX: OK') { throw 'Self-test do agente nao confirmou sucesso.' }
    if ((Get-FileHash -LiteralPath $statusFile -Algorithm SHA256).Hash -ne $statusHash) { throw 'One-shot alterou agent-status.json.' }
    if ((Get-FileHash -LiteralPath $stopFile -Algorithm SHA256).Hash -ne $stopHash) { throw 'One-shot consumiu ou alterou o sentinel dirigido.' }
    if (Test-MutexExists $pidMutex) { throw 'Mutex por PID do one-shot permaneceu publicado.' }

    $savedErrorPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & dotnet $agentDll --daemon --once 2>&1 | Out-Null
    $invalidModeExitCode = $LASTEXITCODE
    $ErrorActionPreference = $savedErrorPreference
    if ($invalidModeExitCode -ne 9) { throw '--daemon combinado com --once nao falhou fechado com codigo 9.' }
}
finally {
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}

Write-Host 'PIX_DAEMON_IDENTITY_TEST=OK'
