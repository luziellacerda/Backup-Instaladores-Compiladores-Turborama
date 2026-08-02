param(
    [Parameter(Mandatory = $true)][string]$AgentDirectory,
    [Parameter(Mandatory = $true)][string]$WorkDirectory,
    [int]$Port = 18765
)

$ErrorActionPreference = 'Stop'
$agent = Join-Path $AgentDirectory 'TurboRamaPixAgent.exe'
if (-not (Test-Path -LiteralPath $agent -PathType Leaf)) { throw 'Agente de teste nao encontrado.' }

$testRoot = Join-Path $WorkDirectory ('adapter-e2e-' + [Guid]::NewGuid().ToString('N'))
$bridge = Join-Path $testRoot 'bridge'
New-Item -ItemType Directory -Force -Path $testRoot, $bridge | Out-Null

$configuration = @{
    TurboRamaPix = @{
        Provider = 'adapter'
        BridgeDirectory = $bridge
        AllowedMinutes = @(15, 30, 45, 60, 120)
        PackagePricesCents = @{ '15' = 750; '30' = 1500; '45' = 2250; '60' = 3000; '120' = 6000 }
        PollSeconds = 2
        PaymentExpirationMinutes = 15
        HttpTimeoutSeconds = 10
        MaxRetrySeconds = 30
        ProductionEnabled = $true
        MercadoPago = @{ ExternalPosId = 'NAO-USADO'; DescriptionPrefix = 'Tempo TurboRama' }
        Adapter = @{ BaseUrl = "http://127.0.0.1:$Port/"; ProviderId = 'banco-teste' }
    }
}
$configuration | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $AgentDirectory 'appsettings.json') -Encoding UTF8

$sharedSecret = 'turborama-adapter-e2e-secret'
$server = Start-Job -ArgumentList $Port, $sharedSecret -ScriptBlock {
    param($ListenPort, $ExpectedSecret)
    $listener = New-Object System.Net.HttpListener
    $listener.Prefixes.Add("http://127.0.0.1:$ListenPort/")
    $listener.Start()
    $orders = @{}

    function Send-Json($context, [int]$status, $value) {
        $json = $value | ConvertTo-Json -Depth 10 -Compress
        $bytes = [Text.Encoding]::UTF8.GetBytes($json)
        $context.Response.StatusCode = $status
        $context.Response.ContentType = 'application/json; charset=utf-8'
        $context.Response.ContentLength64 = $bytes.Length
        $context.Response.OutputStream.Write($bytes, 0, $bytes.Length)
        $context.Response.Close()
    }

    try {
        while ($listener.IsListening) {
            $context = $listener.GetContext()
            $path = $context.Request.Url.AbsolutePath
            if ($path -eq '/shutdown') { Send-Json $context 200 @{ stopped = $true }; break }
            if ($context.Request.Headers['Authorization'] -ne "Bearer $ExpectedSecret") {
                Send-Json $context 401 @{ message = 'credencial invalida' }
                continue
            }
            if ($context.Request.HttpMethod -eq 'GET' -and $path -eq '/v1/health') {
                Send-Json $context 200 @{ schemaVersion = 1; providerId = 'banco-teste'; ready = $true }
                continue
            }
            if ($context.Request.HttpMethod -eq 'POST' -and $path -eq '/v1/orders') {
                $reader = New-Object IO.StreamReader($context.Request.InputStream, [Text.Encoding]::UTF8)
                $request = $reader.ReadToEnd() | ConvertFrom-Json
                $reference = [string]$request.externalReference
                if ($context.Request.Headers['X-Idempotency-Key'] -ne $reference -or [long]$request.amountCents -ne 750 -or $request.currency -ne 'BRL') {
                    Send-Json $context 400 @{ message = 'pedido divergente' }
                    continue
                }
                if (-not $orders.ContainsKey($reference)) {
                    $orders[$reference] = @{ orderId = "BANK_$reference"; reference = $reference; amount = [long]$request.amountCents; polls = 0 }
                }
                $order = $orders[$reference]
                Send-Json $context 201 @{
                    schemaVersion = 1; providerId = 'banco-teste'; providerOrderId = $order.orderId
                    externalReference = $order.reference; amountCents = $order.amount; currency = 'BRL'
                    qrData = "00020126580014BR.GOV.BCB.PIX0136$reference-TURBORAMA-TESTE"; status = 'pending'
                }
                continue
            }
            if ($context.Request.HttpMethod -eq 'GET' -and $path -match '^/v1/orders/([A-Za-z0-9_-]+)$') {
                $order = $orders.Values | Where-Object { $_.orderId -eq $Matches[1] } | Select-Object -First 1
                if (-not $order) { Send-Json $context 404 @{ message = 'order nao encontrada' }; continue }
                $order.polls = [int]$order.polls + 1
                $status = if ($order.polls -ge 2) { 'approved' } else { 'pending' }
                Send-Json $context 200 @{
                    schemaVersion = 1; providerId = 'banco-teste'; providerOrderId = $order.orderId
                    externalReference = $order.reference; amountCents = $order.amount; currency = 'BRL'
                    qrData = "00020126580014BR.GOV.BCB.PIX0136$($order.reference)-TURBORAMA-TESTE"; status = $status
                }
                continue
            }
            Send-Json $context 404 @{ message = 'rota inexistente' }
        }
    }
    finally { $listener.Stop(); $listener.Close() }
}

try {
    $serverReady = $false
    for ($attempt = 0; $attempt -lt 30 -and -not $serverReady; $attempt++) {
        if ($server.State -eq 'Failed') {
            $serverFailure = [string]$server.ChildJobs[0].JobStateInfo.Reason
            throw "Servidor bancario de teste nao iniciou. $serverFailure"
        }
        try {
            $health = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/v1/health" -Headers @{ Authorization = "Bearer $sharedSecret" } -TimeoutSec 1
            $serverReady = $health.ready -eq $true
        }
        catch { Start-Sleep -Milliseconds 250 }
    }
    if (-not $serverReady) { throw 'Servidor bancario de teste nao abriu a porta local.' }
    $env:TURBORAMA_PIX_PROVIDER_SECRET = $sharedSecret
    & $agent --check-provider
    if ($LASTEXITCODE -ne 0) { throw "Health check falhou: $LASTEXITCODE" }

    $requestId = 'PIXADAPTERE2E'
    $requestDirectory = Join-Path $bridge 'requests'
    New-Item -ItemType Directory -Force -Path $requestDirectory | Out-Null
    $request = @{
        id = $requestId
        minutes = 15
        amountCents = 750
        requestedAt = [DateTimeOffset]::UtcNow.ToString('o')
    }
    $request | ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $requestDirectory "$requestId.request.json") -Encoding UTF8

    & $agent --once
    if ($LASTEXITCODE -ne 0) { throw "Criacao da cobranca falhou: $LASTEXITCODE" }
    $qrFile = Join-Path $bridge "qr\$requestId.png"
    $sessionFile = Join-Path $bridge "sessions\$requestId.session.json"
    if (-not (Test-Path -LiteralPath $qrFile) -or (Get-Item -LiteralPath $qrFile).Length -lt 500) { throw 'QR PNG nao foi gerado.' }
    $session = Get-Content -LiteralPath $sessionFile -Raw | ConvertFrom-Json
    if ($session.status -ne 'pending' -or $session.provider -ne 'adapter') { throw 'Sessao pendente invalida.' }

    & $agent --once
    if ($LASTEXITCODE -ne 0) { throw "Confirmacao bancaria falhou: $LASTEXITCODE" }
    $creditFile = Join-Path $bridge "approved\$requestId.credit.json"
    if (-not (Test-Path -LiteralPath $creditFile)) { throw 'Credito assinado nao foi publicado.' }
    $credit = Get-Content -LiteralPath $creditFile -Raw | ConvertFrom-Json
    if ($credit.provider -ne 'adapter' -or [int]$credit.minutes -ne 15 -or [long]$credit.amountCents -ne 750 -or $credit.signature -notmatch '^[a-f0-9]{64}$') {
        throw 'Credito publicado contem dados invalidos.'
    }
    if (Test-Path -LiteralPath $qrFile) { throw 'QR nao foi removido depois da aprovacao.' }

    $hashBefore = (Get-FileHash -LiteralPath $creditFile -Algorithm SHA256).Hash
    & $agent --once
    if ($LASTEXITCODE -ne 0) { throw "Teste de idempotencia falhou: $LASTEXITCODE" }
    $hashAfter = (Get-FileHash -LiteralPath $creditFile -Algorithm SHA256).Hash
    if ($hashBefore -ne $hashAfter) { throw 'Credito foi alterado ou duplicado apos conclusao.' }

    $publicOptions = Get-Content -LiteralPath (Join-Path $bridge 'public-options.json') -Raw | ConvertFrom-Json
    if (-not $publicOptions.ready -or $publicOptions.provider -ne 'adapter') { throw 'Contrato publico nao ficou pronto.' }
    Write-Host "ADAPTADOR E2E: OK | QR, aprovacao, assinatura e idempotencia | $testRoot"
}
finally {
    try { Invoke-RestMethod -Uri "http://127.0.0.1:$Port/shutdown" -TimeoutSec 2 | Out-Null } catch { }
    Wait-Job -Job $server -Timeout 5 | Out-Null
    Stop-Job -Job $server -ErrorAction SilentlyContinue
    Remove-Job -Job $server -Force -ErrorAction SilentlyContinue
    Remove-Item Env:TURBORAMA_PIX_PROVIDER_SECRET -ErrorAction SilentlyContinue
}
