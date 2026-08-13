#Requires -Version 5.1
<#
.SYNOPSIS
Emite, somente no computador privado de fabrica, uma licenca PIX vinculada ao TPM.

.DESCRIPTION
O pedido vem do quiosque e nao contem credenciais. A assinatura usa a chave
privada de um certificado no Windows, token ou HSM. A chave privada nunca e
exportada, copiada para o Git ou incluida no instalador do cliente.

Cada pedido emitido e gravado com flush duravel no registro de emissoes antes
da licenca ser publicada. Nunca apague esse registro: ele impede a reemissao
acidental ou concorrente do mesmo pedido. O registro contem somente IDs
publicos dos pedidos, sem credenciais PIX e sem chaves privadas.
#>
param(
    [string]$Pedido = '',
    [string]$Saida = '',
    [string]$CertificadoThumbprint = '',
    [ValidateSet('CurrentUser','LocalMachine')][string]$LocalCertificado = 'CurrentUser',
    [string]$RegistroEmissoes = '',
    [switch]$AutoTeste
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$project = Join-Path $PSScriptRoot `
    'TurboramaEmulationStation\tools\TurboRamaPixLicenseIssuer\TurboRamaPixLicenseIssuer.csproj'
if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "Projeto privado do emissor nao encontrado: $project"
}

$dotnet = (Get-Command dotnet.exe -ErrorAction Stop).Source
$dotnetHomeOverride = [Environment]::GetEnvironmentVariable('TURBORAMA_DOTNET_CLI_HOME')
if ([string]::IsNullOrWhiteSpace($dotnetHomeOverride)) {
    $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    if ([string]::IsNullOrWhiteSpace($localAppData)) {
        throw 'LOCALAPPDATA nao esta disponivel para a compilacao do emissor.'
    }
    $dotnetHomeOverride = Join-Path $localAppData 'TurboRama\license-issuer-dotnet'
}
$env:DOTNET_CLI_HOME = [IO.Path]::GetFullPath($dotnetHomeOverride)
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

if ($AutoTeste) {
    & $dotnet run --project $project -c Release -- --self-test
    exit $LASTEXITCODE
}

if ([string]::IsNullOrWhiteSpace($Pedido)) {
    throw 'Informe -Pedido com o arquivo de solicitacao gerado pelo quiosque.'
}
if ([string]::IsNullOrWhiteSpace($Saida)) {
    throw 'Informe -Saida com o novo arquivo de licenca.'
}
if ([string]::IsNullOrWhiteSpace($CertificadoThumbprint)) {
    throw 'Informe -CertificadoThumbprint do certificado privado da fabrica.'
}
if ([string]::IsNullOrWhiteSpace($RegistroEmissoes)) {
    $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    if ([string]::IsNullOrWhiteSpace($localAppData)) {
        throw 'LOCALAPPDATA nao esta disponivel para o registro de emissoes.'
    }
    $RegistroEmissoes = Join-Path $localAppData 'TurboRama\license-issuer\issued-requests.log'
}

$requestPath = [IO.Path]::GetFullPath($Pedido)
$outputPath = [IO.Path]::GetFullPath($Saida)
$ledgerPath = [IO.Path]::GetFullPath($RegistroEmissoes)
if (-not (Test-Path -LiteralPath $requestPath -PathType Leaf)) {
    throw "Pedido de ativacao nao encontrado: $requestPath"
}
if (Test-Path -LiteralPath $outputPath) {
    throw "A saida ja existe e nao sera sobrescrita: $outputPath"
}
$thumbprint = ($CertificadoThumbprint -replace '\s','').ToUpperInvariant()
if ($thumbprint -notmatch '^[0-9A-F]{40}$') {
    throw 'CertificadoThumbprint deve conter exatamente 40 digitos hexadecimais.'
}

& $dotnet run --project $project -c Release -- `
    --request $requestPath `
    --output $outputPath `
    --ledger $ledgerPath `
    --thumbprint $thumbprint `
    --store $LocalCertificado
exit $LASTEXITCODE
