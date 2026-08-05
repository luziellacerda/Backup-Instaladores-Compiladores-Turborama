# TurboRama - Download em lote da lista-jogos.json (API ScreenScraper)
# Uso: .\Baixar-Lista.ps1
# Requer config.json com ssid, sspassword, devid, devpassword

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ConfigPath = Join-Path $ScriptDir "config.json"
$ListaPath = Join-Path $ScriptDir "lista-jogos.json"
$InstallRoot = $env:TURBORAMA_INSTALL_ROOT
if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
  $InstallRoot = @('D:\emulationstation', 'D:\Turborama\emulationstation') |
    Where-Object { Test-Path -LiteralPath (Join-Path $_ 'emulationstation.exe') -PathType Leaf } |
    Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($InstallRoot)) { $InstallRoot = 'D:\emulationstation' }
$DefaultOut = Join-Path ([IO.Path]::GetFullPath($InstallRoot)) 'screensaver_videos'
$LogPath = Join-Path $ScriptDir "download-log.txt"

function Url-Encode([string]$s) { [uri]::EscapeDataString($s) }

function Load-Config {
  if (-not (Test-Path $ConfigPath)) {
    throw "Falta config.json. Copie config.example.json e preencha ssid/sspassword/devid/devpassword."
  }
  $c = Get-Content $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
  if (-not $c.ssid -or -not $c.sspassword) { throw "config.json: preencha ssid e sspassword (conta screenscraper.fr)" }
  if (-not $c.devid -or -not $c.devpassword) { throw "config.json: preencha devid e devpassword (developer no forum)" }
  if (-not $c.softname) { $c | Add-Member -NotePropertyName softname -NotePropertyValue "TurboRamaSSDownloader" -Force }
  if (-not $c.outputDir) { $c | Add-Member -NotePropertyName outputDir -NotePropertyValue $DefaultOut -Force }
  return $c
}

function Build-BaseQuery($cfg) {
  $q = "devid=$(Url-Encode $cfg.devid)&devpassword=$(Url-Encode $cfg.devpassword)&softname=$(Url-Encode $cfg.softname)&output=json"
  $q += "&ssid=$(Url-Encode $cfg.ssid)&sspassword=$(Url-Encode $cfg.sspassword)"
  return $q
}

function Invoke-SSApi([string]$url) {
  $headers = @{ "User-Agent" = "TurboRamaSSDownloader/1.1"; "Accept" = "application/json" }
  return Invoke-RestMethod -Uri $url -Headers $headers -Method Get -TimeoutSec 90
}

function Sanitize-FileName([string]$name) {
  $invalid = [IO.Path]::GetInvalidFileNameChars() -join ''
  $re = "[{0}]" -f [regex]::Escape($invalid)
  $n = ($name -replace $re, "_") -replace "\s+", "_"
  if ($n.Length -gt 70) { $n = $n.Substring(0, 70) }
  if (-not $n) { $n = "video" }
  return $n.Trim("._")
}

function Get-NameFromNode($g) {
  $name = $null
  try {
    if ($g.noms) {
      $noms = $g.noms.nom
      if ($noms -is [System.Array]) {
        $n = $noms | Where-Object { $_.region -eq "ss" -or $_.region -eq "us" -or $_.region -eq "wor" } | Select-Object -First 1
        if (-not $n) { $n = $noms[0] }
        if ($n.'#text') { $name = $n.'#text' }
        elseif ($n.text) { $name = $n.text }
        else { $name = [string]$n }
      } else {
        if ($noms.'#text') { $name = $noms.'#text' }
        elseif ($noms.text) { $name = $noms.text }
        else { $name = [string]$noms }
      }
    }
    if (-not $name -and $g.nom) { $name = [string]$g.nom }
  } catch {}
  if (-not $name) { $name = "game_$($g.id)" }
  return $name
}

function Extract-GamesFromSearch($data) {
  $games = @()
  $node = $null
  if ($data.response) { $node = $data.response.jeux }
  if (-not $node) { $node = $data.jeux }
  if (-not $node) { return $games }
  $arr = $node.jeu
  if (-not $arr) { $arr = $node }
  if ($arr -isnot [System.Array]) { $arr = @($arr) }
  foreach ($g in $arr) {
    $games += [pscustomobject]@{
      Id   = $g.id
      Name = (Get-NameFromNode $g)
      Sys  = $(if ($g.systeme -and $g.systeme.id) { $g.systeme.id } else { $null })
      Raw  = $g
    }
  }
  return $games
}

function Pick-MediaUrl($medias, [string]$mediaType) {
  if (-not $medias) { return $null }
  $list = if ($medias -is [System.Array]) { $medias } else { @($medias) }
  $matches = @($list | Where-Object { $_.type -eq $mediaType })
  if ($matches.Count -eq 0) { return $null }
  $prefer = $matches | Where-Object { $_.region -eq "wor" -or $_.region -eq "us" -or $_.region -eq "eu" -or $_.region -eq "ss" } | Select-Object -First 1
  if ($prefer) { return $prefer.url }
  return $matches[0].url
}

function Extract-VideoUrl($gameNode, $preferNorm = $true) {
  $medias = $null
  if ($gameNode.medias) { $medias = $gameNode.medias.media }
  if (-not $medias -and $gameNode.media) { $medias = $gameNode.media }
  $types = if ($preferNorm) { @("video-normalized", "video") } else { @("video", "video-normalized") }
  foreach ($t in $types) {
    $url = Pick-MediaUrl $medias $t
    if ($url) { return $url }
  }
  $sid = $null; $gid = $null
  if ($gameNode.systeme -and $gameNode.systeme.id) { $sid = $gameNode.systeme.id }
  if ($gameNode.id) { $gid = $gameNode.id }
  if ($sid -and $gid) {
    $media = if ($preferNorm) { "video-normalized" } else { "video" }
    return "https://www.screenscraper.fr/medias/$sid/$gid/$media.mp4"
  }
  return $null
}

function Download-File([string]$url, [string]$outPath) {
  $url = $url -replace " ", "%20"
  $url = $url -replace "#screenscraperserveur#", "https://www.screenscraper.fr/"
  $dir = Split-Path $outPath -Parent
  if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
  $headers = @{ "User-Agent" = "TurboRamaSSDownloader/1.1" }
  Invoke-WebRequest -Uri $url -OutFile $outPath -Headers $headers -TimeoutSec 240
  if (-not (Test-Path $outPath) -or (Get-Item $outPath).Length -lt 2048) {
    throw "Download falhou ou ficheiro demasiado pequeno"
  }
}

function Log([string]$msg) {
  $ts = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
  $line = "[$ts] $msg"
  Write-Host $line
  Add-Content -Path $LogPath -Value $line -Encoding UTF8
}

# ---- main ----
$cfg = Load-Config
if (-not (Test-Path $ListaPath)) { throw "Falta lista-jogos.json" }
$lista = Get-Content $ListaPath -Raw -Encoding UTF8 | ConvertFrom-Json
$outDir = if ($cfg.outputDir) { $cfg.outputDir } else { $DefaultOut }
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

Log "=== Inicio download lista ($($lista.Count) jogos) -> $outDir ==="
$ok = 0; $fail = 0; $skip = 0
$results = @()

foreach ($item in $lista) {
  $query = [string]$item.query
  $sysId = [int]$item.systemId
  $plat = [string]$item.platform
  $group = [string]$item.group
  $tag = "$group | $plat | $query"
  try {
    Log "Pesquisar: $tag (systemeid=$sysId)"
    $base = "https://api.screenscraper.fr/api2/jeuRecherche.php"
    $url = "$base?$(Build-BaseQuery $cfg)&systemeid=$sysId&recherche=$(Url-Encode $query)"
    $data = Invoke-SSApi $url

    # detect login errors in raw response
    $rawProbe = $data | ConvertTo-Json -Depth 3 -Compress -ErrorAction SilentlyContinue
    if ($rawProbe -match "Erreur de login|identifiants") {
      throw "Erro de login ScreenScraper (verifique devid/ssid no config.json)"
    }

    $games = Extract-GamesFromSearch $data
    if ($games.Count -eq 0) { throw "Nenhum resultado na pesquisa" }

    # pick best match: first result whose name contains first significant words
    $pick = $games[0]
    $words = ($query -split '\s+' | Where-Object { $_.Length -gt 2 }) | Select-Object -First 2
    foreach ($g in $games) {
      $hit = $true
      foreach ($w in $words) {
        if ($g.Name -notmatch [regex]::Escape($w)) { $hit = $false; break }
      }
      if ($hit) { $pick = $g; break }
    }

    Log "  Match: $($pick.Name) (id=$($pick.Id))"

    # game info by id for medias
    $infoUrl = "https://api.screenscraper.fr/api2/jeuInfos.php?$(Build-BaseQuery $cfg)&gameid=$($pick.Id)"
    $info = $null
    try { $info = Invoke-SSApi $infoUrl } catch { Log "  jeuInfos falhou: $($_.Exception.Message)" }

    $gameNode = $null
    if ($info -and $info.response -and $info.response.jeu) { $gameNode = $info.response.jeu }
    elseif ($info -and $info.jeu) { $gameNode = $info.jeu }
    elseif ($pick.Raw) { $gameNode = $pick.Raw }

    $vidUrl = $null
    if ($gameNode) { $vidUrl = Extract-VideoUrl $gameNode $true }
    if (-not $vidUrl -and $pick.Id) {
      $sid = if ($pick.Sys) { $pick.Sys } else { $sysId }
      $vidUrl = "https://www.screenscraper.fr/medias/$sid/$($pick.Id)/video-normalized.mp4"
    }
    if (-not $vidUrl) { throw "Sem URL de video" }

    $file = "$(Sanitize-FileName $group)_$(Sanitize-FileName $query)_$($pick.Id).mp4"
    $out = Join-Path $outDir $file
    if ((Test-Path $out) -and (Get-Item $out).Length -gt 50KB) {
      Log "  SKIP ja existe: $file"
      $skip++
      $results += [pscustomobject]@{ Status="SKIP"; Group=$group; Query=$query; File=$file; Msg="ja existe" }
      Start-Sleep -Milliseconds 400
      continue
    }

    Log "  Download: $vidUrl"
    Download-File $vidUrl $out
    $mb = [math]::Round((Get-Item $out).Length / 1MB, 2)
    Log "  OK $file ($mb MB)"
    $ok++
    $results += [pscustomobject]@{ Status="OK"; Group=$group; Query=$query; File=$file; Msg="$mb MB" }
  } catch {
    Log "  FALHA: $($_.Exception.Message)"
    $fail++
    $results += [pscustomobject]@{ Status="FALHA"; Group=$group; Query=$query; File=""; Msg=$_.Exception.Message }
  }
  # rate limit gentil
  Start-Sleep -Seconds 2
}

Log "=== Fim: OK=$ok SKIP=$skip FALHA=$fail ==="
$report = Join-Path $outDir "relatorio-download.json"
$results | ConvertTo-Json -Depth 4 | Set-Content $report -Encoding UTF8
Log "Relatorio: $report"
$results | Format-Table Status, Group, Query, Msg -AutoSize | Out-String | Write-Host

if ($fail -gt 0 -and $ok -eq 0) { exit 2 }
exit 0
