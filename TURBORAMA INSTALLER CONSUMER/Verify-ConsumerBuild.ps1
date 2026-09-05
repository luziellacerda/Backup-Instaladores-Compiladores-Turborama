#Requires -Version 5.1
[CmdletBinding()]
param()
if ($PSVersionTable.PSEdition -ne 'Desktop') {
    # Scope is this verifier process only; no persistent ExecutionPolicy setting is changed.
    & (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') -NoProfile -ExecutionPolicy RemoteSigned -File $PSCommandPath
    exit $LASTEXITCODE
}
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$catalogPath = Join-Path $PSScriptRoot 'prerequisites.lock.json'
$catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
$exePath = Join-Path $PSScriptRoot 'bin\Release\InstallerHost.exe'
$assembly = [Reflection.Assembly]::ReflectionOnlyLoadFrom($exePath)
$resources = $assembly.GetManifestResourceNames()
if ($resources.Count -ne ($catalog.payloads.Count + 1)) { throw 'Quantidade inesperada de recursos incorporados.' }
$sha = [Security.Cryptography.SHA256]::Create()
try {
    foreach ($payload in $catalog.payloads) {
        $stream = $assembly.GetManifestResourceStream('InstallerHost.resources.prerequisites.' + $payload.name)
        if ($null -eq $stream) { throw ('Recurso ausente: ' + $payload.name) }
        try {
            $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')
            if ($stream.Length -ne $payload.length -or $hash -ne $payload.sha256) { throw ('Recurso incorreto: ' + $payload.name) }
        } finally { $stream.Dispose() }
        Write-Output ('PASS embedded ' + $payload.name)
    }
    $stream = $assembly.GetManifestResourceStream('InstallerHost.prerequisites.lock.json')
    try { $lockHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') } finally { $stream.Dispose() }
    if ($lockHash -ne (Get-FileHash -LiteralPath $catalogPath -Algorithm SHA256).Hash) { throw 'Lock incorporado difere do fonte.' }
} finally { $sha.Dispose() }
Write-Output 'PASS: 20 payloads e catálogo incorporados; inspeção somente leitura, sem executar o instalador.'
