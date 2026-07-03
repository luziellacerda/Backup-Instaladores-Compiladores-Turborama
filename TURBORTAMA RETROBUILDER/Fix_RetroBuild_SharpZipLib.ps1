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

try {
    Clear-Host
    Write-Host ""
    Write-Host "======================================================="
    Write-Host " TURBORAMA - FIX RetroBuild SharpZipLib"
    Write-Host "======================================================="
    Write-Host ""

    if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
        $ProjectRoot = Get-Location
    }

    $ProjectRoot = [System.IO.Path]::GetFullPath($ProjectRoot)

    $csproj = Get-ChildItem -LiteralPath $ProjectRoot -Recurse -Filter "*.csproj" -File |
        Where-Object {
            try {
                $c = [System.IO.File]::ReadAllText($_.FullName)
                return ($c.Contains("<RootNamespace>RetroBuild</RootNamespace>") -or $c.Contains("<AssemblyName>RetroBuild</AssemblyName>"))
            } catch { return $false }
        } |
        Select-Object -First 1

    if ($csproj -eq $null) {
        throw "Nao encontrei o .csproj do RetroBuild."
    }

    $projectDir = $csproj.Directory.FullName
    $dll = Join-Path $projectDir "resources\ICSharpCode\SharpZipLib.dll"

    Info "Projeto: $($csproj.FullName)"
    Info "DLL esperada: $dll"

    if (!(Test-Path -LiteralPath $dll)) {
        throw "Nao encontrei resources\ICSharpCode\SharpZipLib.dll. Copie essa DLL para essa pasta antes de compilar."
    }

    Copy-Item -LiteralPath $csproj.FullName -Destination ($csproj.FullName + ".bak_sharpziplib") -Force
    Ok "Backup criado."

    $xmlText = [System.IO.File]::ReadAllText($csproj.FullName)

    # Remove referencias antigas simples/quebradas do SharpZipLib.
    $xmlText = [regex]::Replace(
        $xmlText,
        '\s*<Reference\s+Include="ICSharpCode\.SharpZipLib"\s*/>',
        '',
        'IgnoreCase'
    )

    $xmlText = [regex]::Replace(
        $xmlText,
        '\s*<Reference\s+Include="ICSharpCode\.SharpZipLib"[\s\S]*?</Reference>',
        '',
        'IgnoreCase'
    )

    # Remove EmbeddedResource antigo da dll para recriar com LogicalName correto.
    $xmlText = [regex]::Replace(
        $xmlText,
        '\s*<EmbeddedResource\s+Include="resources\\ICSharpCode\\SharpZipLib\.dll"\s*/>',
        '',
        'IgnoreCase'
    )

    $xmlText = [regex]::Replace(
        $xmlText,
        '\s*<EmbeddedResource\s+Include="resources\\ICSharpCode\\SharpZipLib\.dll"[\s\S]*?</EmbeddedResource>',
        '',
        'IgnoreCase'
    )

    $referenceBlock = @'
    <Reference Include="ICSharpCode.SharpZipLib">
      <HintPath>resources\ICSharpCode\SharpZipLib.dll</HintPath>
      <SpecificVersion>False</SpecificVersion>
      <Private>False</Private>
    </Reference>
'@

    $embeddedBlock = @'
    <EmbeddedResource Include="resources\ICSharpCode\SharpZipLib.dll">
      <LogicalName>RetroBuild.resources.ICSharpCode.SharpZipLib.dll</LogicalName>
    </EmbeddedResource>
'@

    # Insere referencia dentro do primeiro ItemGroup de referencias.
    if ($xmlText -match '(?s)(<ItemGroup>\s*(?:<Reference\b[\s\S]*?</Reference>\s*|<Reference\b[^>]*/>\s*)+)') {
        $xmlText = [regex]::Replace(
            $xmlText,
            '(?s)(<ItemGroup>\s*(?:<Reference\b[\s\S]*?</Reference>\s*|<Reference\b[^>]*/>\s*)+)',
            '${1}' + "`r`n" + $referenceBlock,
            1
        )
    }
    else {
        $xmlText = $xmlText.Replace("</Project>", "  <ItemGroup>`r`n$referenceBlock  </ItemGroup>`r`n</Project>")
    }

    # Insere EmbeddedResource no ItemGroup de resources, ou cria um novo.
    if ($xmlText -match '(?s)(<ItemGroup>\s*(?:<EmbeddedResource\b[\s\S]*?</EmbeddedResource>\s*|<EmbeddedResource\b[^>]*/>\s*)+)') {
        $xmlText = [regex]::Replace(
            $xmlText,
            '(?s)(<ItemGroup>\s*(?:<EmbeddedResource\b[\s\S]*?</EmbeddedResource>\s*|<EmbeddedResource\b[^>]*/>\s*)+)',
            '${1}' + "`r`n" + $embeddedBlock,
            1
        )
    }
    else {
        $xmlText = $xmlText.Replace("</Project>", "  <ItemGroup>`r`n$embeddedBlock  </ItemGroup>`r`n</Project>")
    }

    SaveText $csproj.FullName $xmlText
    Ok "RetroBuild.csproj corrigido com HintPath e LogicalName."

    # Garante que Program.cs procure o LogicalName correto.
    $program = Join-Path $projectDir "Program.cs"
    if (Test-Path -LiteralPath $program) {
        Copy-Item -LiteralPath $program -Destination ($program + ".bak_sharpziplib") -Force
        $p = [System.IO.File]::ReadAllText($program)
        $p = $p.Replace('"RetroBuild.resources.ICSharpCode.SharpZipLib.dll"', '"RetroBuild.resources.ICSharpCode.SharpZipLib.dll"')
        SaveText $program $p
        Ok "Program.cs conferido."
    }

    foreach ($folder in @(".vs", "bin", "obj")) {
        $target = Join-Path $projectDir $folder
        if (Test-Path $target) {
            Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction SilentlyContinue
            Ok "Removido: $target"
        }
    }

    Write-Host ""
    Write-Host "======================================================="
    Write-Host " CORRIGIDO"
    Write-Host " Abra a solution do RetroBuild e compile:"
    Write-Host " Release | Any CPU"
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
