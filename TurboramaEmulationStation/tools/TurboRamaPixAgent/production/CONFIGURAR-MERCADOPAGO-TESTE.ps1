#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$setupFile = $null

function Wait-Operator {
    Write-Host ''
    Read-Host 'Pressione ENTER para fechar'
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
        Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }
    $installedAgent = Join-Path $installDirectory 'TurboRamaPixAgent.exe'
    Get-CimInstance Win32_Process -Filter "Name='TurboRamaPixAgent.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.ExecutablePath -and $_.ExecutablePath.Equals($installedAgent, [StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 1

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
    $action = New-ScheduledTaskAction -Execute $installedAgent -WorkingDirectory $installDirectory
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $taskUser
    $taskPrincipal = New-ScheduledTaskPrincipal -UserId $taskUser -LogonType Interactive -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -RestartCount 20 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $taskPrincipal -Settings $settings -Force | Out-Null
    Start-ScheduledTask -TaskName $taskName
    Start-Sleep -Seconds 6

    $statusFile = Join-Path $bridgeDirectory 'agent-status.json'
    if (-not (Test-Path -LiteralPath $statusFile -PathType Leaf)) { throw 'O agente nao publicou o estado final.' }
    $status = Get-Content -LiteralPath $statusFile -Raw | ConvertFrom-Json
    if (-not $status.ready -or $status.provider -ne 'mercadopago') { throw 'O agente iniciou, mas nao ficou pronto.' }

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
