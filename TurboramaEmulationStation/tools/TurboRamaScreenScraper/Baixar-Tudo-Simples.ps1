# ============================================================
# TurboRama - Download SIMPLES ScreenScraper (lista fixa)
#
# 1) Preencha config.json (ssid, sspassword, devid, devpassword)
# 2) Execute BAIXAR-TUDO.bat
#
# Com conta: pesquisa o nome no ScreenScraper e baixa o video.
# Sem conta: usa so IDs ja conhecidos na lista.
# Destino: D:\Turborama\emulationstation\screensaver_videos
# ============================================================

$ErrorActionPreference = "Continue"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Destino   = "D:\Turborama\emulationstation\screensaver_videos"
$ConfigPath = Join-Path $ScriptDir "config.json"
$Headers = @{
  "User-Agent" = "TurboRamaSSDownloader/2.0"
  "Referer"    = "https://www.screenscraper.fr/"
  "Accept"     = "application/json"
}

# systemeid: Switch=225  PC=138  PS4=60  PS5=284  XboxOne=34
# Id>0 = ID conhecido; Id=0 = descobrir via API (precisa config.json)
$Lista = @(
  # Switch
  @{ G="Switch"; N="Zelda_Breath_of_the_Wild";     S=225; Id=195745; Q="Breath of the Wild" }
  @{ G="Switch"; N="Zelda_Tears_of_the_Kingdom";   S=225; Id=430463; Q="Tears of the Kingdom" }
  @{ G="Switch"; N="Super_Mario_Odyssey";          S=225; Id=195863; Q="Super Mario Odyssey" }
  @{ G="Switch"; N="Mario_Kart_8_Deluxe";          S=225; Id=195801; Q="Mario Kart 8 Deluxe" }
  @{ G="Switch"; N="Super_Smash_Bros_Ultimate";    S=225; Id=197929; Q="Super Smash Bros Ultimate" }
  @{ G="Switch"; N="Luigis_Mansion_3";             S=225; Id=359463; Q="Luigi Mansion 3" }
  @{ G="Switch"; N="Pokemon_Legends_Arceus";       S=225; Id=415981; Q="Pokemon Legends Arceus" }
  @{ G="Switch"; N="Animal_Crossing_New_Horizons"; S=225; Id=0;      Q="Animal Crossing New Horizons" }
  @{ G="Switch"; N="Metroid_Prime_Remastered";     S=225; Id=0;      Q="Metroid Prime Remastered" }
  @{ G="Switch"; N="Super_Mario_Bros_Wonder";      S=225; Id=0;      Q="Super Mario Bros Wonder" }

  # Xbox / Microsoft -> PC
  @{ G="XboxPC"; N="Grounded";                     S=138; Id=303530; Q="Grounded" }
  @{ G="XboxPC"; N="Forza_Horizon_5";              S=138; Id=0;      Q="Forza Horizon 5" }
  @{ G="XboxPC"; N="Forza_Motorsport";             S=138; Id=0;      Q="Forza Motorsport" }
  @{ G="XboxPC"; N="Halo_Infinite";                S=138; Id=0;      Q="Halo Infinite" }
  @{ G="XboxPC"; N="Halo_Master_Chief_Collection"; S=138; Id=0;      Q="Halo Master Chief Collection" }
  @{ G="XboxPC"; N="Gears_5";                      S=138; Id=0;      Q="Gears 5" }
  @{ G="XboxPC"; N="Gears_Tactics";                S=138; Id=0;      Q="Gears Tactics" }
  @{ G="XboxPC"; N="Sea_of_Thieves";               S=138; Id=0;      Q="Sea of Thieves" }
  @{ G="XboxPC"; N="State_of_Decay_2";             S=138; Id=0;      Q="State of Decay 2" }
  @{ G="XboxPC"; N="Pentiment";                    S=138; Id=0;      Q="Pentiment" }
  @{ G="XboxPC"; N="Hi_Fi_Rush";                   S=138; Id=0;      Q="Hi-Fi Rush" }
  @{ G="XboxPC"; N="Microsoft_Flight_Simulator";   S=138; Id=0;      Q="Microsoft Flight Simulator" }
  @{ G="XboxPC"; N="Age_of_Empires_II_DE";         S=138; Id=0;      Q="Age of Empires II" }
  @{ G="XboxPC"; N="Age_of_Empires_IV";            S=138; Id=0;      Q="Age of Empires IV" }
  @{ G="XboxPC"; N="Hellblade_II";                 S=138; Id=0;      Q="Hellblade II" }
  @{ G="XboxPC"; N="Indiana_Jones_Great_Circle";   S=138; Id=0;      Q="Indiana Jones Great Circle" }
  @{ G="XboxPC"; N="Avowed";                       S=138; Id=0;      Q="Avowed" }
  @{ G="XboxPC"; N="South_of_Midnight";            S=138; Id=0;      Q="South of Midnight" }
  @{ G="XboxPC"; N="Ara_History_Untold";           S=138; Id=0;      Q="Ara History Untold" }

  # PlayStation -> PC (pesquisa no sistema PS4/PS5/PC)
  @{ G="PSPC"; N="Spider_Man_Remastered";          S=60;  Id=0;      Q="Spider-Man Remastered" }
  @{ G="PSPC"; N="Spider_Man_Miles_Morales";       S=284; Id=475391; Q="Miles Morales" }
  @{ G="PSPC"; N="God_of_War_2018";                S=60;  Id=0;      Q="God of War" }
  @{ G="PSPC"; N="God_of_War_Ragnarok";            S=60;  Id=0;      Q="God of War Ragnarok" }
  @{ G="PSPC"; N="Horizon_Zero_Dawn";              S=60;  Id=0;      Q="Horizon Zero Dawn" }
  @{ G="PSPC"; N="Horizon_Forbidden_West";         S=60;  Id=0;      Q="Horizon Forbidden West" }
  @{ G="PSPC"; N="Days_Gone";                      S=60;  Id=0;      Q="Days Gone" }
  @{ G="PSPC"; N="Ghost_of_Tsushima";              S=60;  Id=0;      Q="Ghost of Tsushima" }
  @{ G="PSPC"; N="Ratchet_Rift_Apart";             S=60;  Id=0;      Q="Ratchet Rift Apart" }
  @{ G="PSPC"; N="Returnal";                       S=60;  Id=0;      Q="Returnal" }
  @{ G="PSPC"; N="Sackboy";                        S=60;  Id=0;      Q="Sackboy" }
  @{ G="PSPC"; N="Uncharted_Legacy";               S=60;  Id=0;      Q="Uncharted Legacy of Thieves" }
  @{ G="PSPC"; N="Last_of_Us_Part_I";              S=60;  Id=0;      Q="The Last of Us Part I" }
  @{ G="PSPC"; N="Helldivers_2";                   S=284; Id=0;      Q="Helldivers 2" }
  @{ G="PSPC"; N="Until_Dawn";                     S=60;  Id=0;      Q="Until Dawn" }
  @{ G="PSPC"; N="LEGO_Horizon_Adventures";        S=284; Id=0;      Q="LEGO Horizon Adventures" }
)

function Load-Config {
  if (-not (Test-Path $ConfigPath)) { return $null }
  try {
    $c = Get-Content $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($c.devid -and $c.devpassword -and $c.ssid -and $c.sspassword) { return $c }
  } catch {}
  return $null
}

function Enc([string]$s) { [uri]::EscapeDataString($s) }

function Api-Search($cfg, [int]$sysId, [string]$query) {
  $base = "https://api.screenscraper.fr/api2/jeuRecherche.php"
  $q = "devid=$(Enc $cfg.devid)&devpassword=$(Enc $cfg.devpassword)&softname=$(Enc $(if($cfg.softname){$cfg.softname}else{'TurboRamaSSDownloader'}))&output=json"
  $q += "&ssid=$(Enc $cfg.ssid)&sspassword=$(Enc $cfg.sspassword)"
  $q += "&systemeid=$sysId&recherche=$(Enc $query)"
  $url = "$base?$q"
  $data = Invoke-RestMethod -Uri $url -Headers $Headers -TimeoutSec 60
  $arr = $null
  if ($data.response.jeux.jeu) { $arr = $data.response.jeux.jeu }
  elseif ($data.jeux.jeu) { $arr = $data.jeux.jeu }
  if (-not $arr) { return $null }
  if ($arr -isnot [System.Array]) { $arr = @($arr) }
  return $arr[0]  # primeiro resultado
}

function Api-GameInfo($cfg, [string]$gameId) {
  $base = "https://api.screenscraper.fr/api2/jeuInfos.php"
  $q = "devid=$(Enc $cfg.devid)&devpassword=$(Enc $cfg.devpassword)&softname=$(Enc $(if($cfg.softname){$cfg.softname}else{'TurboRamaSSDownloader'}))&output=json"
  $q += "&ssid=$(Enc $cfg.ssid)&sspassword=$(Enc $cfg.sspassword)&gameid=$(Enc $gameId)"
  return Invoke-RestMethod -Uri "$base?$q" -Headers $Headers -TimeoutSec 60
}

function Extract-VideoUrl($jeu) {
  $medias = $null
  if ($jeu.medias.media) { $medias = $jeu.medias.media }
  if (-not $medias) { return $null }
  $list = if ($medias -is [System.Array]) { $medias } else { @($medias) }
  foreach ($t in @("video-normalized", "video")) {
    $m = $list | Where-Object { $_.type -eq $t } | Select-Object -First 1
    if ($m -and $m.url) {
      $u = [string]$m.url
      $u = $u -replace "#screenscraperserveur#", "https://www.screenscraper.fr/"
      return $u
    }
  }
  return $null
}

function Baixar-Url([string]$url, [string]$outPath) {
  $url = $url -replace " ", "%20"
  $dir = Split-Path $outPath -Parent
  if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
  Invoke-WebRequest -Uri $url -OutFile $outPath -Headers $Headers -TimeoutSec 180 -UseBasicParsing
  if ((Test-Path $outPath) -and (Get-Item $outPath).Length -gt 50KB) { return $true }
  if (Test-Path $outPath) { Remove-Item $outPath -Force }
  return $false
}

function Baixar-PorId([int]$sysId, [int]$gameId, [string]$outPath) {
  if ($gameId -le 0) { return $false }
  foreach ($tipo in @("video-normalized", "video")) {
    $url = "https://www.screenscraper.fr/medias/$sysId/$gameId/$tipo.mp4"
    try {
      if (Baixar-Url $url $outPath) { return $true }
    } catch {}
  }
  return $false
}

# ---------- main ----------
if (-not (Test-Path $Destino)) { New-Item -ItemType Directory -Path $Destino -Force | Out-Null }

$cfg = Load-Config
Write-Host ""
Write-Host "=============================================="
Write-Host " TurboRama ScreenScraper - Download SIMPLES"
Write-Host " Destino : $Destino"
Write-Host " Jogos   : $($Lista.Count)"
if ($cfg) {
  Write-Host " Conta   : SIM ($($cfg.ssid)) - pesquisa API activa"
} else {
  Write-Host " Conta   : NAO - so IDs conhecidos"
  Write-Host "           Copie config.example.json -> config.json"
  Write-Host "           e preencha ssid/sspassword/devid/devpassword"
}
Write-Host "=============================================="
Write-Host ""

$ok = 0; $skip = 0; $falha = 0
$rel = @()

foreach ($j in $Lista) {
  $out = Join-Path $Destino ("{0}_{1}.mp4" -f $j.G, $j.N)
  Write-Host -NoNewline ("[{0,-6}] {1,-32} " -f $j.G, $j.N)

  if ((Test-Path $out) -and (Get-Item $out).Length -gt 50KB) {
    $mb = [math]::Round((Get-Item $out).Length/1MB, 2)
    Write-Host "JA EXISTE ($mb MB)" -ForegroundColor Cyan
    $skip++; $rel += [pscustomobject]@{Jogo=$j.N; Status="JA_EXISTE"; MB=$mb}
    continue
  }

  $done = $false
  $sysId = [int]$j.S
  $gameId = [int]$j.Id

  # 1) ID conhecido -> download directo
  if ($gameId -gt 0) {
    try { $done = Baixar-PorId $sysId $gameId $out } catch { $done = $false }
  }

  # 2) API pesquisa por nome (se tem conta)
  if (-not $done -and $cfg) {
    try {
      # tenta sistema pedido + PC + PS4 + PS5
      $trySys = @($sysId)
      if ($j.G -eq "PSPC") { $trySys = @($sysId, 60, 284, 138) | Select-Object -Unique }
      if ($j.G -eq "XboxPC") { $trySys = @(138, 34) | Select-Object -Unique }
      if ($j.G -eq "Switch") { $trySys = @(225) }

      foreach ($sid in $trySys) {
        $g = Api-Search $cfg $sid $j.Q
        if (-not $g) { continue }
        $gid = [string]$g.id
        if (-not $gid) { continue }

        # media da pesquisa ou jeuInfos
        $url = $null
        $info = $null
        try { $info = Api-GameInfo $cfg $gid } catch {}
        $jeu = $null
        if ($info.response.jeu) { $jeu = $info.response.jeu }
        elseif ($info.jeu) { $jeu = $info.jeu }
        elseif ($g) { $jeu = $g }
        if ($jeu) { $url = Extract-VideoUrl $jeu }

        if (-not $url) {
          $sid2 = $sid
          if ($g.systeme -and $g.systeme.id) { $sid2 = [int]$g.systeme.id }
          $url = "https://www.screenscraper.fr/medias/$sid2/$gid/video-normalized.mp4"
        }

        try {
          if (Baixar-Url $url $out) { $done = $true; $sysId = $sid; $gameId = [int]$gid; break }
        } catch {}
        Start-Sleep -Milliseconds 400
      }
    } catch {
      Write-Host -NoNewline "(API: $($_.Exception.Message)) "
    }
  }

  if ($done) {
    $mb = [math]::Round((Get-Item $out).Length/1MB, 2)
    Write-Host "OK $mb MB" -ForegroundColor Green
    $ok++; $rel += [pscustomobject]@{Jogo=$j.N; Status="OK"; MB=$mb}
  } else {
    if (-not $cfg -and $gameId -le 0) {
      Write-Host "FALTA (precisa config.json com conta SS)" -ForegroundColor Yellow
    } else {
      Write-Host "FALHOU (sem video no SS ou sem resultado)" -ForegroundColor Red
    }
    $falha++; $rel += [pscustomobject]@{Jogo=$j.N; Status="FALHA"; MB=0}
  }
  Start-Sleep -Milliseconds 350
}

# limpar duplicados obvios (mesmo tamanho, nomes longos antigos)
# manter apenas padrao Grupo_Nome.mp4 da lista
Write-Host ""
Write-Host "=============================================="
Write-Host " RESULTADO: OK=$ok  JA_EXISTE=$skip  FALHA=$falha"
$files = @(Get-ChildItem $Destino -Filter *.mp4 -EA SilentlyContinue)
Write-Host " Videos na pasta: $($files.Count)"
Write-Host "=============================================="
$files | Sort-Object Name | ForEach-Object {
  Write-Host ("  {0,-50} {1,6} MB" -f $_.Name, [math]::Round($_.Length/1MB,2))
}
$rel | ConvertTo-Json | Set-Content (Join-Path $Destino "relatorio-simples.json") -Encoding UTF8
Write-Host ""
Write-Host "Relatorio: $Destino\relatorio-simples.json"
if (-not $cfg) {
  Write-Host ""
  Write-Host ">>> PARA BAIXAR OS QUE FALTAM:" -ForegroundColor Yellow
  Write-Host ">>> 1. Conta em https://www.screenscraper.fr/membreinscription.php"
  Write-Host ">>> 2. DevID no forum developers do ScreenScraper"
  Write-Host ">>> 3. Edite: $ConfigPath"
  Write-Host ">>> 4. Corra de novo BAIXAR-TUDO.bat"
}
