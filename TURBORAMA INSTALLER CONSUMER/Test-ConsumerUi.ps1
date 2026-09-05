#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$MSBuildPath = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe',
    [string]$RequireRuntime10X64Version = ''
)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'TestResults\executables') -Force | Out-Null
    $consumerBuild = $MSBuildPath
    & $consumerBuild InstallerHost.csproj /t:Rebuild /p:Configuration=Release /p:IncludePrerequisitePayloads=false /nologo /v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação da biblioteca de validação.' }
    [xml]$consumerProject = Get-Content InstallerHost.csproj -Raw
    $consumerSources = @($consumerProject.SelectNodes('//*[local-name()="Compile"]') | ForEach-Object { $_.GetAttribute('Include') })
    $interfaceResources = @($consumerProject.SelectNodes('//*[local-name()="EmbeddedResource"]') | Where-Object { $_.GetAttribute('Include') -notlike 'resources\prerequisites\*' } | ForEach-Object { '/resource:' + $_.GetAttribute('Include') + ',' + $_.SelectSingleNode('*[local-name()="LogicalName"]').InnerText })
    $consumerCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    foreach ($runtimeProbeArch in @('x86', 'x64')) {
        if ($runtimeProbeArch -eq 'x64' -and -not [Environment]::Is64BitOperatingSystem) { continue }
        $runtimeProbeBits = if ($runtimeProbeArch -eq 'x64') { 64 } else { 32 }
        $runtimeProbeFramework = if ($runtimeProbeArch -eq 'x64') { 'Framework64' } else { 'Framework' }
        $runtimeProbeCompiler = Join-Path $env:WINDIR ('Microsoft.NET\' + $runtimeProbeFramework + '\v4.0.30319\csc.exe')
        $runtimeProbeExe = 'TestResults\executables\DotNetDesktopDetectionTests.' + $runtimeProbeArch + '.exe'
        & $runtimeProbeCompiler /nologo /target:exe ('/platform:' + $runtimeProbeArch) /warnaserror+ /main:InstallerHost.DotNetDesktopDetectionTests ('/out:' + $runtimeProbeExe) /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll $interfaceResources $consumerSources Tests\DotNetDesktopDetectionTests.cs
        if ($LASTEXITCODE -ne 0) { throw ('Falha ao compilar teste .NET Desktop ' + $runtimeProbeArch) }
        $runtimeProbeArguments = @('--expect-process-bits=' + $runtimeProbeBits)
        if (-not [string]::IsNullOrWhiteSpace($RequireRuntime10X64Version)) {
            $runtimeProbeArguments += '--require-runtime10-x64=' + $RequireRuntime10X64Version
        }
        & (Join-Path $PSScriptRoot $runtimeProbeExe) @runtimeProbeArguments
        if ($LASTEXITCODE -ne 0) { throw ('Regressão na detecção real do .NET Desktop pelo processo ' + $runtimeProbeArch) }
    }
    & $consumerCompiler /nologo /target:exe /warnaserror+ /define:CONSUMER_UI_TESTS /main:InstallerHost.ConsumerUiTests /out:TestResults\executables\ConsumerUiTests.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:Accessibility.dll $interfaceResources $consumerSources Tests\RuntimeVersionPolicyTests.cs Tests\JavaRuntimeDetectorTests.cs Tests\PrerequisiteSelectionTests.cs Tests\ArtworkTests.cs Tests\PublisherPolicyTests.cs Tests\InstallationFlowPolicyTests.cs Tests\ConsumerUiTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação dos testes de interface.' }
    & .\TestResults\executables\ConsumerUiTests.exe (Join-Path $PSScriptRoot 'TestResults\consumer')
    if ($LASTEXITCODE -ne 0) { throw 'Falha nos testes de interface.' }
    & $consumerCompiler /nologo /target:exe /warnaserror+ /main:InstallerHost.GamingReadinessDialogTests /out:TestResults\executables\GamingReadinessDialogTests.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:Accessibility.dll $interfaceResources $consumerSources Tests\GamingReadinessDialogTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar teste do diagnóstico.' }
    & .\TestResults\executables\GamingReadinessDialogTests.exe (Join-Path $PSScriptRoot 'TestResults\diagnostic')
    if ($LASTEXITCODE -ne 0) { throw 'Falha no layout do diagnóstico.' }
    foreach ($consumerPaintTest in @('ButtonTests', 'PaintRegressionTests')) {
        $testExe = 'TestResults\executables\' + $consumerPaintTest + '.exe'
        & $consumerCompiler /nologo /target:exe /warnaserror+ ('/out:' + $testExe) /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:Accessibility.dll UiKit.cs ActionButton.cs VectorIcon.cs ('Tests\' + $consumerPaintTest + '.cs')
        if ($LASTEXITCODE -ne 0) { throw ('Falha ao compilar: ' + $consumerPaintTest) }
        if ($consumerPaintTest -eq 'ButtonTests') { & (Join-Path $PSScriptRoot $testExe) (Join-Path $PSScriptRoot 'TestResults\ButtonStates') }
        else { & (Join-Path $PSScriptRoot $testExe) }
        if ($LASTEXITCODE -ne 0) { throw ('Regressão visual: ' + $consumerPaintTest) }
    }
    $securityCompiler = Join-Path (Split-Path $consumerBuild) 'Roslyn\csc.exe'
    & $securityCompiler /nologo /target:exe /warnaserror+ /langversion:7.3 /define:PRODUCT_PACKAGE_SECURITY_TESTS /out:TestResults\executables\ProductPackageSecurityTests.exe /r:System.dll /r:System.Core.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ProductPackageSecurity.cs SecureProductExtraction.cs LimitedUserImpersonation.cs Tests\ProductPackageSecurityTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar testes de segurança.' }
    & .\TestResults\executables\ProductPackageSecurityTests.exe (Join-Path $PSScriptRoot 'TestResults\security')
    if ($LASTEXITCODE -ne 0) { throw 'Regressão de segurança do pacote.' }
    & $consumerCompiler /nologo /target:exe /warnaserror+ /out:TestResults\executables\SecureInstallerStagingTests.exe /r:System.dll /r:System.Core.dll SecureInstallerStaging.cs Logger.cs Tests\SecureInstallerStagingTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar teste do staging.' }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try { $elevated = ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) }
    finally { $identity.Dispose() }
    if ($elevated) {
        & .\TestResults\executables\SecureInstallerStagingTests.exe
        $stagingExit = $LASTEXITCODE
    } else {
        if ($env:GITHUB_ACTIONS -eq 'true') { throw 'O teste do staging requer runner Windows elevado.' }
        $stagingProcess = Start-Process -FilePath (Join-Path $PSScriptRoot 'TestResults\executables\SecureInstallerStagingTests.exe') -Verb RunAs -WindowStyle Hidden -Wait -PassThru
        $stagingExit = $stagingProcess.ExitCode
    }
    if ($stagingExit -ne 0) { throw 'Falha na criação, gravação ou limpeza do staging privado.' }
    Write-Output 'STAGING INTEGRATION PASS: private creation, SYSTEM owner, file verification, overwrite/traversal rejection and cleanup.'
    Write-Output 'Validação interna concluída; não é instalador distribuível nem teste de instalação real.'
}
finally { Pop-Location }
