#Requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$catalog = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'prerequisites.lock.json') -Raw | ConvertFrom-Json
$destination = Join-Path $PSScriptRoot 'resources\prerequisites'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
if ((Get-Item -LiteralPath $destination).Attributes -band [IO.FileAttributes]::ReparsePoint) {
    throw 'Diretorio de pacotes em reparse point recusado.'
}
foreach ($payload in $catalog.payloads) {
    if ([IO.Path]::GetFileName($payload.name) -ne $payload.name -or $payload.name -match '[\\/:]') {
        throw 'Nome de pacote invalido.'
    }
    $target = Join-Path $destination $payload.name
    if (Test-Path -LiteralPath $target) {
        $existing = Get-Item -LiteralPath $target
        if ($existing.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Pacote em reparse point recusado.' }
        if ($existing.Length -ne $payload.length -or (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $payload.sha256) {
            throw ('Pacote local difere do catalogo: ' + $payload.name)
        }
        Write-Output ('CACHE verificado: ' + $payload.name)
        continue
    }
    $restored = $false
    foreach ($source in $payload.sourceUrls) {
        $uri = [Uri]$source
        if (-not $uri.IsAbsoluteUri -or $uri.Scheme -ne 'https') { throw 'Fonte sem HTTPS recusada.' }
        $temporary = Join-Path $destination ([Guid]::NewGuid().ToString('N') + '.download')
        try {
            Invoke-WebRequest -UseBasicParsing -Uri $uri -OutFile $temporary -TimeoutSec 600
            if ((Get-Item -LiteralPath $temporary).Length -ne $payload.length -or
                (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $payload.sha256) {
                throw 'Tamanho ou SHA-256 diferente do catalogo; arquivo recusado.'
            }
            Move-Item -LiteralPath $temporary -Destination $target
            $restored = $true
            Write-Output ('DOWNLOAD verificado: ' + $payload.name)
            break
        }
        catch { Write-Warning ($payload.name + ': ' + $_.Exception.Message) }
        finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
    }
    if (-not $restored) {
        throw ('Nao foi possivel restaurar a versao exata de ' + $payload.name + '. O catalogo nao foi alterado.')
    }
}
Write-Output ('Pacotes restaurados e verificados: ' + $catalog.payloads.Count)
