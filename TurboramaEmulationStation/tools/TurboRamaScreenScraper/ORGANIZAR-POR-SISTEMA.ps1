# Organiza / enche screensaver_videos\{sistema}\ com videos
# - Cria pastas com nomes de sistema ES (psx, switch, n64, ...)
# - Opcional: copia de roms\<sistema>\media\videos
# Nome do ficheiro NAO importa.

$ErrorActionPreference = "Continue"
$ES = "D:\Turborama\emulationstation"
$DestRoot = Join-Path $ES "screensaver_videos"
$RomsRoot = "D:\Turborama\roms"

# Nomes = pastas em es_systems / roms (padrao EmulationStation)
$Systems = @(
  "psx","ps2","ps3","ps4","ps5","psp","psvita",
  "switch","n64","snes","nes","megadrive","mastersystem","dreamcast","saturn",
  "gamecube","wii","wiiu","gba","gbc","gb","nds","3ds",
  "xbox","xbox360","xboxone","neogeo","arcade","mame","windows","pc"
)

Write-Host "Destino: $DestRoot"
foreach ($s in $Systems) {
  $p = Join-Path $DestRoot $s
  if (-not (Test-Path $p)) { New-Item -ItemType Directory -Path $p -Force | Out-Null }
}

# Mover ficheiros soltos na raiz para pasta (heuristica de prefixo)
Get-ChildItem $DestRoot -File -Include *.mp4,*.mkv,*.avi,*.webm,*.mov -EA SilentlyContinue | ForEach-Object {
  $n = $_.BaseName.ToLower()
  $sys = $null
  foreach ($s in $Systems) {
    if ($n -like "${s}_*" -or $n -like "*_${s}_*" -or $n.StartsWith($s)) { $sys = $s; break }
  }
  if ($n -match 'switch') { $sys = 'switch' }
  if ($n -match 'xbox|forza|halo|gears|grounded') { $sys = 'windows' }
  if ($sys) {
    $dst = Join-Path (Join-Path $DestRoot $sys) $_.Name
    if (-not (Test-Path $dst)) { Move-Item $_.FullName $dst -Force; Write-Host "MOVE $($_.Name) -> $sys\" }
  }
}

Write-Host ""
Write-Host "Copiar de roms\<sistema>\media\videos ? (S/N)"
$ans = Read-Host
if ($ans -match '^[sSyY]') {
  $maxPer = 15
  foreach ($s in $Systems) {
    $src = Join-Path $RomsRoot "$s\media\videos"
    if (-not (Test-Path $src)) { continue }
    $dest = Join-Path $DestRoot $s
    $existing = @(Get-ChildItem $dest -File -Include *.mp4,*.mkv,*.avi -EA SilentlyContinue).Count
    if ($existing -ge $maxPer) { Write-Host "SKIP $s (ja tem $existing)"; continue }

    $files = Get-ChildItem $src -Recurse -File -Include *.mp4,*.mkv,*.avi,*.webm -EA SilentlyContinue |
      Where-Object { $_.Length -gt 100KB } |
      Sort-Object Length -Descending |
      Select-Object -First ($maxPer - $existing)

    $n = 0
    foreach ($f in $files) {
      $dst = Join-Path $dest $f.Name
      if (Test-Path $dst) { continue }
      Copy-Item $f.FullName $dst -Force
      $n++
    }
    if ($n -gt 0) { Write-Host "COPY $s : +$n videos de $src" }
  }
}

Write-Host ""
Write-Host "=== RESUMO ==="
Get-ChildItem $DestRoot -Directory | ForEach-Object {
  $c = @(Get-ChildItem $_.FullName -File -Include *.mp4,*.mkv,*.avi,*.webm,*.mov -EA SilentlyContinue).Count
  if ($c -gt 0) { Write-Host ("  {0,-16} {1,4} videos" -f $_.Name, $c) }
}
Write-Host ""
Write-Host "Bezel: pasta = nome do sistema ES (ex: switch, psx, n64)"
Write-Host "No ES: ScreenSaverDecorations = systems"
