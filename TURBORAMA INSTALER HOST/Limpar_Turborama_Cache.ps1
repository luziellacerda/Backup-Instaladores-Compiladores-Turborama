param(
    [string]$ProjectRoot = $PSScriptRoot
)

$ErrorActionPreference = "Continue"

function Write-Ok($msg) {
    Write-Host "[OK] $msg" -ForegroundColor Green
}

function Write-Info($msg) {
    Write-Host "[INFO] $msg" -ForegroundColor Cyan
}

function Write-Warn($msg) {
    Write-Host "[AVISO] $msg" -ForegroundColor Yellow
}

function Remove-SafeFolder($path) {
    if (Test-Path -LiteralPath $path) {
        try {
            Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop
            Write-Ok "Removido: $path"
        }
        catch {
            Write-Warn "Nao consegui remover: $path"
            Write-Warn $_.Exception.Message
        }
    }
    else {
        Write-Info "Nao existe, ignorado: $path"
    }
}

Write-Host ""
Write-Host "==============================================="
Write-Host "  TURBORAMA CLEANER - LIMPEZA DE COMPILACAO"
Write-Host "==============================================="
Write-Host ""

$ProjectRoot = [System.IO.Path]::GetFullPath($ProjectRoot)

if (-not (Test-Path -LiteralPath (Join-Path $ProjectRoot "InstallerHost.sln"))) {
    Write-Host "ERRO: InstallerHost.sln nao encontrado em:" -ForegroundColor Red
    Write-Host $ProjectRoot -ForegroundColor Red
    exit 1
}

Write-Info "Projeto: $ProjectRoot"

# 1. Remove marca de arquivo baixado da internet
Write-Info "Removendo bloqueio de internet dos arquivos..."
Get-ChildItem -LiteralPath $ProjectRoot -Recurse -File -Force -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        Unblock-File -LiteralPath $_.FullName -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $_.FullName -Stream Zone.Identifier -ErrorAction SilentlyContinue
    }
    catch {
        # Ignora arquivo sem stream ou bloqueado por outro processo
    }
}
Write-Ok "Bloqueios removidos."

# 2. Remove cache do Visual Studio
Remove-SafeFolder (Join-Path $ProjectRoot ".vs")

# 3. Remove bin/obj de todos os projetos
Write-Info "Procurando pastas bin e obj..."
Get-ChildItem -LiteralPath $ProjectRoot -Recurse -Directory -Force -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -in @("bin", "obj") } |
    Sort-Object FullName -Descending |
    ForEach-Object {
        Remove-SafeFolder $_.FullName
    }

# 4. Remove arquivos temporarios comuns sem tocar nos recursos
Write-Info "Removendo temporarios comuns..."
Get-ChildItem -LiteralPath $ProjectRoot -Recurse -File -Force -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Extension -in @(".tmp", ".cache", ".log", ".tlog", ".lastbuildstate") -or
        $_.Name -like "*.FileListAbsolute.txt"
    } |
    ForEach-Object {
        try {
            Remove-Item -LiteralPath $_.FullName -Force -ErrorAction Stop
            Write-Ok "Removido temporario: $($_.FullName)"
        }
        catch {
            Write-Warn "Nao consegui remover temporario: $($_.FullName)"
        }
    }

Write-Host ""
Write-Host "==============================================="
Write-Host " LIMPEZA CONCLUIDA"
Write-Host " Abra o Visual Studio e compile em Release | Any CPU"
Write-Host "==============================================="
Write-Host ""
