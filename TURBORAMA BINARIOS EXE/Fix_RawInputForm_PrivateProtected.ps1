param(
    [string]$RawInputFormPath = ""
)

$ErrorActionPreference = "Stop"

function Ok($m) { Write-Host "[OK] $m" -ForegroundColor Green }
function Info($m) { Write-Host "[INFO] $m" -ForegroundColor Cyan }
function Warn($m) { Write-Host "[AVISO] $m" -ForegroundColor Yellow }
function Err($m) { Write-Host "[ERRO] $m" -ForegroundColor Red }

function SaveText($path, $text) {
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($path, $text, $utf8NoBom)
}

try {
    Clear-Host
    Write-Host ""
    Write-Host "======================================================="
    Write-Host " TURBORAMA - FIX RawInputForm.cs private protected"
    Write-Host "======================================================="
    Write-Host ""

    $here = [System.IO.Path]::GetFullPath((Get-Location).Path)

    if ([string]::IsNullOrWhiteSpace($RawInputFormPath)) {
        $candidates = @(
            (Join-Path $here "RawInputForm.cs"),
            (Join-Path $here "RetroBat\RawInputForm.cs"),
            (Join-Path $here "TurboramaLauncher\RetroBat\RawInputForm.cs"),
            (Join-Path $here "TurboramaLauncher\RawInputForm.cs")
        )

        foreach ($c in $candidates) {
            if (Test-Path -LiteralPath $c) {
                $RawInputFormPath = $c
                break
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($RawInputFormPath) -or !(Test-Path -LiteralPath $RawInputFormPath)) {
        Write-Host "Nao encontrei RawInputForm.cs automaticamente."
        Write-Host ""
        Write-Host "Cole o caminho completo do RawInputForm.cs."
        Write-Host "Exemplo:"
        Write-Host "C:\Users\LZ\Documents\TURBORAMA BINARIOS EXE\RetroBat\RawInputForm.cs"
        Write-Host ""
        $RawInputFormPath = Read-Host "Caminho do RawInputForm.cs"
    }

    if ([string]::IsNullOrWhiteSpace($RawInputFormPath) -or !(Test-Path -LiteralPath $RawInputFormPath)) {
        throw "RawInputForm.cs nao encontrado."
    }

    $RawInputFormPath = [System.IO.Path]::GetFullPath($RawInputFormPath)
    Info "Arquivo: $RawInputFormPath"

    $fileInfo = Get-Item -LiteralPath $RawInputFormPath
    if ($fileInfo.Length -gt 2MB) {
        throw "Esse arquivo esta grande demais para ser RawInputForm.cs. Voce escolheu o arquivo errado."
    }

    Copy-Item -LiteralPath $RawInputFormPath -Destination ($RawInputFormPath + ".bak_private_protected") -Force
    Ok "Backup criado."

    $t = [System.IO.File]::ReadAllText($RawInputFormPath)

    $old1 = "private protected bool RawInputDetected { protected get; private set; }"
    $new1 = "protected bool RawInputDetected { get; private set; }"

    $old2 = "private protected bool RawInputDetected"
    $new2 = "protected bool RawInputDetected"

    if ($t.Contains($old1)) {
        $t = $t.Replace($old1, $new1)
        Ok "Linha exata corrigida."
    }
    elseif ($t.Contains($old2)) {
        $t = $t.Replace($old2, $new2)
        $t = $t.Replace("{ protected get; private set; }", "{ get; private set; }")
        Ok "Linha private protected corrigida."
    }
    else {
        Warn "Nao achei a linha exata private protected RawInputDetected."
    }

    # Corrige qualquer outro private protected que o decompilador tenha gerado.
    if ($t.Contains("private protected ")) {
        $t = $t.Replace("private protected ", "protected ")
        Warn "Outros private protected foram trocados por protected."
    }

    SaveText $RawInputFormPath $t

    Write-Host ""
    Ok "RawInputForm.cs corrigido."
    Write-Host ""
    Write-Host "Agora compile novamente:"
    Write-Host "Release | Any CPU"
    Write-Host ""
}
catch {
    Write-Host ""
    Err $_.Exception.Message
    Write-Host ""
}
finally {
    Write-Host "Pressione ENTER para fechar."
    Read-Host | Out-Null
}
