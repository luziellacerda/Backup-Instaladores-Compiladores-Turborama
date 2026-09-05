#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'TestResults\executables') -Force | Out-Null
    $consumerBuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
    & $consumerBuild InstallerHost.csproj /t:Rebuild /p:Configuration=Release /p:IncludePrerequisitePayloads=false /nologo /v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação da biblioteca de validação.' }
    [xml]$consumerProject = Get-Content InstallerHost.csproj -Raw
    $consumerSources = @($consumerProject.SelectNodes('//*[local-name()="Compile"]') | ForEach-Object { $_.GetAttribute('Include') })
    $consumerCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $consumerCompiler /nologo /target:exe /warnaserror+ /define:CONSUMER_UI_TESTS /main:InstallerHost.ConsumerUiTests /out:TestResults\executables\ConsumerUiTests.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:Accessibility.dll /resource:prerequisites.lock.json,InstallerHost.prerequisites.lock.json $consumerSources Tests\RuntimeVersionPolicyTests.cs Tests\PrerequisiteSelectionTests.cs Tests\ConsumerUiTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação dos testes de interface.' }
    & .\TestResults\executables\ConsumerUiTests.exe (Join-Path $PSScriptRoot 'TestResults\consumer')
    if ($LASTEXITCODE -ne 0) { throw 'Falha nos testes de interface.' }
    & $consumerCompiler /nologo /target:exe /warnaserror+ /main:InstallerHost.GamingReadinessDialogTests /out:TestResults\executables\GamingReadinessDialogTests.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:Accessibility.dll /resource:prerequisites.lock.json,InstallerHost.prerequisites.lock.json $consumerSources Tests\GamingReadinessDialogTests.cs
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
    Write-Output 'Validação interna concluída; não é instalador distribuível nem teste de instalação real.'
}
finally { Pop-Location }
