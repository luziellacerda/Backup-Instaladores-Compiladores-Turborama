#Requires -Version 5.1
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$TargetDir = Join-Path $Root "InstallerHost\resources\prerequisites"
New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null

$RepoBase = "http://www.retrobat.ovh/repo/win64/prerequisites"

function Download-FileReliable {
    param(
        [string]$Name,
        [string[]]$Urls,
        [string]$TargetDir,
        [long]$MinBytes = 10000
    )

    $out = Join-Path $TargetDir $Name
    if (Test-Path -LiteralPath $out) {
        $len = (Get-Item -LiteralPath $out).Length
        if ($len -gt $MinBytes) {
            Write-Host "   ja existe ($([math]::Round($len/1MB,2)) MB), pulando" -ForegroundColor DarkGray
            return $out
        }
    }

    $errors = @()
    foreach ($url in $Urls) {
        try {
            Write-Host "   tentando: $url" -ForegroundColor DarkGray
            $client = New-Object System.Net.WebClient
            $client.Headers.Add("User-Agent", "TurboramaOfflineBundle/2.0")
            $client.DownloadFile($url, $out)
            if ((Test-Path -LiteralPath $out) -and (Get-Item -LiteralPath $out).Length -gt $MinBytes) {
                Write-Host "   OK ($([math]::Round((Get-Item $out).Length/1MB,2)) MB)" -ForegroundColor Green
                return $out
            }
        }
        catch {
            $errors += "$url -> $($_.Exception.Message)"
        }
    }

    throw "Falha ao baixar $Name`n$($errors -join "`n")"
}

function Extract-ArchiveFile {
    param(
        [string]$ArchivePath,
        [string]$Destination,
        [string]$FilePattern
    )

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    $sevenZip = @(
        "C:\Program Files\7-Zip\7z.exe",
        "C:\Program Files (x86)\7-Zip\7z.exe",
        "D:\TURBOPCINSTALL\system\tools\7za.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    if ($sevenZip) {
        & $sevenZip x $ArchivePath ("-o" + $Destination) -y | Out-Null
    }
    else {
        Expand-Archive -Path $ArchivePath -DestinationPath $Destination -Force
    }

    return Get-ChildItem -LiteralPath $Destination -Recurse -Filter $FilePattern | Select-Object -First 1
}

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " TURBORAMA - PACOTE OFFLINE COMPLETO DE RUNTIMES" -ForegroundColor Cyan
Write-Host " Destino: $TargetDir" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

$downloads = @(
    @{ Name = "vc_redist.x64.exe"; MinBytes = 1000000; Urls = @("https://aka.ms/vs/17/release/vc_redist.x64.exe") },
    @{ Name = "vc_redist.x86.exe"; MinBytes = 1000000; Urls = @("https://aka.ms/vs/17/release/vc_redist.x86.exe") },
    @{ Name = "NDP48-x86-x64-AllOS-ENU.exe"; MinBytes = 50000000; Urls = @(
        "https://go.microsoft.com/fwlink/?linkid=2088631",
        "https://download.microsoft.com/download/f/3/a/f3a6af84-da23-40a5-8d1c-49cc10c8e76f/NDP48-x86-x64-AllOS-ENU.exe"
    ) },
    @{ Name = "dotNetFx35_WX_10_x86_x64.exe"; MinBytes = 10000000; Urls = @(
        "https://github.com/abbodi1406/dotNetFx35W10/releases/download/v0.25.11/dotNetFx35_WX_10_x86_x64_z.exe"
    ) },
    @{ Name = "windowsdesktop-runtime-8.0-win-x64.exe"; MinBytes = 10000000; Urls = @("https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe") },
    @{ Name = "windowsdesktop-runtime-8.0-win-x86.exe"; MinBytes = 10000000; Urls = @("https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x86.exe") },
    @{ Name = "MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; MinBytes = 50000000; Urls = @(
        "https://msedge.sf.dl.delivery.mp.microsoft.com/filestreamingservice/files/ee9caa0e-313c-4ec3-9165-ec3226dee379/MicrosoftEdgeWebView2RuntimeInstallerX64.exe"
    ) },
    @{ Name = "xnafx40_redist.msi"; MinBytes = 1000000; Urls = @("https://download.microsoft.com/download/5/3/A/53A804C8-EC78-43CD-A0F0-2FB4D45603D3/xnafx40_redist.msi") },
    @{ Name = "vcredist2005_x64.zip"; MinBytes = 100000; Urls = @("$RepoBase/vcredist2005_x64.zip") },
    @{ Name = "vcredist2005_x86.zip"; MinBytes = 100000; Urls = @("$RepoBase/vcredist2005_x86.zip") },
    @{ Name = "vcredist2008_x64.zip"; MinBytes = 100000; Urls = @("$RepoBase/vcredist2008_x64.zip") },
    @{ Name = "vcredist2008_x86.zip"; MinBytes = 100000; Urls = @("$RepoBase/vcredist2008_x86.zip") },
    @{ Name = "vcredist2010_x64.zip"; MinBytes = 100000; Urls = @("$RepoBase/vcredist2010_x64.zip") },
    @{ Name = "vcredist2010_x86.zip"; MinBytes = 100000; Urls = @("$RepoBase/vcredist2010_x86.zip") },
    @{ Name = "vcredist2012_x64.zip"; MinBytes = 100000; Urls = @("$RepoBase/vcredist2012_x64.zip") },
    @{ Name = "vcredist2012_x86.zip"; MinBytes = 100000; Urls = @("$RepoBase/vcredist2012_x86.zip") },
    @{ Name = "vcredist2013_x64.zip"; MinBytes = 100000; Urls = @("$RepoBase/vcredist2013_x64.zip") },
    @{ Name = "vcredist2013_x86.zip"; MinBytes = 100000; Urls = @("$RepoBase/vcredist2013_x86.zip") },
    @{ Name = "DokanSetup.zip"; MinBytes = 100000; Urls = @("$RepoBase/DokanSetup.zip") },
    @{ Name = "winfsp.zip"; MinBytes = 100000; Urls = @("$RepoBase/winfsp.zip") }
)

foreach ($item in $downloads) {
    Write-Host "-> $($item.Name)" -ForegroundColor Yellow
    Download-FileReliable -Name $item.Name -Urls $item.Urls -TargetDir $TargetDir -MinBytes $item.MinBytes
}

# Renomear instalador .NET 3.5 se veio com sufixo _z
$net35z = Join-Path $TargetDir "dotNetFx35_WX_10_x86_x64_z.exe"
$net35 = Join-Path $TargetDir "dotNetFx35_WX_10_x86_x64.exe"
if ((Test-Path $net35z) -and (-not (Test-Path $net35))) {
    Move-Item -LiteralPath $net35z -Destination $net35 -Force
}

# Renomear .NET 4.8 legado
$ndpOld = Join-Path $TargetDir "NDP48-Web.exe"
$ndpNew = Join-Path $TargetDir "NDP48-x86-x64-AllOS-ENU.exe"
if ((Test-Path $ndpOld) -and (-not (Test-Path $ndpNew))) {
    Move-Item -LiteralPath $ndpOld -Destination $ndpNew -Force
}

# DirectX offline
$dxZip = Join-Path $TargetDir "directx_Jun2010_redist.zip"
$dxExe = Join-Path $TargetDir "directx_Jun2010_redist.exe"
if (-not (Test-Path $dxExe)) {
    if (-not (Test-Path $dxZip)) {
        Write-Host "-> directx_Jun2010_redist.zip" -ForegroundColor Yellow
        Download-FileReliable -Name "directx_Jun2010_redist.zip" -Urls @("$RepoBase/directx_Jun2010_redist.zip") -TargetDir $TargetDir -MinBytes 1000000
    }
    Write-Host "-> Extraindo directx_Jun2010_redist.exe" -ForegroundColor Yellow
    $tempDx = Join-Path $env:TEMP ("dxextract_" + [Guid]::NewGuid().ToString("N"))
    $nested = Extract-ArchiveFile -ArchivePath $dxZip -Destination $tempDx -FilePattern "directx_Jun2010_redist.exe"
    if ($null -eq $nested) { throw "directx_Jun2010_redist.exe nao encontrado no ZIP." }
    Copy-Item -LiteralPath $nested.FullName -Destination $dxExe -Force
    Remove-Item -LiteralPath $tempDx -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $dxZip -Force -ErrorAction SilentlyContinue
    Write-Host "   OK DirectX offline pronto" -ForegroundColor Green
}

# OpenAL offline (DLLs router x86/x64)
$oalBundle = Join-Path $TargetDir "openal-offline.zip"
if (-not (Test-Path $oalBundle)) {
    Write-Host "-> openal-offline.zip" -ForegroundColor Yellow
    $oalZip = Join-Path $env:TEMP "openal-soft-bin.zip"
    if (-not (Test-Path $oalZip)) {
        Download-FileReliable -Name "openal-soft-bin.zip" -Urls @(
            "https://github.com/kcat/openal-soft/releases/download/1.23.1/openal-soft-1.23.1-bin.zip"
        ) -TargetDir $env:TEMP -MinBytes 100000
    }
    $oalTemp = Join-Path $env:TEMP "openal_extract"
    Remove-Item $oalTemp -Recurse -Force -ErrorAction SilentlyContinue
    $root = Extract-ArchiveFile -ArchivePath $oalZip -Destination $oalTemp -FilePattern "OpenAL32.dll"
    $win32 = Get-ChildItem $oalTemp -Recurse -Filter "OpenAL32.dll" | Where-Object { $_.FullName -match 'router\\Win32|\\Win32\\' } | Select-Object -First 1
    $win64 = Get-ChildItem $oalTemp -Recurse -Filter "OpenAL32.dll" | Where-Object { $_.FullName -match 'router\\Win64|\\Win64\\' } | Select-Object -First 1
    if ($null -eq $win32 -or $null -eq $win64) { throw "OpenAL32.dll router Win32/Win64 nao encontrado." }
    $packRoot = Join-Path $env:TEMP "openal_pack"
    Remove-Item $packRoot -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path (Join-Path $packRoot "Win32") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $packRoot "Win64") | Out-Null
    Copy-Item $win32.FullName (Join-Path $packRoot "Win32\OpenAL32.dll") -Force
    Copy-Item $win64.FullName (Join-Path $packRoot "Win64\OpenAL32.dll") -Force
    if (Test-Path $oalBundle) { Remove-Item $oalBundle -Force }
    Compress-Archive -Path (Join-Path $packRoot '*') -DestinationPath $oalBundle -Force
    Remove-Item $oalTemp,$packRoot -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "   OK OpenAL offline pronto" -ForegroundColor Green
}

# Remover arquivos legados que exigem internet
$legacyOnlineOnly = @(
    "MicrosoftEdgeWebview2Setup.exe",
    "NDP48-Web.exe",
    "dotNetFx35_WX_10_x86_x64_z.exe"
)
foreach ($legacy in $legacyOnlineOnly) {
    $legacyPath = Join-Path $TargetDir $legacy
    if (Test-Path $legacyPath) {
        Remove-Item -LiteralPath $legacyPath -Force -ErrorAction SilentlyContinue
        Write-Host "Removido legado online-only: $legacy" -ForegroundColor DarkYellow
    }
}

Write-Host ""
Write-Host "AUDITORIA FINAL DO PACOTE OFFLINE" -ForegroundColor Cyan
$required = @(
    "vc_redist.x64.exe","vc_redist.x86.exe","NDP48-x86-x64-AllOS-ENU.exe","dotNetFx35_WX_10_x86_x64.exe",
    "directx_Jun2010_redist.exe","vcredist2005_x64.zip","vcredist2005_x86.zip","vcredist2008_x64.zip","vcredist2008_x86.zip",
    "vcredist2010_x64.zip","vcredist2010_x86.zip","vcredist2012_x64.zip","vcredist2012_x86.zip","vcredist2013_x64.zip","vcredist2013_x86.zip",
    "DokanSetup.zip","winfsp.zip","MicrosoftEdgeWebView2RuntimeInstallerX64.exe","windowsdesktop-runtime-8.0-win-x64.exe",
    "windowsdesktop-runtime-8.0-win-x86.exe","xnafx40_redist.msi","openal-offline.zip"
)
$missing = @()
$totalMb = 0
foreach ($file in $required) {
    $path = Join-Path $TargetDir $file
    if (Test-Path $path) {
        $size = (Get-Item $path).Length
        $totalMb += $size
        Write-Host ("  OK  {0,-48} {1,8:N2} MB" -f $file, ($size/1MB)) -ForegroundColor Green
    }
    else {
        $missing += $file
        Write-Host ("  FALTA {0}" -f $file) -ForegroundColor Red
    }
}
Write-Host ""
Write-Host ("Total do pacote offline: {0:N2} MB" -f ($totalMb/1MB)) -ForegroundColor Cyan
if ($missing.Count -gt 0) {
    throw "Pacote incompleto. Faltam: $($missing -join ', ')"
}

Write-Host ""
Write-Host "Pacote offline COMPLETO. Recompile o InstallerHost em Release." -ForegroundColor Green
Write-Host ""