#Requires -Version 5.1
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$TargetDir = Join-Path $Root "InstallerHost\resources\prerequisites"
New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null

function Download-FileReliable {
    param(
        [string]$Name,
        [string[]]$Urls,
        [string]$TargetDir
    )

    $out = Join-Path $TargetDir $Name
    if (Test-Path -LiteralPath $out) {
        $len = (Get-Item -LiteralPath $out).Length
        if ($len -gt 10000) {
            Write-Host "   ja existe ($len bytes), pulando" -ForegroundColor DarkGray
            return
        }
    }

    $errors = @()
    foreach ($url in $Urls) {
        try {
            $client = New-Object System.Net.WebClient
            $client.Headers.Add("User-Agent", "TurboramaPrerequisiteDownloader/1.0")
            $client.DownloadFile($url, $out)
            if ((Test-Path -LiteralPath $out) -and (Get-Item -LiteralPath $out).Length -gt 1000) {
                Write-Host "   OK ($url)" -ForegroundColor Green
                return
            }
        }
        catch {
            $errors += "$url -> $($_.Exception.Message)"
        }
    }

    throw "Falha ao baixar $Name`n$($errors -join "`n")"
}

$downloads = @(
    @{ Name = "vc_redist.x64.exe"; Urls = @("https://aka.ms/vs/17/release/vc_redist.x64.exe") },
    @{ Name = "vc_redist.x86.exe"; Urls = @("https://aka.ms/vs/17/release/vc_redist.x86.exe") },
    @{ Name = "NDP48-Web.exe"; Urls = @("https://go.microsoft.com/fwlink/?linkid=2088631", "https://download.microsoft.com/download/9/5/A/95A9616B-7A37-4AF6-8AA7-3DFD2F1469CB/NDP48-Web.exe") },
    @{ Name = "windowsdesktop-runtime-8.0-win-x64.exe"; Urls = @("https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe") },
    @{ Name = "windowsdesktop-runtime-8.0-win-x86.exe"; Urls = @("https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x86.exe") },
    @{ Name = "MicrosoftEdgeWebview2Setup.exe"; Urls = @("https://go.microsoft.com/fwlink/p/?LinkId=2124703") },
    @{ Name = "xnafx40_redist.msi"; Urls = @("https://download.microsoft.com/download/5/3/A/53A804C8-EC78-43CD-A0F0-2FB4D45603D3/xnafx40_redist.msi") },
    @{ Name = "vcredist2005_x64.zip"; Urls = @("http://www.retrobat.ovh/repo/win64/prerequisites/vcredist2005_x64.zip") },
    @{ Name = "vcredist2005_x86.zip"; Urls = @("http://www.retrobat.ovh/repo/win64/prerequisites/vcredist2005_x86.zip") },
    @{ Name = "vcredist2008_x64.zip"; Urls = @("http://www.retrobat.ovh/repo/win64/prerequisites/vcredist2008_x64.zip") },
    @{ Name = "vcredist2008_x86.zip"; Urls = @("http://www.retrobat.ovh/repo/win64/prerequisites/vcredist2008_x86.zip") },
    @{ Name = "vcredist2010_x64.zip"; Urls = @("http://www.retrobat.ovh/repo/win64/prerequisites/vcredist2010_x64.zip") },
    @{ Name = "vcredist2010_x86.zip"; Urls = @("http://www.retrobat.ovh/repo/win64/prerequisites/vcredist2010_x86.zip") },
    @{ Name = "vcredist2012_x64.zip"; Urls = @("http://www.retrobat.ovh/repo/win64/prerequisites/vcredist2012_x64.zip") },
    @{ Name = "vcredist2012_x86.zip"; Urls = @("http://www.retrobat.ovh/repo/win64/prerequisites/vcredist2012_x86.zip") },
    @{ Name = "vcredist2013_x64.zip"; Urls = @("http://www.retrobat.ovh/repo/win64/prerequisites/vcredist2013_x64.zip") },
    @{ Name = "vcredist2013_x86.zip"; Urls = @("http://www.retrobat.ovh/repo/win64/prerequisites/vcredist2013_x86.zip") },
    @{ Name = "directx_Jun2010_redist.zip"; Urls = @("http://www.retrobat.ovh/repo/win64/prerequisites/directx_Jun2010_redist.zip") },
    @{ Name = "DokanSetup.zip"; Urls = @("http://www.retrobat.ovh/repo/win64/prerequisites/DokanSetup.zip") },
    @{ Name = "winfsp.zip"; Urls = @("http://www.retrobat.ovh/repo/win64/prerequisites/winfsp.zip") }
)

$optionalDownloads = @(
    @{ Name = "oalinst.exe"; Urls = @(
        "http://www.retrobat.ovh/repo/win64/prerequisites/oalinst.exe",
        "https://github.com/kcat/openal-soft/releases/download/1.23.1/oalinst.exe"
    ) }
)

Write-Host ""
Write-Host "Baixando PACOTE COMPLETO de runtimes para jogos/emuladores..." -ForegroundColor Cyan
Write-Host "Destino: $TargetDir"
Write-Host ""

foreach ($item in $downloads) {
    Write-Host "-> $($item.Name)" -ForegroundColor Yellow
    Download-FileReliable -Name $item.Name -Urls $item.Urls -TargetDir $TargetDir
}

foreach ($item in $optionalDownloads) {
    Write-Host "-> $($item.Name) (opcional)" -ForegroundColor Yellow
    try {
        Download-FileReliable -Name $item.Name -Urls $item.Urls -TargetDir $TargetDir
    }
    catch {
        Write-Host "   AVISO: $($item.Name) nao baixado. Continuando..." -ForegroundColor DarkYellow
    }
}

$dxZip = Join-Path $TargetDir "directx_Jun2010_redist.zip"
$dxExe = Join-Path $TargetDir "directx_Jun2010_redist.exe"
if ((Test-Path -LiteralPath $dxZip) -and (-not (Test-Path -LiteralPath $dxExe))) {
    Write-Host "-> Extraindo directx_Jun2010_redist.exe do ZIP" -ForegroundColor Yellow
    $sevenZip = "C:\Program Files\7-Zip\7z.exe"
    if (-not (Test-Path $sevenZip)) {
        throw "7-Zip nao encontrado para extrair DirectX."
    }
    $tempDx = Join-Path $env:TEMP ("dxextract_" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $tempDx | Out-Null
    & $sevenZip x $dxZip ("-o" + $tempDx) -y | Out-Null
    $nested = Get-ChildItem -LiteralPath $tempDx -Recurse -Filter "directx_Jun2010_redist.exe" | Select-Object -First 1
    if ($null -eq $nested) {
        throw "directx_Jun2010_redist.exe nao encontrado dentro do ZIP."
    }
    Copy-Item -LiteralPath $nested.FullName -Destination $dxExe -Force
    Remove-Item -LiteralPath $tempDx -Recurse -Force
    Remove-Item -LiteralPath $dxZip -Force -ErrorAction SilentlyContinue
    Write-Host "   OK (ZIP removido, apenas .exe mantido)" -ForegroundColor Green
}

Write-Host ""
Write-Host "Pacote completo de runtimes baixado." -ForegroundColor Green
Write-Host "Recompile o InstallerHost em Release para embutir tudo no instalador comercial." -ForegroundColor Green
Write-Host ""