#Requires -Version 5.1

# Fluxo historico preservado abaixo somente para referencia. O agente atual
# pertence a sessao kioskUser e recusa configuracao executada por um processo
# elevado; por isso este script antigo deve falhar antes de qualquer alteracao.
Write-Host ''
Write-Host 'FLUXO LEGADO DESATIVADO.' -ForegroundColor Red
Write-Host 'Use INSTALAR-TURBORAMA-PIX-COMERCIAL-v25-ULTRA-FINAL.exe elevado.'
Write-Host 'Depois, dentro da sessao kioskUser/Arcade, execute CONFIGURAR-USER-TOKEN-PIX.exe.'
Write-Host 'Nao use Executar como administrador no CONFIGURAR-USER-TOKEN-PIX.exe.' -ForegroundColor Yellow
if ([Environment]::UserInteractive -and $Host.Name -eq 'ConsoleHost' -and
    -not [Console]::IsInputRedirected -and -not [Console]::IsOutputRedirected) {
    Read-Host 'Pressione ENTER para fechar' | Out-Null
}
exit 25

$ErrorActionPreference = 'Stop'

function Wait-Operator {
    Write-Host ''
    Read-Host 'Pressione ENTER para fechar'
}

function Assert-PixDaemonStatus {
    param(
        [Parameter(Mandatory = $true)] $Status,
        [Parameter(Mandatory = $true)] [string[]]$ExpectedExecutablePaths,
        [int]$HeartbeatMaxAgeSeconds = 120
    )
    foreach ($field in @('schemaVersion','mode','processId','processStartFileTimeUtc','managerTokenHash','provider','ready','state','updatedAtUnixSeconds')) {
        if (-not $Status.PSObject.Properties[$field]) { throw "agent-status.json nao possui o campo obrigatorio '$field'." }
    }
    if (($Status.schemaVersion -isnot [int] -and $Status.schemaVersion -isnot [long]) -or
        [long]$Status.schemaVersion -ne 2 -or $Status.mode -isnot [string] -or
        [string]$Status.mode -cne 'daemon') {
        throw 'Contrato de identidade do daemon em agent-status.json e invalido.'
    }
    if (($Status.processId -isnot [int] -and $Status.processId -isnot [long]) -or
        [long]$Status.processId -le 0 -or [long]$Status.processId -gt [int]::MaxValue) {
        throw 'PID do daemon em agent-status.json e invalido.'
    }
    if (($Status.processStartFileTimeUtc -isnot [int] -and $Status.processStartFileTimeUtc -isnot [long]) -or
        [long]$Status.processStartFileTimeUtc -le 0) {
        throw 'Instante de criacao do daemon em agent-status.json e invalido.'
    }
    if ($Status.managerTokenHash -isnot [string] -or
        [string]$Status.managerTokenHash -cnotmatch '^[0-9a-f]{64}$') {
        throw 'Identificador efemero do daemon em agent-status.json e invalido.'
    }
    if ($Status.provider -isnot [string] -or
        ([string]$Status.provider -cne 'mercadopago' -and [string]$Status.provider -cne 'adapter')) {
        throw 'Provedor do daemon em agent-status.json e invalido.'
    }
    if ($Status.ready -isnot [bool]) {
        throw 'Campo ready do daemon em agent-status.json e invalido.'
    }
    if ($Status.state -isnot [string] -or
        [string]$Status.state -cnotmatch '^(starting|online|owner_setup_pending|provider_unavailable|stopping)$') {
        throw 'Estado do daemon em agent-status.json e invalido.'
    }
    if ($Status.updatedAtUnixSeconds -isnot [int] -and $Status.updatedAtUnixSeconds -isnot [long]) {
        throw 'Heartbeat do daemon em agent-status.json e invalido.'
    }
    $updated = [DateTimeOffset]::FromUnixTimeSeconds([long]$Status.updatedAtUnixSeconds)
    $age = [DateTimeOffset]::UtcNow - $updated
    if ($age.TotalSeconds -gt $HeartbeatMaxAgeSeconds -or $age.TotalSeconds -lt -30) {
        throw "O heartbeat do daemon PIX esta fora da janela permitida de $HeartbeatMaxAgeSeconds segundos."
    }
    $processId = [int]$Status.processId
    try { $daemonProcess = Get-Process -Id $processId -ErrorAction Stop }
    catch { throw "O processo $processId publicado pelo daemon PIX nao esta em execucao." }
    if ($daemonProcess.HasExited) { throw "O processo $processId publicado pelo daemon PIX ja encerrou." }
    if ([long]($daemonProcess.StartTime.ToUniversalTime().ToFileTimeUtc()) -ne [long]$Status.processStartFileTimeUtc) {
        throw 'O PID publicado foi reutilizado ou nao pertence a esta instancia do daemon PIX.'
    }
    $expected = @($ExpectedExecutablePaths | ForEach-Object { [IO.Path]::GetFullPath($_) })
    $actualExecutable = [string]$daemonProcess.Path
    if ([string]::IsNullOrWhiteSpace($actualExecutable) -or
        -not ($expected | Where-Object { $_.Equals([IO.Path]::GetFullPath($actualExecutable), [StringComparison]::OrdinalIgnoreCase) })) {
        throw "O PID publicado nao executa um agente PIX instalado reconhecido: $actualExecutable"
    }
    foreach ($mutexName in @('Local\TurboRamaPixAgent-Daemon-v1', "Local\TurboRamaPixAgent-Daemon-v1-$processId")) {
        $mutex = $null
        try { $mutex = [Threading.Mutex]::OpenExisting($mutexName) }
        catch { throw "A prova de identidade do daemon PIX nao esta disponivel: $mutexName" }
        finally { if ($mutex) { $mutex.Dispose() } }
    }
    if ($daemonProcess.HasExited) { throw 'O daemon PIX encerrou durante a verificacao de identidade.' }
    return $updated
}

function Get-ExactPixAgentProcesses {
    param([Parameter(Mandatory = $true)] [string]$ExpectedExecutablePath)

    $expected = [IO.Path]::GetFullPath($ExpectedExecutablePath)
    try {
        $candidates = @(Get-CimInstance Win32_Process -Filter "Name='TurboRamaPixAgent.exe'" -ErrorAction Stop)
    }
    catch {
        throw "Nao foi possivel enumerar os processos do agente PIX: $($_.Exception.Message)"
    }

    $result = @()
    foreach ($candidate in $candidates) {
        $candidatePath = [string]$candidate.ExecutablePath
        if ([string]::IsNullOrWhiteSpace($candidatePath)) {
            throw "Nao foi possivel confirmar o caminho do processo TurboRamaPixAgent.exe PID $($candidate.ProcessId)."
        }
        if (-not ([IO.Path]::GetFullPath($candidatePath)).Equals($expected, [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $process = Get-Process -Id ([int]$candidate.ProcessId) -ErrorAction SilentlyContinue
        if (-not $process) { continue }
        try { $actualPath = [IO.Path]::GetFullPath([string]$process.Path) }
        catch { throw "Nao foi possivel revalidar o caminho do agente PIX PID $($candidate.ProcessId)." }
        if (-not $actualPath.Equals($expected, [StringComparison]::OrdinalIgnoreCase)) {
            throw "O PID $($candidate.ProcessId) mudou de identidade durante a parada do agente PIX."
        }
        $result += $process
    }
    return @($result)
}

function Stop-ExactPixAgent {
    param(
        [Parameter(Mandatory = $true)] [string]$ExpectedExecutablePath,
        [int]$TimeoutSeconds = 20
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $running = @(Get-ExactPixAgentProcesses -ExpectedExecutablePath $ExpectedExecutablePath)
        if ($running.Count -eq 0) { return }
        foreach ($process in $running) {
            try {
                Stop-Process -InputObject $process -Force -ErrorAction Stop
            }
            catch {
                $stillRunning = @(Get-ExactPixAgentProcesses -ExpectedExecutablePath $ExpectedExecutablePath |
                    Where-Object { $_.Id -eq $process.Id })
                if ($stillRunning.Count -gt 0) {
                    throw "Nao foi possivel encerrar o agente PIX PID $($process.Id): $($_.Exception.Message)"
                }
            }
        }
        if ([DateTimeOffset]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 250 }
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    $remaining = @(Get-ExactPixAgentProcesses -ExpectedExecutablePath $ExpectedExecutablePath)
    if ($remaining.Count -gt 0) {
        throw "O agente PIX anterior nao encerrou no prazo. PIDs ainda ativos: $(($remaining.Id -join ', '))."
    }
}

function Wait-PixDaemonReady {
    param(
        [Parameter(Mandatory = $true)] [string]$StatusFile,
        [Parameter(Mandatory = $true)] [string[]]$ExpectedExecutablePaths,
        [Parameter(Mandatory = $true)] [string]$ExpectedProvider,
        [Parameter(Mandatory = $true)] [long]$MinimumProcessStartFileTimeUtc,
        [int]$HeartbeatMaxAgeSeconds = 120,
        [int]$TimeoutSeconds = 90
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastError = 'O daemon ainda nao publicou agent-status.json.'
    do {
        try {
            if (-not (Test-Path -LiteralPath $StatusFile -PathType Leaf)) { throw $lastError }
            $statusItem = Get-Item -LiteralPath $StatusFile -ErrorAction Stop
            if ($statusItem.Length -le 1 -or $statusItem.Length -gt 1MB) {
                throw 'agent-status.json possui tamanho invalido.'
            }
            $status = Get-Content -LiteralPath $StatusFile -Raw -ErrorAction Stop | ConvertFrom-Json
            [void](Assert-PixDaemonStatus -Status $status -ExpectedExecutablePaths $ExpectedExecutablePaths -HeartbeatMaxAgeSeconds $HeartbeatMaxAgeSeconds)
            if ([long]$status.processStartFileTimeUtc -lt $MinimumProcessStartFileTimeUtc) {
                throw 'O status ainda pertence a uma instancia anterior do daemon PIX.'
            }
            if (-not $status.PSObject.Properties['ready'] -or $status.ready -isnot [bool] -or -not $status.ready) {
                throw "O daemon PIX ainda nao esta pronto. Estado: $([string]$status.state)"
            }
            if (-not $status.PSObject.Properties['provider'] -or $status.provider -isnot [string] -or
                [string]$status.provider -cne $ExpectedProvider) {
                throw "O daemon PIX publicou um provedor diferente do esperado: $([string]$status.provider)"
            }
            return $status
        }
        catch {
            if (-not [string]::IsNullOrWhiteSpace($_.Exception.Message)) { $lastError = $_.Exception.Message }
        }
        if ([DateTimeOffset]::UtcNow -lt $deadline) { Start-Sleep -Seconds 1 }
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw "O daemon PIX nao ficou pronto em $TimeoutSeconds segundos. Ultimo diagnostico: $lastError"
}

try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Start-Process powershell.exe -Verb RunAs -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $PSCommandPath + '"'))
        exit
    }

    Clear-Host
    Write-Host '=============================================' -ForegroundColor Cyan
    Write-Host ' TURBORAMA - INSTALACAO COMERCIAL DO PIX' -ForegroundColor White
    Write-Host '=============================================' -ForegroundColor Cyan
    Write-Host ''
    Write-Host 'A credencial sera protegida pelo Windows e nao aparecera no EmulationStation.'
    Write-Host ''

    Write-Host 'Escolha o provedor:' -ForegroundColor White
    Write-Host '  1 - Mercado Pago / conta Mercado Livre (RECOMENDADO)'
    Write-Host '  2 - Outro banco ou plataforma via adaptador TurboRama'
    $providerChoice = (Read-Host 'Opcao [1]').Trim()
    if ([string]::IsNullOrWhiteSpace($providerChoice)) { $providerChoice = '1' }
    if ($providerChoice -notin @('1','2')) { throw 'Opcao de provedor invalida.' }

    $providerName = if ($providerChoice -eq '1') { 'mercadopago' } else { 'adapter' }
    $externalPosId = $null
    $adapterBaseUrl = $null
    $adapterProviderId = $null
    if ($providerName -eq 'mercadopago') {
        Write-Host 'O PDV nao e APP_USR, Access Token nem ID do aplicativo.' -ForegroundColor Yellow
        Write-Host 'Use o external_id da caixa ja criada, por exemplo TURBORAMAKIOSK01.'
        $externalPosId = (Read-Host 'Digite o external_id real do PDV criado no Mercado Pago').Trim()
        if ($externalPosId -notmatch '^[A-Za-z0-9]{1,39}$' -or $externalPosId -match '^APP.?USR') {
            throw 'Identificador de PDV invalido. O external_id aceita somente letras e numeros, tem menos de 40 caracteres e nao e uma credencial.'
        }
    }
    else {
        Write-Host ''
        Write-Host 'O adaptador do banco deve seguir o arquivo CONTRATO-ADAPTADOR-BANCARIO.md.' -ForegroundColor Yellow
        $adapterBaseUrl = (Read-Host 'URL do adaptador (ex.: http://127.0.0.1:8765/)').Trim()
        $adapterProviderId = (Read-Host 'Identificador informado pelo adaptador (ex.: meu-banco)').Trim().ToLowerInvariant()
        if ($adapterProviderId -notmatch '^[a-z0-9_-]{2,48}$') {
            throw 'Identificador do adaptador invalido.'
        }
    }

    $sourceAgent = Join-Path $PSScriptRoot 'Agent'
    $sourceConfig = Join-Path $PSScriptRoot 'appsettings.json'
    $sourceEmulationStation = Join-Path $PSScriptRoot 'EmulationStation\emulationstation.exe'
    if (-not (Test-Path (Join-Path $sourceAgent 'TurboRamaPixAgent.exe')) -or -not (Test-Path $sourceConfig)) {
        throw 'O pacote esta incompleto. A pasta Agent ou o arquivo appsettings.json nao foi encontrado.'
    }
    if (-not (Test-Path $sourceEmulationStation)) {
        throw 'O executavel comercial do EmulationStation nao foi encontrado no pacote.'
    }

    $knownExecutables = @(
        'D:\emulationstation\emulationstation.exe',
        'D:\EmulationStation\emulationstation.exe',
        (Join-Path $env:ProgramFiles 'EmulationStation\emulationstation.exe')
    )
    $installedEmulationStation = $knownExecutables | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $installedEmulationStation) {
        $installedEmulationStation = (Read-Host 'Digite o caminho completo do emulationstation.exe instalado').Trim('"').Trim()
    }
    if (-not (Test-Path $installedEmulationStation -PathType Leaf)) {
        throw 'O emulationstation.exe instalado nao foi encontrado.'
    }

    $emulationDirectory = Split-Path -Parent $installedEmulationStation
    $portableDataDirectory = Join-Path $emulationDirectory '.emulationstation'
    $bridgeDirectory = if (Test-Path -LiteralPath $portableDataDirectory -PathType Container) {
        Join-Path $portableDataDirectory 'pix'
    } else {
        Join-Path $env:USERPROFILE '.emulationstation\pix'
    }
    New-Item -ItemType Directory -Force -Path $bridgeDirectory | Out-Null

    $installDirectory = Join-Path $env:ProgramData 'TurboRama\PixAgent'
    $installedConfig = Join-Path $installDirectory 'appsettings.json'
    $previousBridgeDirectory = $null
    if (Test-Path -LiteralPath $installedConfig -PathType Leaf) {
        try {
            $previousConfiguration = Get-Content -LiteralPath $installedConfig -Raw | ConvertFrom-Json
            $previousBridgeDirectory = [Environment]::ExpandEnvironmentVariables([string]$previousConfiguration.TurboRamaPix.BridgeDirectory)
            if (-not [IO.Path]::IsPathRooted($previousBridgeDirectory)) { $previousBridgeDirectory = $null }
        } catch { $previousBridgeDirectory = $null }
    }

    $taskName = 'TurboRama PIX Agent'
    $existingTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if ($existingTask) {
        Stop-ScheduledTask -TaskName $taskName -ErrorAction Stop
    }
    $currentAgentPath = Join-Path $installDirectory 'TurboRamaPixAgent.exe'
    Stop-ExactPixAgent -ExpectedExecutablePath $currentAgentPath

    New-Item -ItemType Directory -Force -Path $installDirectory | Out-Null
    Copy-Item -Path (Join-Path $sourceAgent '*') -Destination $installDirectory -Recurse -Force
    Copy-Item -LiteralPath $sourceConfig -Destination (Join-Path $installDirectory 'appsettings.json') -Force

    $configuration = Get-Content -LiteralPath $installedConfig -Raw | ConvertFrom-Json
    $configuration.TurboRamaPix.Provider = $providerName
    $configuration.TurboRamaPix.BridgeDirectory = $bridgeDirectory
    if ($providerName -eq 'mercadopago') {
        $configuration.TurboRamaPix.MercadoPago.ExternalPosId = $externalPosId
    }
    else {
        $configuration.TurboRamaPix.Adapter.BaseUrl = $adapterBaseUrl
        $configuration.TurboRamaPix.Adapter.ProviderId = $adapterProviderId
    }
    $configuration | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $installedConfig -Encoding UTF8

    if ($previousBridgeDirectory -and -not $previousBridgeDirectory.Equals($bridgeDirectory, [StringComparison]::OrdinalIgnoreCase)) {
        foreach ($protectedFile in @('secret.dat','bridge.key')) {
            $oldProtectedFile = Join-Path $previousBridgeDirectory $protectedFile
            $newProtectedFile = Join-Path $bridgeDirectory $protectedFile
            if ((Test-Path -LiteralPath $oldProtectedFile -PathType Leaf) -and -not (Test-Path -LiteralPath $newProtectedFile)) {
                Copy-Item -LiteralPath $oldProtectedFile -Destination $newProtectedFile -Force
            }
        }
    }

    $agent = Join-Path $installDirectory 'TurboRamaPixAgent.exe'
    Write-Host ''
    $secretFile = Join-Path $bridgeDirectory 'secret.dat'
    $replaceCredential = 'S'
    if (Test-Path -LiteralPath $secretFile -PathType Leaf) {
        $replaceCredential = (Read-Host 'Credencial protegida encontrada e preservada. Deseja substitui-la? [N]').Trim()
    }
    if ($replaceCredential -match '^[SsYy]') {
        if ($providerName -eq 'mercadopago') {
            Write-Host 'Agora cole o ACCESS TOKEN DE PRODUCAO do Mercado Pago.' -ForegroundColor Yellow
        }
        else {
            Write-Host 'Agora cole o segredo Bearer definido no adaptador bancario.' -ForegroundColor Yellow
        }
        & $agent --set-token
        if ($LASTEXITCODE -ne 0) { throw "Nao foi possivel guardar a credencial (codigo $LASTEXITCODE)." }
    }

    Write-Host ''
    Write-Host 'Verificando credencial e conexao oficial...' -ForegroundColor Cyan
    & $agent --check-provider
    if ($LASTEXITCODE -ne 0) { throw 'A credencial foi recusada ou a conexao com o provedor falhou.' }

    & $agent --self-test
    if ($LASTEXITCODE -ne 0) { throw 'A verificacao local de seguranca falhou.' }

    $taskUser = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $action = New-ScheduledTaskAction -Execute $agent -Argument '--daemon' -WorkingDirectory $installDirectory
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $taskUser
    $taskPrincipal = New-ScheduledTaskPrincipal -UserId $taskUser -LogonType Interactive -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -RestartCount 20 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $taskPrincipal -Settings $settings -Force | Out-Null
    $statusFile = Join-Path $bridgeDirectory 'agent-status.json'
    $minimumProcessStartFileTimeUtc = [DateTime]::UtcNow.ToFileTimeUtc()
    Start-ScheduledTask -TaskName $taskName -ErrorAction Stop
    $status = Wait-PixDaemonReady -StatusFile $statusFile -ExpectedExecutablePaths @($agent) `
        -ExpectedProvider $providerName -MinimumProcessStartFileTimeUtc $minimumProcessStartFileTimeUtc

    if (Get-Process emulationstation -ErrorAction SilentlyContinue) {
        Write-Host ''
        Write-Host 'O EmulationStation esta aberto.' -ForegroundColor Yellow
        Write-Host 'Encerre somente o EmulationStation pelo menu. O instalador NAO desligara o computador.'
        Read-Host 'Depois que a tela fechar, pressione ENTER para continuar'
    }
    if (Get-Process emulationstation -ErrorAction SilentlyContinue) {
        throw 'O EmulationStation continua aberto. Ele nao foi encerrado pelo instalador e nenhum executavel foi substituido.'
    }

    $backupDirectory = Join-Path $emulationDirectory ('backups\antes-pix-comercial-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Force -Path $backupDirectory | Out-Null
    $backupExecutable = Join-Path $backupDirectory 'emulationstation.exe'
    Copy-Item -LiteralPath $installedEmulationStation -Destination $backupExecutable -Force
    $expectedHash = (Get-FileHash -LiteralPath $sourceEmulationStation -Algorithm SHA256).Hash
    try {
        Copy-Item -LiteralPath $sourceEmulationStation -Destination $installedEmulationStation -Force
        $installedHash = (Get-FileHash -LiteralPath $installedEmulationStation -Algorithm SHA256).Hash
        if ($installedHash -ne $expectedHash) { throw 'A copia do novo executavel nao passou na verificacao SHA-256.' }
    }
    catch {
        Copy-Item -LiteralPath $backupExecutable -Destination $installedEmulationStation -Force
        throw ('Falha ao atualizar o EmulationStation. O backup foi restaurado. ' + $_.Exception.Message)
    }

    Write-Host ''
    Write-Host 'INSTALACAO CONCLUIDA.' -ForegroundColor Green
    Write-Host 'No EmulationStation: START > COMPRAR TEMPO COM PIX.' -ForegroundColor White
    Write-Host ('Pasta PIX compartilhada: ' + $bridgeDirectory) -ForegroundColor Gray
    Write-Host ('Backup do executavel anterior: ' + $backupExecutable) -ForegroundColor Gray
    Write-Host 'Antes de liberar ao publico, realize uma compra real de menor valor e confira o recebimento na conta.' -ForegroundColor Yellow
}
catch {
    Write-Host ''
    Write-Host ('FALHA: ' + $_.Exception.Message) -ForegroundColor Red
    Write-Host 'Nenhuma cobranca deve ser oferecida ao cliente enquanto esta falha existir.' -ForegroundColor Yellow
}

Wait-Operator
