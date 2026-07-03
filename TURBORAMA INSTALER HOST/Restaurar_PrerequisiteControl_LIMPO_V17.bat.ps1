param([string]$ProjectRoot = "")

$ErrorActionPreference = "Stop"

function Ok($m){ Write-Host "[OK] $m" -ForegroundColor Green }
function Info($m){ Write-Host "[INFO] $m" -ForegroundColor Cyan }
function Warn($m){ Write-Host "[AVISO] $m" -ForegroundColor Yellow }
function Err($m){ Write-Host "[ERRO] $m" -ForegroundColor Red }

function SaveText($path, $text) {
    $enc = New-Object System.Text.UTF8Encoding($false)
    $text = $text.Replace("`r`n","`n").Replace("`r","`n").Replace("`n","`r`n")
    [System.IO.File]::WriteAllText($path, $text, $enc)
}

function ReadText($path) {
    return [System.IO.File]::ReadAllText($path).Replace("`r`n","`n").Replace("`r","`n")
}

try {
    Clear-Host
    Write-Host "======================================================="
    Write-Host " TURBORAMA - RESTAURAR PrerequisiteControl LIMPO V17"
    Write-Host "======================================================="
    Write-Host ""

    if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
        $ProjectRoot = Get-Location
    }

    $ProjectRoot = [System.IO.Path]::GetFullPath($ProjectRoot)
    $PatchDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $CleanDir = Join-Path $PatchDir "_TURBORAMA_CLEAN_SOURCE"

    Info "Projeto: $ProjectRoot"
    Info "Patch: $PatchDir"
    Info "Fonte limpa: $CleanDir"

    $cleanCs = Join-Path $CleanDir "PrerequisiteControl.cs"
    $cleanDesigner = Join-Path $CleanDir "PrerequisiteControl.Designer.cs"

    if (!(Test-Path -LiteralPath $cleanCs)) {
        throw "Arquivo limpo nao encontrado: $cleanCs"
    }
    if (!(Test-Path -LiteralPath $cleanDesigner)) {
        throw "Arquivo limpo nao encontrado: $cleanDesigner"
    }

    Warn "Feche o Visual Studio antes de aplicar."
    Write-Host "Pressione ENTER para substituir PrerequisiteControl por uma versao limpa."
    Read-Host | Out-Null

    Get-Process devenv -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-Process MSBuild -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-Process VBCSCompiler -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-Process InstallerHost -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

    $targetCs = Get-ChildItem -LiteralPath $ProjectRoot -Recurse -Filter "PrerequisiteControl.cs" -File -ErrorAction SilentlyContinue |
        Where-Object {
            try {
                if ($_.FullName.StartsWith($CleanDir, [System.StringComparison]::OrdinalIgnoreCase)) { return $false }
                $c = [System.IO.File]::ReadAllText($_.FullName)
                return $c.Contains("class PrerequisiteControl")
            } catch { return $false }
        } |
        Select-Object -First 1

    $targetDesigner = Get-ChildItem -LiteralPath $ProjectRoot -Recurse -Filter "PrerequisiteControl.Designer.cs" -File -ErrorAction SilentlyContinue |
        Where-Object {
            try {
                if ($_.FullName.StartsWith($CleanDir, [System.StringComparison]::OrdinalIgnoreCase)) { return $false }
                $c = [System.IO.File]::ReadAllText($_.FullName)
                return $c.Contains("partial class PrerequisiteControl")
            } catch { return $false }
        } |
        Select-Object -First 1

    if ($targetCs -eq $null) {
        throw "Nao encontrei o PrerequisiteControl.cs original do projeto."
    }
    if ($targetDesigner -eq $null) {
        throw "Nao encontrei o PrerequisiteControl.Designer.cs original do projeto."
    }

    Info "Destino CS: $($targetCs.FullName)"
    Info "Destino Designer: $($targetDesigner.FullName)"

    $srcCsFull = [System.IO.Path]::GetFullPath($cleanCs)
    $dstCsFull = [System.IO.Path]::GetFullPath($targetCs.FullName)
    $srcDesignerFull = [System.IO.Path]::GetFullPath($cleanDesigner)
    $dstDesignerFull = [System.IO.Path]::GetFullPath($targetDesigner.FullName)

    if ($srcCsFull.Equals($dstCsFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Fonte e destino do PrerequisiteControl.cs sao o mesmo arquivo. Extraia o ZIP novamente; os arquivos limpos precisam ficar na pasta _TURBORAMA_CLEAN_SOURCE."
    }
    if ($srcDesignerFull.Equals($dstDesignerFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Fonte e destino do Designer sao o mesmo arquivo. Extraia o ZIP novamente; os arquivos limpos precisam ficar na pasta _TURBORAMA_CLEAN_SOURCE."
    }

    Copy-Item -LiteralPath $targetCs.FullName -Destination ($targetCs.FullName + ".bak_ANTES_RESTAURAR_V17") -Force
    Copy-Item -LiteralPath $targetDesigner.FullName -Destination ($targetDesigner.FullName + ".bak_ANTES_RESTAURAR_V17") -Force

    Copy-Item -LiteralPath $cleanCs -Destination $targetCs.FullName -Force
    Copy-Item -LiteralPath $cleanDesigner -Destination $targetDesigner.FullName -Force

    Ok "PrerequisiteControl.cs e Designer restaurados sem self-copy."

    foreach ($src in Get-ChildItem -LiteralPath $ProjectRoot -Recurse -Filter "*.cs" -File -ErrorAction SilentlyContinue) {
        if ($src.FullName.StartsWith($CleanDir, [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        if ($src.Name -eq "PrerequisiteControl.cs" -or $src.Name -eq "PrerequisiteControl.Designer.cs") {
            continue
        }

        $t = ReadText $src.FullName
        $o = $t

        $t = [regex]::Replace($t, '\s*try\s*\{\s*(?:global::)?(?:InstallerHost\.)?(?:global::)?(?:InstallerHost\.)?(?:TurboramaPremiumUi|TurboramaPremiumTheme)\.(?:ApplyTheme|ApplyLicense|Apply)\(this\);\s*\}\s*catch\s*\{\s*\}', "`n", [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        $t = [regex]::Replace($t, '\s*(?:global::)?(?:InstallerHost\.)?(?:global::)?(?:InstallerHost\.)?(?:TurboramaPremiumUi|TurboramaPremiumTheme)\.(?:ApplyTheme|ApplyLicense|Apply)\(this\);\s*', "`n", [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        $t = $t.Replace("global::global::", "global::")
        $t = $t.Replace("InstallerHost.global::InstallerHost.", "InstallerHost.")
        $t = $t.Replace("global::InstallerHost.global::InstallerHost.", "InstallerHost.")
        $t = $t.Replace("InstallerHost.InstallerHost.", "InstallerHost.")

        if ($src.Name -eq "WizardPanel.cs" -and !$t.Contains("TurboramaPremiumUi") -and !$t.Contains("TurboramaPremiumTheme")) {
            $t = [regex]::Replace($t, '^\s*using\s+InstallerHost\s*;\s*', "", [System.Text.RegularExpressions.RegexOptions]::Multiline)
        }

        if ($t -ne $o) {
            Copy-Item -LiteralPath $src.FullName -Destination ($src.FullName + ".bak_REMOVE_TEMA_V17") -Force
            SaveText $src.FullName $t
            Ok "Removido tema quebrado de: $($src.Name)"
        }
    }

    $csproj = Get-ChildItem -LiteralPath $ProjectRoot -Recurse -Filter "*.csproj" -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($csproj -ne $null) {
        $p = [System.IO.File]::ReadAllText($csproj.FullName)
        $op = $p

        $p = $p.Replace('    <Compile Include="TurboramaPremiumUi.cs" />' + "`r`n", "")
        $p = $p.Replace('    <Compile Include="TurboramaPremiumTheme.cs" />' + "`r`n", "")
        $p = $p.Replace('<Compile Include="TurboramaPremiumUi.cs" />', "")
        $p = $p.Replace('<Compile Include="TurboramaPremiumTheme.cs" />', "")

        if ($p -ne $op) {
            Copy-Item -LiteralPath $csproj.FullName -Destination ($csproj.FullName + ".bak_REMOVE_TEMA_V17") -Force
            SaveText $csproj.FullName $p
            Ok "TurboramaPremiumUi/Theme removido do csproj."
        }

        $projectDir = $csproj.Directory.FullName
        foreach ($folder in @(".vs","bin","obj")) {
            $target = Join-Path $projectDir $folder
            if (Test-Path $target) {
                Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction SilentlyContinue
                Ok "Removido: $target"
            }
        }
    }

    Write-Host ""
    Write-Host "======================================================="
    Write-Host " V17 APLICADO"
    Write-Host " Agora abra InstallerHost.sln e compile Release."
    Write-Host "======================================================="
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
