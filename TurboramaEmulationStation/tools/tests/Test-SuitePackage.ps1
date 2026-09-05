param(
    [Parameter(Mandatory=$true)][string]$PackageDirectory,
    [ValidateSet(0,21)][int]$ExpectedProbeExit = 21
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
$exe = Join-Path $package 'emulationstation.exe'
$helper = Join-Path $package 'TurboRama.Suite.Access.exe'
if (!(Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'EXE ausente.' }
if (Test-Path -LiteralPath $helper) {
    throw 'Use staging limpo, sem helper adjacente. Nenhum arquivo existente sera removido.'
}

function Assert-DiagnosticExit([string]$Path, [string]$Argument, [int]$Expected) {
    $process = Start-Process -FilePath $Path -WorkingDirectory $package `
        -ArgumentList $Argument -WindowStyle Hidden -PassThru
    try {
        if (!$process.WaitForExit(30000)) {
            $process.Kill()
            $process.WaitForExit()
            throw "Teste $Argument excedeu o prazo."
        }
        if ($process.ExitCode -ne $Expected) {
            throw "$Argument : esperado $Expected, recebido $($process.ExitCode)."
        }
    }
    finally { $process.Dispose() }
}

. (Join-Path $PSScriptRoot 'Get-SuiteEmbeddedPayload.ps1')

$originalHash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
Assert-DiagnosticExit $exe '--suite-access-integrity-self-test' 0
Assert-DiagnosticExit $exe '--suite-access-probe-identity' $ExpectedProbeExit

# A bogus adjacent module must not be selected or executed. Only remove the
# marker this test successfully created; never replace a user's helper.
$markerCreated = $false
try {
    $marker = [IO.File]::Open($helper, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $markerCreated = $true
    try { $marker.Write([byte[]]@(0,1,2,3), 0, 4) }
    finally { $marker.Dispose() }
    Assert-DiagnosticExit $exe '--suite-access-integrity-self-test' 0
    Assert-DiagnosticExit $exe '--suite-access-probe-identity' $ExpectedProbeExit
}
finally { if ($markerCreated) { Remove-Item -LiteralPath $helper -Force } }

# Corrupt a uniquely named test COPY, never the binary that will be released.
$altered = Join-Path $package ('suite-integrity-test-' + [Guid]::NewGuid().ToString('N') + '.exe')
$copyCreated = $false
try {
    [IO.File]::Copy($exe, $altered, $false)
    $copyCreated = $true
    $payload = Get-SuiteEmbeddedPayload $altered
    $position = $payload.Offset + $payload.Size - 1
    $stream = [IO.File]::Open($altered, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $stream.Position = $position
        $originalByte = $stream.ReadByte()
        $stream.Position = $position
        $stream.WriteByte([byte]($originalByte -bxor 1))
    }
    finally { $stream.Dispose() }
    Assert-DiagnosticExit $altered '--suite-access-integrity-self-test' 44
    Assert-DiagnosticExit $altered '--suite-access-probe-identity' 44
}
finally { if ($copyCreated) { Remove-Item -LiteralPath $altered -Force } }
if ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -ne $originalHash) {
    throw 'EXE original foi alterado durante os testes.'
}
Assert-DiagnosticExit $exe '--suite-access-integrity-self-test' 0
'SUITE_EMBEDDED_PACKAGE=OK (embutido, adjacente ignorado, probe, payload adulterado rejeitado, original intacto)'
