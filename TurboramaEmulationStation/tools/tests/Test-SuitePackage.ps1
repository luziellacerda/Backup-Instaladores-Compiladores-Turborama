param([Parameter(Mandatory=$true)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
$exe = Join-Path $package 'emulationstation.exe'
$helper = Join-Path $package 'TurboRama.Suite.Access.exe'
$backup = Join-Path $package 'TurboRama.Suite.Access.exe.integrity-test'
if (!(Test-Path -LiteralPath $exe) -or !(Test-Path -LiteralPath $helper)) {
    throw 'Pacote incompleto.'
}
if (Test-Path -LiteralPath $backup) { throw 'Backup de teste ja existe.' }
function Assert-IntegrityExit([int]$Expected) {
    $process = Start-Process -FilePath $exe -WorkingDirectory $package `
        -ArgumentList '--suite-access-integrity-self-test' -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(30000)) {
        $process.Kill()
        throw 'Teste de integridade excedeu o prazo.'
    }
    if ($process.ExitCode -ne $Expected) {
        throw "Integridade: esperado $Expected, recebido $($process.ExitCode)."
    }
}
Assert-IntegrityExit 0
# Move only the exact helper inside the explicitly selected staging directory.
Move-Item -LiteralPath $helper -Destination $backup
try { Assert-IntegrityExit 44 }
finally { Move-Item -LiteralPath $backup -Destination $helper }

$originalHash = (Get-FileHash -LiteralPath $helper -Algorithm SHA256).Hash
$position = (Get-Item -LiteralPath $helper).Length - 1
if ($position -lt 64) { throw 'Helper invalido.' }
$stream = [IO.File]::Open($helper, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    $stream.Position = $position
    $originalByte = $stream.ReadByte()
    $stream.Position = $position
    $stream.WriteByte([byte]($originalByte -bxor 1))
}
finally { $stream.Dispose() }
try { Assert-IntegrityExit 44 }
finally {
    $stream = [IO.File]::Open($helper, [IO.FileMode]::Open, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Position = $position; $stream.WriteByte([byte]$originalByte) }
    finally { $stream.Dispose() }
}
if ((Get-FileHash -LiteralPath $helper -Algorithm SHA256).Hash -ne $originalHash) {
    throw 'Helper nao restaurado apos teste.'
}
Assert-IntegrityExit 0
'SUITE_PACKAGE_INTEGRITY=OK (correto, ausente, alterado, restaurado)'
