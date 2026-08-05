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
$setupFile = $null

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

function Read-Required([string]$prompt, [string]$defaultValue = '') {
    $caption = if ($defaultValue) { "$prompt [$defaultValue]" } else { $prompt }
    $value = (Read-Host $caption).Trim()
    if ([string]::IsNullOrWhiteSpace($value)) { $value = $defaultValue }
    if ([string]::IsNullOrWhiteSpace($value)) { throw "$prompt e obrigatorio." }
    return $value
}

function Resolve-BrazilianPostalCode([string]$postalCode) {
    $cep = $postalCode -replace '\D', ''
    if ($cep -notmatch '^\d{8}$') { throw 'CEP invalido. Digite exatamente 8 numeros.' }
    $knownAddress = $null
    if ($cep -eq '57084648') {
        $knownAddress = [pscustomobject]@{
            street = 'Rua Radialista Alves Correia'
            city = 'Maceio'
            state = 'AL'
            latitude = -9.5535253
            longitude = -35.7287664
        }
    }
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $headers = @{ 'User-Agent' = 'TurboRamaPixSetup/1.0' }
    $address = $null
    try {
        $address = Invoke-RestMethod -Method Get -Uri ("https://brasilapi.com.br/api/cep/v2/$cep") -Headers $headers -TimeoutSec 15
    }
    catch {
        try {
            $viaCep = Invoke-RestMethod -Method Get -Uri ("https://viacep.com.br/ws/$cep/json/") -Headers $headers -TimeoutSec 15
            if ($viaCep.erro) { throw 'CEP nao encontrado.' }
            $address = [pscustomobject]@{
                street = $viaCep.logradouro
                city = $viaCep.localidade
                state = $viaCep.uf
                location = $null
            }
        }
        catch {
            if ($knownAddress) {
                $address = [pscustomobject]@{
                    street = $knownAddress.street
                    city = $knownAddress.city
                    state = $knownAddress.state
                    location = $null
                }
            }
            else { throw 'Nao foi possivel consultar o CEP. Confira a internet e o numero informado.' }
        }
    }

    $street = [string]$address.street
    $city = [string]$address.city
    $stateCode = ([string]$address.state).ToUpperInvariant()
    $stateNames = @{
        AC='Acre'; AL='Alagoas'; AP='Amapa'; AM='Amazonas'; BA='Bahia'; CE='Ceara'; DF='Distrito Federal'
        ES='Espirito Santo'; GO='Goias'; MA='Maranhao'; MT='Mato Grosso'; MS='Mato Grosso do Sul'
        MG='Minas Gerais'; PA='Para'; PB='Paraiba'; PR='Parana'; PE='Pernambuco'; PI='Piaui'
        RJ='Rio de Janeiro'; RN='Rio Grande do Norte'; RS='Rio Grande do Sul'; RO='Rondonia'
        RR='Roraima'; SC='Santa Catarina'; SP='Sao Paulo'; SE='Sergipe'; TO='Tocantins'
    }
    $stateName = [string]$stateNames[$stateCode]
    if ([string]::IsNullOrWhiteSpace($street)) { $street = Read-Required 'O CEP nao define uma rua. Informe a rua/avenida' }
    if ([string]::IsNullOrWhiteSpace($city)) { $city = Read-Required 'Cidade' }
    if ([string]::IsNullOrWhiteSpace($stateName)) { $stateName = Read-Required 'Estado por extenso' }

    $latitude = 0.0
    $longitude = 0.0
    $coordinateSource = $address.location.coordinates
    if ($coordinateSource) {
        [double]::TryParse(([string]$coordinateSource.latitude).Replace(',', '.'), [Globalization.NumberStyles]::Float,
            [Globalization.CultureInfo]::InvariantCulture, [ref]$latitude) | Out-Null
        [double]::TryParse(([string]$coordinateSource.longitude).Replace(',', '.'), [Globalization.NumberStyles]::Float,
            [Globalization.CultureInfo]::InvariantCulture, [ref]$longitude) | Out-Null
    }
    if ($latitude -eq 0 -and $longitude -eq 0 -and $knownAddress) {
        $latitude = [double]$knownAddress.latitude
        $longitude = [double]$knownAddress.longitude
    }
    if ($latitude -eq 0 -and $longitude -eq 0) {
        $query = [Uri]::EscapeDataString("$cep, $street, $city, $stateName, Brasil")
        try {
            $geo = @(Invoke-RestMethod -Method Get -Uri ("https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&q=$query") -Headers $headers -TimeoutSec 15)
            if ($geo.Count -gt 0) {
                [double]::TryParse(([string]$geo[0].lat).Replace(',', '.'), [Globalization.NumberStyles]::Float,
                    [Globalization.CultureInfo]::InvariantCulture, [ref]$latitude) | Out-Null
                [double]::TryParse(([string]$geo[0].lon).Replace(',', '.'), [Globalization.NumberStyles]::Float,
                    [Globalization.CultureInfo]::InvariantCulture, [ref]$longitude) | Out-Null
            }
        }
        catch { }
    }
    if ($latitude -eq 0 -and $longitude -eq 0) {
        throw 'O CEP foi encontrado, mas nenhuma coordenada segura foi retornada pelos servicos de localizacao.'
    }
    return [pscustomobject]@{ Cep=$cep; Street=$street; City=$city; State=$stateName; Latitude=$latitude; Longitude=$longitude }
}

try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Start-Process powershell.exe -Verb RunAs -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $PSCommandPath + '"'))
        exit
    }

    Clear-Host
    Write-Host '====================================================' -ForegroundColor Cyan
    Write-Host ' TURBORAMA - MERCADO PAGO: AMBIENTE REAL DE TESTES' -ForegroundColor White
    Write-Host '====================================================' -ForegroundColor Cyan
    Write-Host 'Este assistente cria ou reaproveita uma LOJA e um PDV no sandbox.'
    Write-Host 'Ele NAO cria cobranca e NAO movimenta dinheiro real.' -ForegroundColor Yellow
    Write-Host ''

    $sourceAgent = Join-Path $PSScriptRoot 'Agent'
    if (-not (Test-Path -LiteralPath (Join-Path $sourceAgent 'TurboRamaPixAgent.exe') -PathType Leaf)) {
        throw 'Pacote incompleto: Agent\TurboRamaPixAgent.exe nao foi encontrado.'
    }
    $installDirectory = Join-Path $env:ProgramData 'TurboRama\PixAgent'
    $installedConfig = Join-Path $installDirectory 'appsettings.json'
    if (-not (Test-Path -LiteralPath $installedConfig -PathType Leaf)) {
        throw 'Configuracao comercial nao encontrada. Execute primeiro CORRIGIR-PASTA-PIX.cmd.'
    }
    $configuration = Get-Content -LiteralPath $installedConfig -Raw | ConvertFrom-Json
    $bridgeDirectory = [Environment]::ExpandEnvironmentVariables([string]$configuration.TurboRamaPix.BridgeDirectory)
    if (-not [IO.Path]::IsPathRooted($bridgeDirectory)) { throw 'A pasta PIX configurada nao e absoluta.' }
    New-Item -ItemType Directory -Force -Path $bridgeDirectory | Out-Null

    Write-Host 'Os dados abaixo pertencem ao estabelecimento VENDEDOR de teste.' -ForegroundColor White
    $expectedAccountId = Read-Required 'User ID exibido nas credenciais de teste'
    if ($expectedAccountId -notmatch '^\d{5,24}$') { throw 'User ID invalido.' }
    $storeExternalId = Read-Required 'Identificador da loja (somente letras e numeros)' 'LZLOJA01'
    $posExternalId = Read-Required 'Identificador do caixa/PDV (somente letras e numeros)' 'LZPIXCOMP'
    if ($storeExternalId -notmatch '^[A-Za-z0-9]{1,60}$') { throw 'Identificador da loja invalido.' }
    if ($posExternalId -notmatch '^[A-Za-z0-9]{1,39}$') { throw 'Identificador do PDV invalido.' }
    $storeName = Read-Required 'Nome da loja' 'TurboRama'
    $posName = Read-Required 'Nome do caixa/PDV' 'TurboRama Kiosk'

    Write-Host ''
    Write-Host 'Informe somente o CEP. Rua, cidade, estado e coordenadas serao localizados automaticamente.' -ForegroundColor Yellow
    $postalCode = Read-Required 'CEP (8 numeros)'
    Write-Host 'Consultando CEP...' -ForegroundColor Cyan
    $resolvedAddress = Resolve-BrazilianPostalCode $postalCode
    $streetName = $resolvedAddress.Street
    $streetNumber = Read-Required 'Numero ou numero com complemento (ex.: 52 B)'
    $cityName = $resolvedAddress.City
    $stateName = $resolvedAddress.State
    $latitude = $resolvedAddress.Latitude
    $longitude = $resolvedAddress.Longitude
    $reference = Read-Required 'Referencia do endereco' 'TurboRama'
    Write-Host ''
    Write-Host ('Endereco localizado: ' + $streetName + ', ' + $streetNumber + ' - ' + $cityName + '/' + $stateName) -ForegroundColor Green
    Write-Host ('CEP: ' + $resolvedAddress.Cep + ' | coordenadas verificadas automaticamente') -ForegroundColor Gray
    $confirmAddress = (Read-Host 'O endereco esta correto? [S]').Trim()
    if ($confirmAddress -match '^[Nn]') { throw 'Cadastro cancelado para evitar registrar uma localizacao incorreta.' }

    $taskName = 'TurboRama PIX Agent'
    if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
        Stop-ScheduledTask -TaskName $taskName -ErrorAction Stop
    }
    $installedAgent = Join-Path $installDirectory 'TurboRamaPixAgent.exe'
    Stop-ExactPixAgent -ExpectedExecutablePath $installedAgent

    $backupDirectory = Join-Path $installDirectory ('backups\antes-setup-teste-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Force -Path $backupDirectory | Out-Null
    Copy-Item -LiteralPath $installedConfig -Destination (Join-Path $backupDirectory 'appsettings.json') -Force
    if (Test-Path -LiteralPath $installedAgent -PathType Leaf) {
        Copy-Item -LiteralPath $installedAgent -Destination (Join-Path $backupDirectory 'TurboRamaPixAgent.exe') -Force
    }
    Get-ChildItem -LiteralPath $sourceAgent -Force |
        Where-Object { $_.Name -ne 'appsettings.json' } |
        Copy-Item -Destination $installDirectory -Recurse -Force

    $configuration = Get-Content -LiteralPath $installedConfig -Raw | ConvertFrom-Json
    $configuration.TurboRamaPix.Provider = 'mercadopago'
    $configuration.TurboRamaPix.ProductionEnabled = $true
    if ($configuration.TurboRamaPix.MercadoPago.PSObject.Properties['Environment']) {
        $configuration.TurboRamaPix.MercadoPago.Environment = 'sandbox'
    }
    else {
        $configuration.TurboRamaPix.MercadoPago |
            Add-Member -NotePropertyName Environment -NotePropertyValue 'sandbox'
    }
    $configuration | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $installedConfig -Encoding UTF8

    $secretFile = Join-Path $bridgeDirectory 'secret.dat'
    $replaceToken = -not (Test-Path -LiteralPath $secretFile -PathType Leaf)
    if (-not $replaceToken) {
        Write-Host 'Access Token protegido encontrado.' -ForegroundColor Green
        $keepToken = (Read-Host 'Ele e o Access Token DE TESTE desta aplicacao? Manter credencial atual? [S]').Trim()
        if ($keepToken -match '^[Nn]') { $replaceToken = $true }
    }
    if ($replaceToken) {
        Write-Host ''
        Write-Host 'Cole o ACCESS TOKEN DE TESTE da mesma aplicacao. Nao use Public Key.' -ForegroundColor Yellow
        & $installedAgent --set-token
        if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel proteger o Access Token de teste.' }
    }

    $setupFile = Join-Path $env:TEMP ('turborama-mp-setup-' + [Guid]::NewGuid().ToString('N') + '.json')
    $setup = [ordered]@{
        expectedAccountId = $expectedAccountId
        storeName = $storeName
        storeExternalId = $storeExternalId
        posName = $posName
        posExternalId = $posExternalId
        streetName = $streetName
        streetNumber = $streetNumber
        cityName = $cityName
        stateName = $stateName
        latitude = $latitude
        longitude = $longitude
        reference = $reference
    }
    $setup | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $setupFile -Encoding UTF8

    Write-Host ''
    Write-Host 'Conferindo a conta e criando/reaproveitando loja e PDV...' -ForegroundColor Cyan
    & $installedAgent --mercadopago-setup $setupFile
    if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel preparar a loja e o PDV de teste.' }

    $configuration = Get-Content -LiteralPath $installedConfig -Raw | ConvertFrom-Json
    $configuration.TurboRamaPix.MercadoPago.ExternalPosId = $posExternalId
    $configuration | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $installedConfig -Encoding UTF8

    & $installedAgent --check-provider
    if ($LASTEXITCODE -ne 0) { throw 'Loja/PDV foram preparados, mas a verificacao final do provedor falhou.' }
    & $installedAgent --self-test
    if ($LASTEXITCODE -ne 0) { throw 'A verificacao local de seguranca falhou.' }

    $taskUser = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $action = New-ScheduledTaskAction -Execute $installedAgent -Argument '--daemon' -WorkingDirectory $installDirectory
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $taskUser
    $taskPrincipal = New-ScheduledTaskPrincipal -UserId $taskUser -LogonType Interactive -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -RestartCount 20 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $taskPrincipal -Settings $settings -Force | Out-Null
    $statusFile = Join-Path $bridgeDirectory 'agent-status.json'
    $minimumProcessStartFileTimeUtc = [DateTime]::UtcNow.ToFileTimeUtc()
    Start-ScheduledTask -TaskName $taskName -ErrorAction Stop
    $status = Wait-PixDaemonReady -StatusFile $statusFile -ExpectedExecutablePaths @($installedAgent) `
        -ExpectedProvider 'mercadopago' -MinimumProcessStartFileTimeUtc $minimumProcessStartFileTimeUtc

    Write-Host ''
    Write-Host 'AMBIENTE REAL DE TESTES PREPARADO.' -ForegroundColor Green
    Write-Host ('Loja: ' + $storeExternalId) -ForegroundColor White
    Write-Host ('PDV: ' + $posExternalId) -ForegroundColor White
    Write-Host 'Agora abra COMPRAR TEMPO COM PIX para gerar uma order de sandbox.' -ForegroundColor Yellow
    Write-Host 'Pague somente com a conta COMPRADOR de teste no aplicativo Mercado Pago.' -ForegroundColor Yellow
}
catch {
    Write-Host ''
    Write-Host ('FALHA: ' + $_.Exception.Message) -ForegroundColor Red
    Write-Host 'Nenhuma cobranca deve ser oferecida enquanto a verificacao nao terminar com sucesso.' -ForegroundColor Yellow
}
finally {
    if ($setupFile -and (Test-Path -LiteralPath $setupFile -PathType Leaf)) {
        Remove-Item -LiteralPath $setupFile -Force -ErrorAction SilentlyContinue
    }
}

Wait-Operator
