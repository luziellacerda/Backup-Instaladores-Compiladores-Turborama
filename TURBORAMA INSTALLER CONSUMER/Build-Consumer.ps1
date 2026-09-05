#Requires -Version 5.1
[CmdletBinding()]
param([string]$MSBuildPath = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$heldInputStreams = New-Object System.Collections.Generic.List[System.IO.FileStream]
Push-Location $PSScriptRoot
try {
    $repository = (& git -c "safe.directory=$((Get-Item $PSScriptRoot).Parent.FullName)" rev-parse --show-toplevel)
    if ($LASTEXITCODE -ne 0) { throw 'Compile a cópia do projeto versionada no Git.' }
    $commitOutput = @(& git -c "safe.directory=$repository" rev-parse HEAD)
    if ($LASTEXITCODE -ne 0 -or $commitOutput.Count -ne 1) { throw 'Não foi possível fixar o commit de origem.' }
    $commit = ([string]$commitOutput[0]).Trim()
    $dirty = @(& git -c "safe.directory=$repository" status --porcelain -- .)
    if ($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) { throw 'O projeto deve estar commitado antes da compilação de entrega.' }
    [xml]$project = Get-Content -LiteralPath 'InstallerHost.csproj' -Raw
    $compileInputs = @($project.SelectNodes('//*[local-name()="Compile"]') | ForEach-Object { $_.GetAttribute('Include') })
    $testInputs = @(Get-ChildItem -LiteralPath 'Tests' -Filter '*.cs' -File | ForEach-Object { 'Tests\' + $_.Name })
    $artInputs = @($project.SelectNodes('//*[local-name()="EmbeddedResource"]') | ForEach-Object { $_.GetAttribute('Include') } | Where-Object { $_ -notlike 'resources\prerequisites\*' })
    $inputs = @($compileInputs + $testInputs + $artInputs + @('InstallerHost.csproj','app.manifest','prerequisites.lock.json','resources\Builder.ico','Build-Consumer.ps1','Verify-ConsumerBuild.ps1','Test-ConsumerUi.ps1') | Sort-Object -Unique)
    $projectRoot = [System.IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\', '/')
    $projectPrefix = $projectRoot + [System.IO.Path]::DirectorySeparatorChar
    foreach ($inputPath in $inputs) {
        $fullInputPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $inputPath))
        if (-not $fullInputPath.StartsWith($projectPrefix, [System.StringComparison]::OrdinalIgnoreCase)) { throw ('Input fora do projeto: ' + $inputPath) }
        $inputItem = Get-Item -LiteralPath $fullInputPath -Force
        if (($inputItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw ('Input em reparse point: ' + $inputPath) }
        $tracked = @(& git -c "safe.directory=$repository" --literal-pathspecs ls-files --error-unmatch -- $inputPath)
        if ($LASTEXITCODE -ne 0 -or $tracked.Count -ne 1) { throw ('Input não pertence ao commit: ' + $inputPath) }
        $heldInputStreams.Add([System.IO.File]::Open(
            $fullInputPath,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::Read)) | Out-Null
    }
    $catalog = Get-Content -LiteralPath 'prerequisites.lock.json' -Raw | ConvertFrom-Json
    $payloadPaths = @($catalog.payloads | ForEach-Object { Join-Path 'resources\prerequisites' $_.name })
    foreach ($payloadPath in $payloadPaths) {
        $fullPayloadPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $payloadPath))
        if (-not $fullPayloadPath.StartsWith($projectPrefix, [System.StringComparison]::OrdinalIgnoreCase)) { throw ('Payload fora do projeto: ' + $payloadPath) }
        $payloadItem = Get-Item -LiteralPath $fullPayloadPath -Force
        if (($payloadItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw ('Payload em reparse point: ' + $payloadPath) }
        $heldInputStreams.Add([System.IO.File]::Open(
            $fullPayloadPath,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::Read)) | Out-Null
    }
    $sourceHashes = @{}
    foreach ($inputPath in $inputs) { $sourceHashes[$inputPath] = (Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash }
    foreach ($payload in $catalog.payloads) {
        $path = Join-Path 'resources\prerequisites' $payload.name
        if ((Get-Item -LiteralPath $path).Length -ne $payload.length -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $payload.sha256) { throw ('Payload difere do lock: ' + $payload.name) }
        if ($payload.fileType -ne 'Zip') {
            $signature = Get-AuthenticodeSignature -LiteralPath $path
            if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $payload.signerThumbprint) { throw ('Assinatura inválida: ' + $payload.name) }
        }
    }
    & .\Test-ConsumerUi.ps1 -MSBuildPath $MSBuildPath
    if ($LASTEXITCODE -ne 0) { throw 'Testes internos falharam.' }
    & $MSBuildPath InstallerHost.csproj /t:Rebuild /p:Configuration=Release /p:IncludePrerequisitePayloads=true /nologo /v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Compilação do instalador falhou.' }
    $builtExePath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'bin\Release\InstallerHost.exe'))
    $builtExeItem = Get-Item -LiteralPath $builtExePath -Force
    if (($builtExeItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Executável produzido em reparse point; artefato recusado.' }
    $heldInputStreams.Add([System.IO.File]::Open(
        $builtExePath,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::Read)) | Out-Null
    & .\Verify-ConsumerBuild.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Verificação do executável falhou.' }
    foreach ($inputPath in $inputs) {
        if ((Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash -ne $sourceHashes[$inputPath]) { throw ('Fonte mudou durante a compilação: ' + $inputPath) }
    }
    $dirty = @(& git -c "safe.directory=$repository" status --porcelain -- .)
    if ($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) { throw 'Projeto alterado ou Git indisponível durante a compilação.' }
    $finalCommitOutput = @(& git -c "safe.directory=$repository" rev-parse HEAD)
    if ($LASTEXITCODE -ne 0 -or $finalCommitOutput.Count -ne 1 -or
        -not ([string]$finalCommitOutput[0]).Trim().Equals($commit, [System.StringComparison]::Ordinal)) {
        throw 'HEAD mudou durante a compilação; artefato recusado.'
    }
    $exe = Get-Item -LiteralPath $builtExePath
    $manifest = [ordered]@{
        schemaVersion = 1; sourceCommit = $commit; sourceClean = $true
        createdUtc = [DateTime]::UtcNow.ToString('o'); version = $exe.VersionInfo.FileVersion
        file = $exe.Name; length = $exe.Length; sha256 = (Get-FileHash -LiteralPath $exe.FullName -Algorithm SHA256).Hash
        catalogSha256 = $sourceHashes['prerequisites.lock.json']; sourceHashes = $sourceHashes
        authenticodeStatus = [string](Get-AuthenticodeSignature -LiteralPath $exe.FullName).Status
        internalTestsPassed = $true; realWindowQaPassed = $false; cleanWindowsInstallPassed = $false
        productPackageIncluded = $false; productionApproved = $false
        containsPrereleaseComponents = $true; prereleaseComponents = @('WinFsp 2026 Beta4 (2.2.26215), optional and unchecked')
        warning = 'Candidato interno para testes, com WinFsp Beta opcional; confirmar inclusão antes da entrega. Componentes reais incorporados; requer partes do produto e sidecar para instalar TurboRama. Não aprovado para produção.'
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath 'bin\Release\InstallerHost-build-manifest.json' -Encoding UTF8
    ($manifest.sha256 + ' *InstallerHost.exe') | Set-Content -LiteralPath 'bin\Release\InstallerHost.exe.sha256' -Encoding ASCII
    Write-Output ('EXE de teste: ' + $exe.FullName)
} finally {
    for ($streamIndex = $heldInputStreams.Count - 1; $streamIndex -ge 0; $streamIndex--) {
        $heldInputStreams[$streamIndex].Dispose()
    }
    Pop-Location
}
