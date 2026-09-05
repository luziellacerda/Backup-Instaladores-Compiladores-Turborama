#Requires -Version 5.1
[CmdletBinding()]
param([string]$MSBuildPath = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not (Test-Path -LiteralPath $MSBuildPath)) { throw 'Informe -MSBuildPath apontando para o MSBuild instalado.' }
$nextCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $nextCompiler)) { throw 'Compilador .NET Framework x64 não encontrado.' }
Push-Location $PSScriptRoot
try {
    & $MSBuildPath 'InstallerHost.Next.csproj' /t:Rebuild /p:Configuration=Release /nologo /v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Build da prévia falhou.' }
    $nextAssembly = Join-Path $PSScriptRoot 'bin\Release\TurboRama.Next.Preview.exe'
    foreach ($nextTest in @('SessionTests', 'ShellTests', 'RenderTests')) {
        $nextTestExe = Join-Path $PSScriptRoot ('bin\Release\' + $nextTest + '.exe')
        $nextTestSource = Join-Path $PSScriptRoot ('Tests\' + $nextTest + '.cs')
        & $nextCompiler /nologo /target:exe /warnaserror+ ('/out:' + $nextTestExe) /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ('/r:' + $nextAssembly) $nextTestSource
        if ($LASTEXITCODE -ne 0) { throw ('Compilação do teste falhou: ' + $nextTest) }
        if ($nextTest -eq 'RenderTests') { & $nextTestExe (Join-Path $PSScriptRoot 'TestResults\render') }
        else { & $nextTestExe }
        if ($LASTEXITCODE -ne 0) { throw ('Teste falhou: ' + $nextTest) }
    }
    Write-Output 'Prévia testada. Não é instalador final, não é certificação Windows, não publica artefatos.'
}
finally { Pop-Location }
