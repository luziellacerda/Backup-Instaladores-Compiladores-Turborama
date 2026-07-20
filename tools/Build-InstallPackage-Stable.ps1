#Requires -Version 5.1
param(
  [string]$SourceRoot = "D:\Turborama",
  [string]$RetroBuildDir = "D:\Backup-Instaladores-Compiladores-Turborama\TURBORTAMA RETROBUILDER\RetroBuild",
  [string]$InstallerHostProject = "D:\Backup-Instaladores-Compiladores-Turborama\TURBORAMA INSTALER HOST\InstallerHost\InstallerHost.csproj",
  [string]$OutputDir = "D:\Backup-Instaladores-Compiladores-Turborama\DIST-INSTALL-STABLE",
  [string]$StageDir = "D:\tmp\turborama-install-stage",
  [string]$BaseZipName = "turborama-v6.0-stable-win64.zip",
  [string]$VersionTag = "",
  [switch]$SkipZipExtract
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($VersionTag)) {
  $VersionTag = "TurboRama-stable-" + (Get-Date -Format "yyyyMMdd")
}
$SplitPartBytes = 1900L * 1024L * 1024L

$z7 = @(
  "D:\Turborama\emulationstation\7z.exe",
  "D:\Backup-Instaladores-Compiladores-Turborama\TURBORTAMA RETROBUILDER\RetroBuild\system\tools\7za.exe",
  "C:\Program Files\7-Zip\7z.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

$msbuild = @(
  "${env:ProgramFiles}\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe",
  "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
  "${env:ProgramFiles}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $z7) { throw "7-Zip nao encontrado." }
if (-not $msbuild) { throw "MSBuild nao encontrado." }

function Step([string]$m) { Write-Host ""; Write-Host ("=== " + $m + " ===") -ForegroundColor Cyan }
function Ok([string]$m) { Write-Host ("OK  " + $m) -ForegroundColor Green }
function Warn([string]$m) { Write-Host ("!!  " + $m) -ForegroundColor Yellow }

Step "1 Compilar InstallerHost Release"
& $msbuild $InstallerHostProject /p:Configuration=Release /v:m /nologo
if ($LASTEXITCODE -ne 0) { throw "Falha MSBuild InstallerHost" }
$ihSrc = "D:\Backup-Instaladores-Compiladores-Turborama\TURBORAMA INSTALER HOST\InstallerHost\bin\Release\InstallerHost.exe"
if (-not (Test-Path $ihSrc)) { throw "InstallerHost.exe Release nao gerado" }
Copy-Item $ihSrc (Join-Path $RetroBuildDir "InstallerHost.exe") -Force
$rbRel = Join-Path $RetroBuildDir "bin\Release"
if (Test-Path $rbRel) { Copy-Item $ihSrc (Join-Path $rbRel "InstallerHost.exe") -Force -EA SilentlyContinue }
Ok "InstallerHost.exe actualizado"

Step "2 Preparar stage a partir do zip base"
$baseZip = Join-Path $RetroBuildDir $BaseZipName
if (-not (Test-Path $baseZip)) { throw "Zip base nao encontrado: $baseZip" }

if (-not $SkipZipExtract) {
  if (Test-Path $StageDir) {
    Warn "A limpar stage antigo..."
    Remove-Item $StageDir -Recurse -Force
  }
  New-Item -ItemType Directory -Force -Path $StageDir | Out-Null
  $gb = [math]::Round((Get-Item $baseZip).Length / 1GB, 2)
  Write-Host ("A extrair base " + $gb + " GB - demora...")
  & $z7 x $baseZip ("-o" + $StageDir) -y | Out-Null
  if ($LASTEXITCODE -gt 1) { throw ("7z extract falhou code " + $LASTEXITCODE) }
  Ok ("Base extraida: " + $StageDir)
} else {
  if (-not (Test-Path $StageDir)) { throw "SkipZipExtract mas stage nao existe" }
  Warn "A reutilizar stage existente"
}

Step "3 Actualizar stage com sistema actual"
$esSrc = Join-Path $SourceRoot "emulationstation"
$esDst = Join-Path $StageDir "emulationstation"
if (-not (Test-Path $esSrc)) { throw ("Fonte ES em falta: " + $esSrc) }

Get-ChildItem $esDst -Filter "emulationstation.exe*" -EA SilentlyContinue |
  Where-Object { $_.Name -ne "emulationstation.exe" } |
  Remove-Item -Force -EA SilentlyContinue

& robocopy $esSrc $esDst /E /IS /IT /XF *.log *.bak* *DEV-ONLY* emulationstation.exe.bak* emulationstation.exe.pre-* emulationstation.exe.stable-* /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
if ($LASTEXITCODE -ge 8) { throw ("robocopy ES falhou code " + $LASTEXITCODE) }
$global:LASTEXITCODE = 0
Ok "emulationstation actualizado"

$decSrc = Join-Path $SourceRoot "system\decorations"
$decDst = Join-Path $StageDir "system\decorations"
if (Test-Path $decSrc) {
  New-Item -ItemType Directory -Force -Path $decDst | Out-Null
  & robocopy $decSrc $decDst /E /IS /IT /XD _backup* turborama_consumer /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
  if ($LASTEXITCODE -ge 8) { throw ("robocopy decorations falhou code " + $LASTEXITCODE) }
  $global:LASTEXITCODE = 0
  Ok "system decorations actualizado"
} else {
  Warn "decorations fonte em falta"
}

foreach ($f in @("TurboRama.exe", "turborama.ini", "license.txt")) {
  $s = Join-Path $SourceRoot $f
  if (Test-Path $s) {
    Copy-Item $s $StageDir -Force
    Ok ("copiado " + $f)
  }
}

Get-ChildItem $StageDir -Recurse -File -EA SilentlyContinue | Where-Object {
  $n = $_.Name
  ($n -match '\.log$') -or ($n -match 'DEV-ONLY') -or ($n -match '\.bak') -or ($n -match 'pre-rollback') -or ($n -match 'stable-new') -or ($n -match 'NAO-USAR')
} | ForEach-Object {
  Remove-Item $_.FullName -Force -EA SilentlyContinue
}

$esSet = Join-Path $esDst ".emulationstation\es_settings.cfg"
if (Test-Path $esSet) {
  $c = Get-Content $esSet -Raw
  $changed = $false
  if ($c -notmatch "SlideshowScreenSaverCustomVideoSource") {
    $inject = @(
      '  <bool name="SlideshowScreenSaverCustomVideoSource" value="true" />',
      '  <bool name="SlideshowScreenSaverVideoRecurse" value="true" />',
      '  <string name="ScreenSaverDecorations" value="systems" />'
    ) -join "`r`n"
    $c = $c.Replace("</config>", $inject + "`r`n</config>")
    $changed = $true
  }
  if ($c -notmatch "ScreenSaverBehavior") {
    $c = $c.Replace("</config>", '  <string name="ScreenSaverBehavior" value="random video" />' + "`r`n</config>")
    $changed = $true
  }
  if ($changed) {
    Set-Content -Path $esSet -Value $c -Encoding UTF8
    Ok "es_settings kiosk/screensaver"
  }
}

Step "4 Validar stage"
$must = @(
  "TurboRama.exe",
  "emulationstation\emulationstation.exe",
  "emulationstation\emulatorLauncher.exe",
  "emulationstation\SDL3.dll",
  "emulationstation\emulatorLauncher.cfg",
  "emulationstation\.emulationstation\es_systems.cfg"
)
foreach ($m in $must) {
  $p = Join-Path $StageDir $m
  if (-not (Test-Path $p)) { throw ("VALIDACAO FALHOU: falta " + $m) }
  Ok $m
}
$esLen = (Get-Item (Join-Path $StageDir "emulationstation\emulationstation.exe")).Length
if ($esLen -lt 50MB) {
  throw ("ES demasiado pequeno no stage: " + $esLen)
}
Ok ("ES MB=" + [math]::Round($esLen / 1MB, 1))

$ss = Join-Path $StageDir "emulationstation\screensaver_videos"
if (Test-Path $ss) {
  $vc = @(Get-ChildItem $ss -Recurse -File -EA SilentlyContinue | Where-Object { $_.Extension -match 'mp4|mkv|webm|avi|mov' }).Count
  Ok ("screensaver videos: " + $vc)
} else {
  Warn "sem screensaver_videos no stage"
}

foreach ($bn in @("ps5.png", "ps4.png", "switch.png", "xboxone.png", "windows.png")) {
  $bp = Join-Path $StageDir ("system\decorations\default_unglazed\systems\" + $bn)
  if (Test-Path $bp) { Ok ("bezel " + $bn) } else { Warn ("bezel em falta " + $bn) }
}

Step "5 Gerar ZIP Zip64"
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$outZip = Join-Path $OutputDir ($VersionTag + "-win64.zip")
if (Test-Path $outZip) { Remove-Item $outZip -Force }
Write-Host "A compactar stage - demora..."
Push-Location $StageDir
try {
  & $z7 a -tzip -mx=3 -mmt=on $outZip "*" | Out-Null
  if ($LASTEXITCODE -gt 1) { throw ("7z zip falhou code " + $LASTEXITCODE) }
} finally {
  Pop-Location
}
Ok ("ZIP MB=" + [math]::Round((Get-Item $outZip).Length / 1MB, 1))

Step "6 Criar setup e split pkg"
$setupPath = Join-Path $OutputDir ($VersionTag + "-win64-setup.exe")
Copy-Item $ihSrc $setupPath -Force
Ok ("setup " + (Split-Path $setupPath -Leaf))

Get-ChildItem $OutputDir -Filter ($VersionTag + "-win64-setup.exe.pkg.*") -EA SilentlyContinue | Remove-Item -Force

$parts = New-Object System.Collections.Generic.List[string]
$buf = New-Object byte[] (4MB)
$fs = [IO.File]::OpenRead($outZip)
try {
  $part = 1
  $written = 0L
  $out = $null
  while (($read = $fs.Read($buf, 0, $buf.Length)) -gt 0) {
    if (($null -eq $out) -or ($written -ge $SplitPartBytes)) {
      if ($null -ne $out) { $out.Dispose() }
      $name = ("{0}.{1:D3}" -f ($setupPath + ".pkg"), $part)
      $out = [IO.File]::Create($name)
      $parts.Add($name)
      $part++
      $written = 0L
      Write-Host ("  parte: " + (Split-Path $name -Leaf))
    }
    $out.Write($buf, 0, $read)
    $written += $read
  }
  if ($null -ne $out) { $out.Dispose() }
} finally {
  $fs.Dispose()
}
Ok ("parts=" + $parts.Count)

$shaPath = $setupPath + ".sha256.txt"
$shaLines = New-Object System.Collections.Generic.List[string]
function Add-ShaLine([string]$path, $list) {
  $h = Get-FileHash -Algorithm SHA256 -Path $path
  $list.Add(($h.Hash + "  " + (Split-Path $path -Leaf)))
}
Add-ShaLine $setupPath $shaLines
Add-ShaLine $outZip $shaLines
foreach ($p in $parts) { Add-ShaLine $p $shaLines }
$shaLines | Set-Content -Path $shaPath -Encoding ASCII
Ok ("sha256 " + (Split-Path $shaPath -Leaf))

# publicar tambem no RetroBuild
$classicZip = Join-Path $RetroBuildDir $BaseZipName
if (Test-Path $classicZip) {
  $bak = $classicZip + ".bak-" + (Get-Date -Format "yyyyMMdd-HHmmss")
  Move-Item $classicZip $bak -Force
  Warn ("backup zip antigo: " + $bak)
}
Copy-Item $outZip $classicZip -Force
Copy-Item $setupPath (Join-Path $RetroBuildDir (Split-Path $setupPath -Leaf)) -Force
foreach ($p in $parts) { Copy-Item $p (Join-Path $RetroBuildDir (Split-Path $p -Leaf)) -Force }
Copy-Item $shaPath (Join-Path $RetroBuildDir (Split-Path $shaPath -Leaf)) -Force

Step "7 LEIA-ME"
$setupLeaf = Split-Path $setupPath -Leaf
$zipLeaf = Split-Path $outZip -Leaf
$shaLeaf = Split-Path $shaPath -Leaf
$readme = @"
TurboRama - Pacote de instalacao ESTAVEL
========================================
Gerado: $(Get-Date -Format "yyyy-MM-dd HH:mm")
Versao: $VersionTag

FICHEIROS - manter TODOS na mesma pasta:
  $setupLeaf
  $setupLeaf.pkg.001  (+ .002 se existir)
  $shaLeaf
  opcional: $zipLeaf

INSTALACAO PC RECEM FORMATADO
-----------------------------
1. Windows 10/11 64-bit
2. Executar $setupLeaf como ADMINISTRADOR
3. Destino recomendado: D:\Turborama
4. Instalar todos os pre-requisitos
5. Abrir TurboRama.exe

INCLUI
------
- EmulationStation TurboRama kiosk actual
- screensaver_videos por sistema
- system\decorations bezels
- emulators, bios, estrutura roms
- TurboRama.exe launcher

NAO INCLUI
----------
- ROMs/jogos (copiar para roms\)
- saves de outra maquina

ATALHOS KIOSK
-------------
- Start = menu principal com senha
- F11 = painel locadora
- F10 moeda | F8 pausa | F7 parar | F12 zerar
"@
$readmePath = Join-Path $OutputDir "LEIA-ME-INSTALACAO.txt"
Set-Content -Path $readmePath -Value $readme -Encoding UTF8
Ok $readmePath

Step "CONCLUIDO"
Write-Host ("Pasta: " + $OutputDir) -ForegroundColor Green
Get-ChildItem $OutputDir | Select-Object Name, @{N = "MB"; E = { [math]::Round($_.Length / 1MB, 1) } }, LastWriteTime | Format-Table -AutoSize
Write-Host "Distribua setup.exe + todos os .pkg.### juntos." -ForegroundColor Yellow
