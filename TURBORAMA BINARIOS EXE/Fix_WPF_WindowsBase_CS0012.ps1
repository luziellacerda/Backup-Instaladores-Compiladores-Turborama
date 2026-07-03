param(
    [string]$ProjectRoot = ""
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

function Add-ReferenceIfMissing {
    param(
        [string]$Content,
        [string]$ReferenceName
    )

    $escaped = [regex]::Escape($ReferenceName)

    if ($Content -match "<Reference\s+Include\s*=\s*`"$escaped(?:,.*?)?`"\s*/?>" -or
        $Content -match "<Reference\s+Include\s*=\s*'$escaped(?:,.*?)?'\s*/?>") {
        return $Content
    }

    $refLine = "    <Reference Include=`"$ReferenceName`" />"

    # Preferir inserir dentro de um ItemGroup que ja tenha referencias.
    $itemGroupPattern = "(?s)(<ItemGroup>\s*(?:<Reference\b.*?</Reference>\s*|<Reference\b[^>]*/>\s*)+)(</ItemGroup>)"
    if ($Content -match $itemGroupPattern) {
        return [regex]::Replace($Content, $itemGroupPattern, {
            param($m)
            return $m.Groups[1].Value + $refLine + "`r`n  " + $m.Groups[2].Value
        }, 1)
    }

    # Se nao existir ItemGroup de referencias, criar antes do primeiro Import.
    $newGroup = "  <ItemGroup>`r`n$refLine`r`n  </ItemGroup>`r`n"
    if ($Content -match "(?s)(\s*<Import\b)") {
        return [regex]::Replace($Content, "(?s)(\s*<Import\b)", "`r`n$newGroup`$1", 1)
    }

    # Ultimo caso: inserir antes do fechamento Project.
    return $Content -replace "</Project>", "$newGroup</Project>"
}

try {
    Clear-Host
    Write-Host ""
    Write-Host "======================================================="
    Write-Host " TURBORAMA - FIX CS0012 WindowsBase / WPF"
    Write-Host "======================================================="
    Write-Host ""

    if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
        $ProjectRoot = Get-Location
    }

    $ProjectRoot = [System.IO.Path]::GetFullPath($ProjectRoot)

    $csproj = Get-ChildItem -LiteralPath $ProjectRoot -Recurse -Filter "RetroBat.csproj" -File | Select-Object -First 1
    if ($csproj -eq $null) {
        $csproj = Get-ChildItem -LiteralPath $ProjectRoot -Recurse -Filter "*.csproj" -File | Select-Object -First 1
    }

    if ($csproj -eq $null) {
        throw "Nao encontrei nenhum .csproj. Rode dentro da pasta onde esta RetroBat.sln."
    }

    Info "Projeto encontrado: $($csproj.FullName)"

    Copy-Item -LiteralPath $csproj.FullName -Destination ($csproj.FullName + ".bak_wpf_cs0012") -Force
    Ok "Backup criado."

    $proj = [System.IO.File]::ReadAllText($csproj.FullName)

    # Garante references necessarias para WinForms + WPF MediaElement/ElementHost.
    $needed = @(
        "System",
        "System.Core",
        "System.Drawing",
        "System.Windows.Forms",
        "System.Xaml",
        "WindowsBase",
        "PresentationCore",
        "PresentationFramework",
        "WindowsFormsIntegration"
    )

    foreach ($r in $needed) {
        $before = $proj
        $proj = Add-ReferenceIfMissing -Content $proj -ReferenceName $r
        if ($proj -ne $before) {
            Ok "Referencia adicionada: $r"
        }
        else {
            Info "Referencia ja existe: $r"
        }
    }

    # Para projeto legacy, garantir TargetFrameworkVersion.
    if ($proj -notmatch "<TargetFrameworkVersion>") {
        $proj = [regex]::Replace($proj, "(<PropertyGroup[^>]*>)", "`$1`r`n    <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>", 1)
        Warn "TargetFrameworkVersion nao existia. Adicionado v4.8."
    }
    else {
        Info "TargetFrameworkVersion ja existe. Nao alterei."
    }

    # Nao converter para SDK style. Nao adicionar UseWPF no projeto legacy.
    SaveText $csproj.FullName $proj

    # Limpar cache de build.
    $projectDir = $csproj.Directory.FullName
    foreach ($folder in @("bin", "obj")) {
        $target = Join-Path $projectDir $folder
        if (Test-Path $target) {
            Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction SilentlyContinue
            Ok "Removido: $target"
        }
    }

    $slnDir = Split-Path -Parent $ProjectRoot
    $vsFolder = Join-Path $ProjectRoot ".vs"
    if (Test-Path $vsFolder) {
        Remove-Item -LiteralPath $vsFolder -Recurse -Force -ErrorAction SilentlyContinue
        Ok "Removido: $vsFolder"
    }

    Write-Host ""
    Write-Host "======================================================="
    Write-Host " CORRIGIDO"
    Write-Host " Abra a solution e compile:"
    Write-Host " Release | Any CPU"
    Write-Host "======================================================="
    Write-Host ""
    Write-Host "Se ainda der CS0012, instale no Visual Studio Installer:"
    Write-Host " - .NET desktop development"
    Write-Host " - .NET Framework 4.8 Developer Pack / Targeting Pack"
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
