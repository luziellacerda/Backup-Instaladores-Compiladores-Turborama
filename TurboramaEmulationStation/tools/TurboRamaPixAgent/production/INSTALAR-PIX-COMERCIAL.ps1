#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

function Wait-Operator {
    Write-Host ''
    Read-Host 'Pressione ENTER para fechar'
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
        Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }
    $currentAgentPath = Join-Path $installDirectory 'TurboRamaPixAgent.exe'
    Get-CimInstance Win32_Process -Filter "Name='TurboRamaPixAgent.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.ExecutablePath -and $_.ExecutablePath.Equals($currentAgentPath, [StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 1

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
    $action = New-ScheduledTaskAction -Execute $agent -WorkingDirectory $installDirectory
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $taskUser
    $taskPrincipal = New-ScheduledTaskPrincipal -UserId $taskUser -LogonType Interactive -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -RestartCount 20 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $taskPrincipal -Settings $settings -Force | Out-Null
    Start-ScheduledTask -TaskName $taskName
    Start-Sleep -Seconds 5

    $statusFile = Join-Path $bridgeDirectory 'agent-status.json'
    if (-not (Test-Path $statusFile)) { throw 'O servico foi instalado, mas ainda nao publicou o estado operacional.' }
    $status = Get-Content -LiteralPath $statusFile -Raw | ConvertFrom-Json
    if (-not $status.ready -or $status.provider -ne $providerName) { throw 'O servico iniciou, mas nao ficou pronto para pagamentos reais.' }

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
