#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

function Wait-Operator {
    Write-Host ''
    Read-Host 'Pressione ENTER para fechar'
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
        Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }
    Get-CimInstance Win32_Process -Filter "Name='TurboRamaPixAgent.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.ExecutablePath -and $_.ExecutablePath.Equals($installedAgent, [StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 1

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
    $action = New-ScheduledTaskAction -Execute $installedAgent -WorkingDirectory $installDirectory
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $taskUser
    $taskPrincipal = New-ScheduledTaskPrincipal -UserId $taskUser -LogonType Interactive -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -RestartCount 20 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $taskPrincipal -Settings $settings -Force | Out-Null
    Start-ScheduledTask -TaskName $taskName
    Start-Sleep -Seconds 6

    $statusFile = Join-Path $bridgeDirectory 'agent-status.json'
    $optionsFile = Join-Path $bridgeDirectory 'public-options.json'
    if (-not (Test-Path -LiteralPath $statusFile -PathType Leaf) -or -not (Test-Path -LiteralPath $optionsFile -PathType Leaf)) {
        throw 'O agente iniciou, mas nao publicou os arquivos que o EmulationStation consulta.'
    }
    $status = Get-Content -LiteralPath $statusFile -Raw | ConvertFrom-Json
    $updated = [DateTimeOffset]::FromUnixTimeSeconds([long]$status.updatedAtUnixSeconds)
    if (-not $status.ready -or $status.provider -ne 'mercadopago' -or ([DateTimeOffset]::UtcNow - $updated).TotalSeconds -gt 30) {
        throw 'O agente iniciou, mas o estado publicado ainda nao esta pronto ou esta desatualizado.'
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
