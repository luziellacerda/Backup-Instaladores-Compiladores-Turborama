$ErrorActionPreference = 'Stop'
try {
    $knownExecutables = @(
        'D:\emulationstation\emulationstation.exe',
        'D:\EmulationStation\emulationstation.exe',
        (Join-Path $env:ProgramFiles 'EmulationStation\emulationstation.exe')
    )
    $installedEmulationStation = $knownExecutables | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    $bridgeDirectory = Join-Path $env:USERPROFILE '.emulationstation\pix'
    if ($installedEmulationStation) {
        $portableData = Join-Path (Split-Path -Parent $installedEmulationStation) '.emulationstation'
        if (Test-Path -LiteralPath $portableData -PathType Container) { $bridgeDirectory = Join-Path $portableData 'pix' }
    }
    $installedConfig = Join-Path $env:ProgramData 'TurboRama\PixAgent\appsettings.json'
    if (Test-Path -LiteralPath $installedConfig -PathType Leaf) {
        $configuration = Get-Content -LiteralPath $installedConfig -Raw | ConvertFrom-Json
        $configuredBridge = [Environment]::ExpandEnvironmentVariables([string]$configuration.TurboRamaPix.BridgeDirectory)
        if ($configuredBridge -and -not $configuredBridge.Equals($bridgeDirectory, [StringComparison]::OrdinalIgnoreCase)) {
            throw "O agente e o EmulationStation usam pastas diferentes. Execute CORRIGIR-PASTA-PIX.cmd. Agente: $configuredBridge | EmulationStation: $bridgeDirectory"
        }
    }
    $statusFile = Join-Path $bridgeDirectory 'agent-status.json'
    if (-not (Test-Path $statusFile)) { throw 'O servico PIX ainda nao publicou estado.' }
    $status = Get-Content -LiteralPath $statusFile -Raw | ConvertFrom-Json
    $updated = [DateTimeOffset]::FromUnixTimeSeconds([long]$status.updatedAtUnixSeconds)
    $age = [DateTimeOffset]::UtcNow - $updated
    if ($status.provider -notin @('mercadopago','adapter')) { throw 'O provedor comercial ativo nao e valido.' }
    if (-not $status.ready) { throw 'O agente nao esta pronto. Verifique a credencial e a configuracao do provedor.' }
    if ($age.TotalSeconds -gt 120) { throw 'O agente esta sem responder ha mais de 2 minutos.' }
    Write-Host 'PIX COMERCIAL ONLINE E PRONTO.' -ForegroundColor Green
    Write-Host ('Provedor: ' + $status.provider)
    Write-Host ('Ultima resposta: ' + $updated.ToLocalTime().ToString('dd/MM/yyyy HH:mm:ss'))
    Write-Host ('Pasta compartilhada: ' + $bridgeDirectory)
    Write-Host 'Menu do cliente: START > COMPRAR TEMPO COM PIX'
}
catch {
    Write-Host ('PIX INDISPONIVEL: ' + $_.Exception.Message) -ForegroundColor Red
}
Write-Host ''
Read-Host 'Pressione ENTER para fechar'
