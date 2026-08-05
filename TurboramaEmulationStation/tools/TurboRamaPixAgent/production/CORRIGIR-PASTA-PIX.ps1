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

function Resolve-BridgeDirectory([string]$emulationStationPath) {
    $emulationDirectory = Split-Path -Parent $emulationStationPath
    $portableDataDirectory = Join-Path $emulationDirectory '.emulationstation'
    if (Test-Path -LiteralPath $portableDataDirectory -PathType Container) {
        return Join-Path $portableDataDirectory 'pix'
    }
    return Join-Path $env:USERPROFILE '.emulationstation\pix'
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
    Write-Host ' TURBORAMA - CORRECAO DO PIX PORTATIL' -ForegroundColor White
    Write-Host '=============================================' -ForegroundColor Cyan
    Write-Host 'Esta correcao nao fecha nem substitui o EmulationStation.'
    Write-Host 'Ela atualiza somente o agente PIX e sua pasta compartilhada.'
    Write-Host ''

    $sourceAgent = Join-Path $PSScriptRoot 'Agent'
    $sourceConfig = Join-Path $PSScriptRoot 'appsettings.json'
    if (-not (Test-Path -LiteralPath (Join-Path $sourceAgent 'TurboRamaPixAgent.exe') -PathType Leaf)) {
        throw 'Pacote incompleto: Agent\TurboRamaPixAgent.exe nao foi encontrado.'
    }

    $knownExecutables = @(
        'D:\emulationstation\emulationstation.exe',
        'D:\EmulationStation\emulationstation.exe',
        (Join-Path $env:ProgramFiles 'EmulationStation\emulationstation.exe')
    )
    $installedEmulationStation = $knownExecutables | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (-not $installedEmulationStation) {
        $installedEmulationStation = (Read-Host 'Digite o caminho completo do emulationstation.exe instalado').Trim('"').Trim()
    }
    if (-not (Test-Path -LiteralPath $installedEmulationStation -PathType Leaf)) {
        throw 'O emulationstation.exe instalado nao foi encontrado.'
    }

    $bridgeDirectory = Resolve-BridgeDirectory $installedEmulationStation
    New-Item -ItemType Directory -Force -Path $bridgeDirectory | Out-Null

    $installDirectory = Join-Path $env:ProgramData 'TurboRama\PixAgent'
    $installedConfig = Join-Path $installDirectory 'appsettings.json'
    $previousBridgeDirectory = $null
    $previousExternalPosId = $null
    if (Test-Path -LiteralPath $installedConfig -PathType Leaf) {
        try {
            $previousConfiguration = Get-Content -LiteralPath $installedConfig -Raw | ConvertFrom-Json
            $previousBridgeDirectory = [Environment]::ExpandEnvironmentVariables([string]$previousConfiguration.TurboRamaPix.BridgeDirectory)
            $previousExternalPosId = [string]$previousConfiguration.TurboRamaPix.MercadoPago.ExternalPosId
        }
        catch { throw 'A configuracao PIX instalada esta corrompida.' }
    }
    elseif (-not (Test-Path -LiteralPath $sourceConfig -PathType Leaf)) {
        throw 'Nao existe configuracao instalada nem appsettings.json no pacote.'
    }

    Write-Host ('EmulationStation: ' + $installedEmulationStation) -ForegroundColor Gray
    Write-Host ('Pasta PIX correta: ' + $bridgeDirectory) -ForegroundColor Green
    if ($previousBridgeDirectory) { Write-Host ('Pasta usada anteriormente: ' + $previousBridgeDirectory) -ForegroundColor Yellow }
    Write-Host ''
    Write-Host 'O identificador do PDV NAO e APP_USR, Access Token ou ID do aplicativo.' -ForegroundColor Yellow
    Write-Host 'Informe o external_id do caixa criado no Mercado Pago (ex.: TURBORAMAKIOSK01).'
    $prompt = 'external_id real do PDV'
    if ($previousExternalPosId -match '^[A-Za-z0-9]{1,39}$' -and $previousExternalPosId -notmatch '^APP.?USR') {
        $prompt += " [$previousExternalPosId]"
    }
    $externalPosId = (Read-Host $prompt).Trim()
    if ([string]::IsNullOrWhiteSpace($externalPosId) -and $prompt.Contains('[')) { $externalPosId = $previousExternalPosId }
    if ($externalPosId -notmatch '^[A-Za-z0-9]{1,39}$' -or $externalPosId -match '^APP.?USR') {
        throw 'PDV invalido. Use apenas letras e numeros, no maximo 39 caracteres. Nao informe APP_USR nem Access Token.'
    }

    $backupDirectory = Join-Path $installDirectory ('backups\antes-correcao-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Force -Path $backupDirectory | Out-Null
    if (Test-Path -LiteralPath $installedConfig -PathType Leaf) {
        Copy-Item -LiteralPath $installedConfig -Destination (Join-Path $backupDirectory 'appsettings.json') -Force
    }
    $installedAgent = Join-Path $installDirectory 'TurboRamaPixAgent.exe'
    if (Test-Path -LiteralPath $installedAgent -PathType Leaf) {
        Copy-Item -LiteralPath $installedAgent -Destination (Join-Path $backupDirectory 'TurboRamaPixAgent.exe') -Force
    }

    $taskName = 'TurboRama PIX Agent'
    if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
        Stop-ScheduledTask -TaskName $taskName -ErrorAction Stop
    }
    Stop-ExactPixAgent -ExpectedExecutablePath $installedAgent

    New-Item -ItemType Directory -Force -Path $installDirectory | Out-Null
    Get-ChildItem -LiteralPath $sourceAgent -Force |
        Where-Object { $_.Name -ne 'appsettings.json' } |
        Copy-Item -Destination $installDirectory -Recurse -Force
    if (-not (Test-Path -LiteralPath $installedConfig -PathType Leaf)) {
        Copy-Item -LiteralPath $sourceConfig -Destination $installedConfig -Force
    }

    $configuration = Get-Content -LiteralPath $installedConfig -Raw | ConvertFrom-Json
    $configuration.TurboRamaPix.Provider = 'mercadopago'
    $configuration.TurboRamaPix.ProductionEnabled = $true
    $configuration.TurboRamaPix.BridgeDirectory = $bridgeDirectory
    $configuration.TurboRamaPix.MercadoPago.ExternalPosId = $externalPosId
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

    $secretFile = Join-Path $bridgeDirectory 'secret.dat'
    if (-not (Test-Path -LiteralPath $secretFile -PathType Leaf)) {
        Write-Host ''
        Write-Host 'A credencial anterior nao foi encontrada. Cole o ACCESS TOKEN DE PRODUCAO.' -ForegroundColor Yellow
        & $installedAgent --set-token
        if ($LASTEXITCODE -ne 0) { throw "Nao foi possivel guardar a credencial (codigo $LASTEXITCODE)." }
    }
    else {
        Write-Host 'Access Token protegido foi migrado; nao e necessario digita-lo novamente.' -ForegroundColor Green
    }

    Write-Host ''
    Write-Host 'Testando token, conta, conexao e existencia real do PDV...' -ForegroundColor Cyan
    & $installedAgent --check-provider
    if ($LASTEXITCODE -ne 0) {
        throw 'O teste real falhou. Confirme se o external_id informado existe na mesma conta do Access Token.'
    }
    & $installedAgent --self-test
    if ($LASTEXITCODE -ne 0) { throw 'A verificacao local de seguranca falhou.' }

    $taskUser = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $action = New-ScheduledTaskAction -Execute $installedAgent -Argument '--daemon' -WorkingDirectory $installDirectory
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $taskUser
    $taskPrincipal = New-ScheduledTaskPrincipal -UserId $taskUser -LogonType Interactive -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -RestartCount 20 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $taskPrincipal -Settings $settings -Force | Out-Null
    $statusFile = Join-Path $bridgeDirectory 'agent-status.json'
    $optionsFile = Join-Path $bridgeDirectory 'public-options.json'
    $minimumProcessStartFileTimeUtc = [DateTime]::UtcNow.ToFileTimeUtc()
    Start-ScheduledTask -TaskName $taskName -ErrorAction Stop
    $status = Wait-PixDaemonReady -StatusFile $statusFile -ExpectedExecutablePaths @($installedAgent) `
        -ExpectedProvider 'mercadopago' -MinimumProcessStartFileTimeUtc $minimumProcessStartFileTimeUtc `
        -HeartbeatMaxAgeSeconds 30
    if (-not (Test-Path -LiteralPath $optionsFile -PathType Leaf)) {
        throw 'O agente iniciou, mas nao publicou public-options.json.'
    }

    Write-Host ''
    Write-Host 'CORRECAO CONCLUIDA: PIX ONLINE E VISIVEL AO EMULATIONSTATION.' -ForegroundColor Green
    Write-Host ('Pasta compartilhada: ' + $bridgeDirectory) -ForegroundColor White
    Write-Host ('Backup do agente/configuracao: ' + $backupDirectory) -ForegroundColor Gray
    Write-Host 'Abra novamente START > COMPRAR TEMPO COM PIX.' -ForegroundColor White
    Write-Host 'Se a tela ja estava aberta, saia dela e entre novamente. Nao e preciso desligar o computador.' -ForegroundColor Yellow
}
catch {
    Write-Host ''
    Write-Host ('FALHA: ' + $_.Exception.Message) -ForegroundColor Red
    Write-Host 'O sistema permanece bloqueado para evitar cobrar sem confirmar o pagamento.' -ForegroundColor Yellow
}

Wait-Operator
