[CmdletBinding()]
param(
    [switch]$RequireProcessedPayment,
    [switch]$NoPause
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$exitCode = 0

function Read-JsonFile {
    param([Parameter(Mandatory = $true)] [string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Arquivo de estado ausente: $Path"
    }
    $item = Get-Item -LiteralPath $Path
    if ($item.Length -le 1 -or $item.Length -gt 1MB) {
        throw "Arquivo de estado com tamanho invalido: $Path"
    }
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Assert-PixDaemonStatus {
    param(
        [Parameter(Mandatory = $true)] $Status,
        [Parameter(Mandatory = $true)] [string[]]$ExpectedExecutablePaths,
        [int]$HeartbeatMaxAgeSeconds = 120
    )

    foreach ($field in @('schemaVersion','mode','processId','processStartFileTimeUtc','managerTokenHash','provider','ready','state','updatedAtUnixSeconds')) {
        if (-not $Status.PSObject.Properties[$field]) {
            throw "agent-status.json nao possui o campo obrigatorio '$field'."
        }
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
    $actualStart = [long]$daemonProcess.StartTime.ToUniversalTime().ToFileTimeUtc()
    if ($actualStart -ne [long]$Status.processStartFileTimeUtc) {
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

try {
    $knownExecutables = @(
        'D:\emulationstation\emulationstation.exe',
        'D:\EmulationStation\emulationstation.exe',
        (Join-Path $env:ProgramFiles 'EmulationStation\emulationstation.exe')
    )
    $installedEmulationStation = $knownExecutables |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
    if (-not $installedEmulationStation) {
        throw 'A instalacao do EmulationStation nao foi encontrada.'
    }

    $portableData = Join-Path (Split-Path -Parent $installedEmulationStation) '.emulationstation'
    $bridgeDirectory = if (Test-Path -LiteralPath $portableData -PathType Container) {
        Join-Path $portableData 'pix'
    }
    else {
        Join-Path $env:USERPROFILE '.emulationstation\pix'
    }

    $installedConfig = Join-Path $env:ProgramData 'TurboRama\PixAgent\appsettings.json'
    if (Test-Path -LiteralPath $installedConfig -PathType Leaf) {
        $configuration = Read-JsonFile $installedConfig
        $configuredBridge = [Environment]::ExpandEnvironmentVariables(
            [string]$configuration.TurboRamaPix.BridgeDirectory)
        if ($configuredBridge -and
            -not ([IO.Path]::GetFullPath($configuredBridge)).Equals(
                [IO.Path]::GetFullPath($bridgeDirectory), [StringComparison]::OrdinalIgnoreCase)) {
            throw "O agente e o EmulationStation usam pastas diferentes. Agente: $configuredBridge | EmulationStation: $bridgeDirectory"
        }
    }

    $status = Read-JsonFile (Join-Path $bridgeDirectory 'agent-status.json')
    $emulationDirectory = Split-Path -Parent $installedEmulationStation
    $expectedAgentExecutables = @(
        (Join-Path $emulationDirectory 'pix-agent\runtime\dotnet.exe'),
        (Join-Path $emulationDirectory 'pix-agent\TurboRamaPixAgent.exe'),
        (Join-Path $env:ProgramData 'TurboRama\PixAgent\TurboRamaPixAgent.exe')
    )
    $updated = Assert-PixDaemonStatus -Status $status -ExpectedExecutablePaths $expectedAgentExecutables
    if ([string]$status.provider -notin @('mercadopago', 'adapter')) {
        throw 'O provedor comercial ativo nao e valido.'
    }
    if (-not $status.ready) {
        throw "O agente nao esta pronto. Estado: $([string]$status.state)"
    }
    $publicOptions = Read-JsonFile (Join-Path $bridgeDirectory 'public-options.json')
    foreach ($field in @('schemaVersion','ready','productionEnabled')) {
        if (-not $publicOptions.PSObject.Properties[$field]) {
            throw "public-options.json nao possui o campo obrigatorio '$field'."
        }
    }
    if (($publicOptions.schemaVersion -isnot [int] -and $publicOptions.schemaVersion -isnot [long]) -or
        [long]$publicOptions.schemaVersion -ne 1) {
        throw 'Contrato de public-options.json e invalido.'
    }
    if ($publicOptions.ready -isnot [bool] -or -not $publicOptions.ready) {
        throw 'A compra publica ainda nao foi liberada pelo agente.'
    }
    if ($publicOptions.productionEnabled -isnot [bool] -or -not $publicOptions.productionEnabled) {
        throw 'Pagamentos comerciais estao desabilitados na configuracao publica.'
    }

    $processedPayment = $null
    if ($RequireProcessedPayment) {
        $processedDirectory = Join-Path $bridgeDirectory 'processed'
        $processedPayment = Get-ChildItem -LiteralPath $processedDirectory -Filter '*.credit.json' -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending |
            ForEach-Object {
                try {
                    $event = Read-JsonFile $_.FullName
                    if ([int]$event.schemaVersion -eq 2 -and
                        [string]$event.provider -in @('mercadopago', 'adapter') -and
                        [string]$event.beneficiaryType -in @('player', 'guest') -and
                        [string]$event.beneficiaryId -match '^[A-Za-z0-9_-]{16,128}$') {
                        return $event
                    }
                }
                catch { }
            } |
            Select-Object -First 1
        if (-not $processedPayment) {
            throw 'Nenhum pagamento comercial schema v2 processado foi encontrado. Faca uma cobranca real controlada antes de liberar producao.'
        }
    }

    Write-Host 'PIX COMERCIAL ONLINE E OPERACIONAL.' -ForegroundColor Green
    Write-Host ('Provedor: ' + [string]$status.provider)
    Write-Host ('Ultimo heartbeat: ' + $updated.ToLocalTime().ToString('dd/MM/yyyy HH:mm:ss'))
    Write-Host ('Pasta compartilhada: ' + $bridgeDirectory)
    if ($processedPayment) {
        $approved = [DateTimeOffset]::FromUnixTimeSeconds([long]$processedPayment.approvedAtUnixSeconds)
        Write-Host ('Pagamento schema v2 encontrado, aprovado em: ' + $approved.ToLocalTime().ToString('dd/MM/yyyy HH:mm:ss'))
    }
    else {
        Write-Host 'Validacao de pagamento real nao foi exigida nesta execucao.' -ForegroundColor Yellow
    }
    Write-Host 'Menu do cliente: SELECT > COMPRAR TEMPO COM PIX'
    Write-Host 'Este verificador nunca le secret.dat nem bridge.key.' -ForegroundColor Cyan
}
catch {
    $exitCode = 1
    Write-Host ('PIX INDISPONIVEL: ' + $_.Exception.Message) -ForegroundColor Red
}

if (-not $NoPause) {
    Write-Host ''
    Read-Host 'Pressione ENTER para fechar' | Out-Null
}
exit $exitCode
