# TurboRama ScreenScraper Downloader
# Baixa videos/medias da API ScreenScraper.fr para a pasta do screensaver.
# Requer conta em https://www.screenscraper.fr/

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ConfigPath = Join-Path $ScriptDir "config.json"
$DefaultOut = "D:\Turborama\emulationstation\screensaver_videos"

# systemeid ScreenScraper (API v2) — IDs oficiais do site
$Platforms = [ordered]@{
  "PlayStation 2 (PS2)"     = 58
  "PlayStation 3 (PS3)"     = 59
  "PlayStation 4 (PS4)"     = 60
  "PlayStation 5 (PS5)"     = 284
  "Nintendo Switch"         = 225
  "Xbox"                    = 32
  "Xbox 360"                = 33
  "Xbox One"                = 34
  "PlayStation (PS1)"       = 57
  "PSP"                     = 61
  "PS Vita"                 = 62
  "Nintendo Wii"            = 16
  "Nintendo Wii U"          = 18
  "Nintendo GameCube"       = 13
  "Nintendo 64"             = 14
  "Super Nintendo (SNES)"   = 4
  "Nintendo Entertainment System (NES)" = 3
  "Game Boy Advance"        = 12
  "Game Boy Color"          = 10
  "Game Boy"                = 9
  "Nintendo DS"             = 15
  "Nintendo 3DS"            = 17
  "Mega Drive / Genesis"    = 1
  "Master System"           = 2
  "Dreamcast"               = 23
  "Saturn"                  = 22
  "Neo Geo"                 = 142
  "Arcade (MAME)"           = 75
}

function Load-Config {
  if (-not (Test-Path $ConfigPath)) {
    return [pscustomobject]@{
      ssid = ""
      sspassword = ""
      devid = ""
      devpassword = ""
      softname = "TurboRamaSSDownloader"
      outputDir = $DefaultOut
      preferNormalizedVideo = $true
      region = "us"
    }
  }
  return (Get-Content $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json)
}

function Save-Config($cfg) {
  $cfg | ConvertTo-Json -Depth 5 | Set-Content -Path $ConfigPath -Encoding UTF8
}

function Url-Encode([string]$s) {
  return [uri]::EscapeDataString($s)
}

function Build-BaseQuery($cfg) {
  $q = "devid=$(Url-Encode $cfg.devid)&devpassword=$(Url-Encode $cfg.devpassword)&softname=$(Url-Encode $cfg.softname)&output=json"
  if ($cfg.ssid -and $cfg.sspassword) {
    $q += "&ssid=$(Url-Encode $cfg.ssid)&sspassword=$(Url-Encode $cfg.sspassword)"
  }
  return $q
}

function Invoke-SSApi([string]$url) {
  $headers = @{
    "User-Agent" = "TurboRamaSSDownloader/1.0"
    "Accept"     = "application/json"
  }
  return Invoke-RestMethod -Uri $url -Headers $headers -Method Get -TimeoutSec 60
}

function Get-GameSearch($cfg, [int]$systemId, [string]$query) {
  $base = "https://api.screenscraper.fr/api2/jeuRecherche.php"
  $url = "$base?$(Build-BaseQuery $cfg)&systemeid=$systemId&recherche=$(Url-Encode $query)"
  return Invoke-SSApi $url
}

function Get-GameInfo($cfg, [int]$systemId, [string]$romName) {
  $base = "https://api.screenscraper.fr/api2/jeuInfos.php"
  $url = "$base?$(Build-BaseQuery $cfg)&systemeid=$systemId&romtype=rom&romnom=$(Url-Encode $romName)"
  return Invoke-SSApi $url
}

function Get-GameInfoById($cfg, [string]$gameId) {
  $base = "https://api.screenscraper.fr/api2/jeuInfos.php"
  $url = "$base?$(Build-BaseQuery $cfg)&gameid=$(Url-Encode $gameId)"
  return Invoke-SSApi $url
}

function Pick-MediaUrl($medias, [string]$mediaType, [string]$region) {
  if (-not $medias) { return $null }
  $list = @()
  if ($medias -is [System.Array]) { $list = $medias }
  else { $list = @($medias) }

  $matches = $list | Where-Object { $_.type -eq $mediaType }
  if (-not $matches) { return $null }

  $byRegion = $matches | Where-Object { $_.region -eq $region -or $_.region -eq "wor" -or $_.region -eq "us" -or $_.region -eq "eu" -or $_.region -eq "jp" }
  $pick = if ($byRegion) { @($byRegion)[0] } else { @($matches)[0] }
  return $pick.url
}

function Extract-GamesFromSearch($data) {
  $games = @()
  try {
    $node = $data.response.jeux
    if (-not $node) { $node = $data.jeux }
    if (-not $node) { return $games }

    $arr = $node.jeu
    if (-not $arr) { $arr = $node }
    if ($arr -isnot [System.Array]) { $arr = @($arr) }

    foreach ($g in $arr) {
      $name = $null
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
      if (-not $name) { $name = "Jogo #$($g.id)" }

      $games += [pscustomobject]@{
        Id   = $g.id
        Name = $name
        Sys  = $g.systeme.id
        Raw  = $g
      }
    }
  } catch {
    # ignore parse quirks
  }
  return $games
}

function Extract-VideoUrl($gameNode, $cfg) {
  # Prefer structured medias
  $medias = $null
  if ($gameNode.medias) { $medias = $gameNode.medias.media }
  if (-not $medias -and $gameNode.media) { $medias = $gameNode.media }

  $types = @()
  if ($cfg.preferNormalizedVideo) {
    $types = @("video-normalized", "video")
  } else {
    $types = @("video", "video-normalized")
  }

  foreach ($t in $types) {
    $url = Pick-MediaUrl $medias $t $cfg.region
    if ($url) { return $url }
  }

  # Fallback URL pattern used by many scrapers
  $sid = $null
  $gid = $null
  if ($gameNode.systeme -and $gameNode.systeme.id) { $sid = $gameNode.systeme.id }
  if ($gameNode.id) { $gid = $gameNode.id }
  if ($sid -and $gid) {
    $media = if ($cfg.preferNormalizedVideo) { "video-normalized" } else { "video" }
    return "https://www.screenscraper.fr/medias/$sid/$gid/$media.mp4"
  }
  return $null
}

function Download-File([string]$url, [string]$outPath) {
  if (-not $url) { throw "URL de media vazia" }
  $url = $url -replace " ", "%20"
  $url = $url -replace "#screenscraperserveur#", "https://www.screenscraper.fr/"

  $dir = Split-Path $outPath -Parent
  if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

  $headers = @{ "User-Agent" = "TurboRamaSSDownloader/1.0" }
  Invoke-WebRequest -Uri $url -OutFile $outPath -Headers $headers -TimeoutSec 180
  if (-not (Test-Path $outPath) -or (Get-Item $outPath).Length -lt 1024) {
    throw "Download falhou ou ficheiro demasiado pequeno"
  }
}

function Sanitize-FileName([string]$name) {
  $invalid = [IO.Path]::GetInvalidFileNameChars() -join ''
  $re = "[{0}]" -f [regex]::Escape($invalid)
  $n = $name -replace $re, "_"
  $n = ($n -replace "\s+", "_").Trim("._")
  if ($n.Length -gt 80) { $n = $n.Substring(0, 80) }
  if (-not $n) { $n = "video" }
  return $n
}

# ---------------- UI ----------------
$cfg = Load-Config

$form = New-Object System.Windows.Forms.Form
$form.Text = "TurboRama - ScreenScraper Downloader"
$form.Size = New-Object System.Drawing.Size(820, 640)
$form.StartPosition = "CenterScreen"
$form.MinimumSize = New-Object System.Drawing.Size(700, 520)

$lblUser = New-Object System.Windows.Forms.Label
$lblUser.Text = "Utilizador ScreenScraper (ssid):"
$lblUser.Location = New-Object System.Drawing.Point(12, 14)
$lblUser.AutoSize = $true
$form.Controls.Add($lblUser)

$txtUser = New-Object System.Windows.Forms.TextBox
$txtUser.Location = New-Object System.Drawing.Point(220, 10)
$txtUser.Width = 180
$txtUser.Text = $cfg.ssid
$form.Controls.Add($txtUser)

$lblPass = New-Object System.Windows.Forms.Label
$lblPass.Text = "Senha (sspassword):"
$lblPass.Location = New-Object System.Drawing.Point(420, 14)
$lblPass.AutoSize = $true
$form.Controls.Add($lblPass)

$txtPass = New-Object System.Windows.Forms.TextBox
$txtPass.Location = New-Object System.Drawing.Point(560, 10)
$txtPass.Width = 180
$txtPass.UseSystemPasswordChar = $true
$txtPass.Text = $cfg.sspassword
$form.Controls.Add($txtPass)

$lblDev = New-Object System.Windows.Forms.Label
$lblDev.Text = "DevID / DevPassword / SoftName:"
$lblDev.Location = New-Object System.Drawing.Point(12, 44)
$lblDev.AutoSize = $true
$form.Controls.Add($lblDev)

$txtDevId = New-Object System.Windows.Forms.TextBox
$txtDevId.Location = New-Object System.Drawing.Point(220, 40)
$txtDevId.Width = 120
$txtDevId.Text = $cfg.devid
$form.Controls.Add($txtDevId)

$txtDevPass = New-Object System.Windows.Forms.TextBox
$txtDevPass.Location = New-Object System.Drawing.Point(350, 40)
$txtDevPass.Width = 120
$txtDevPass.UseSystemPasswordChar = $true
$txtDevPass.Text = $cfg.devpassword
$form.Controls.Add($txtDevPass)

$txtSoft = New-Object System.Windows.Forms.TextBox
$txtSoft.Location = New-Object System.Drawing.Point(480, 40)
$txtSoft.Width = 260
$txtSoft.Text = $(if ($cfg.softname) { $cfg.softname } else { "TurboRamaSSDownloader" })
$form.Controls.Add($txtSoft)

$lblOut = New-Object System.Windows.Forms.Label
$lblOut.Text = "Pasta destino (screensaver):"
$lblOut.Location = New-Object System.Drawing.Point(12, 74)
$lblOut.AutoSize = $true
$form.Controls.Add($lblOut)

$txtOut = New-Object System.Windows.Forms.TextBox
$txtOut.Location = New-Object System.Drawing.Point(220, 70)
$txtOut.Width = 440
$txtOut.Text = $(if ($cfg.outputDir) { $cfg.outputDir } else { $DefaultOut })
$form.Controls.Add($txtOut)

$btnBrowse = New-Object System.Windows.Forms.Button
$btnBrowse.Text = "..."
$btnBrowse.Location = New-Object System.Drawing.Point(670, 68)
$btnBrowse.Width = 40
$btnBrowse.Add_Click({
  $fbd = New-Object System.Windows.Forms.FolderBrowserDialog
  $fbd.SelectedPath = $txtOut.Text
  if ($fbd.ShowDialog() -eq "OK") { $txtOut.Text = $fbd.SelectedPath }
})
$form.Controls.Add($btnBrowse)

$btnSave = New-Object System.Windows.Forms.Button
$btnSave.Text = "Guardar config"
$btnSave.Location = New-Object System.Drawing.Point(720, 68)
$btnSave.Width = 70
$btnSave.Add_Click({
  $c = [pscustomobject]@{
    ssid = $txtUser.Text.Trim()
    sspassword = $txtPass.Text
    devid = $txtDevId.Text.Trim()
    devpassword = $txtDevPass.Text
    softname = $txtSoft.Text.Trim()
    outputDir = $txtOut.Text.Trim()
    preferNormalizedVideo = $true
    region = "us"
  }
  Save-Config $c
  [System.Windows.Forms.MessageBox]::Show("Config guardada em config.json", "OK")
})
$form.Controls.Add($btnSave)

$lblPlat = New-Object System.Windows.Forms.Label
$lblPlat.Text = "Plataforma:"
$lblPlat.Location = New-Object System.Drawing.Point(12, 110)
$lblPlat.AutoSize = $true
$form.Controls.Add($lblPlat)

$cmbPlat = New-Object System.Windows.Forms.ComboBox
$cmbPlat.Location = New-Object System.Drawing.Point(100, 106)
$cmbPlat.Width = 280
$cmbPlat.DropDownStyle = "DropDownList"
foreach ($k in $Platforms.Keys) { [void]$cmbPlat.Items.Add($k) }
$cmbPlat.SelectedIndex = 0
$form.Controls.Add($cmbPlat)

$lblQ = New-Object System.Windows.Forms.Label
$lblQ.Text = "Pesquisar jogo:"
$lblQ.Location = New-Object System.Drawing.Point(400, 110)
$lblQ.AutoSize = $true
$form.Controls.Add($lblQ)

$txtQuery = New-Object System.Windows.Forms.TextBox
$txtQuery.Location = New-Object System.Drawing.Point(500, 106)
$txtQuery.Width = 200
$form.Controls.Add($txtQuery)

$btnSearch = New-Object System.Windows.Forms.Button
$btnSearch.Text = "Pesquisar"
$btnSearch.Location = New-Object System.Drawing.Point(710, 104)
$btnSearch.Width = 80
$form.Controls.Add($btnSearch)

$lst = New-Object System.Windows.Forms.ListView
$lst.Location = New-Object System.Drawing.Point(12, 140)
$lst.Size = New-Object System.Drawing.Size(780, 320)
$lst.View = "Details"
$lst.FullRowSelect = $true
$lst.GridLines = $true
[void]$lst.Columns.Add("ID", 70)
[void]$lst.Columns.Add("Nome", 520)
[void]$lst.Columns.Add("SysID", 80)
$form.Controls.Add($lst)

$btnDownload = New-Object System.Windows.Forms.Button
$btnDownload.Text = "Baixar VIDEO do selecionado"
$btnDownload.Location = New-Object System.Drawing.Point(12, 475)
$btnDownload.Width = 220
$btnDownload.Height = 32
$form.Controls.Add($btnDownload)

$btnOpenFolder = New-Object System.Windows.Forms.Button
$btnOpenFolder.Text = "Abrir pasta screensaver"
$btnOpenFolder.Location = New-Object System.Drawing.Point(250, 475)
$btnOpenFolder.Width = 180
$btnOpenFolder.Height = 32
$btnOpenFolder.Add_Click({
  $p = $txtOut.Text.Trim()
  if (-not (Test-Path $p)) { New-Item -ItemType Directory -Path $p -Force | Out-Null }
  Start-Process explorer.exe $p
})
$form.Controls.Add($btnOpenFolder)

$btnHelp = New-Object System.Windows.Forms.Button
$btnHelp.Text = "Ajuda / Site"
$btnHelp.Location = New-Object System.Drawing.Point(450, 475)
$btnHelp.Width = 120
$btnHelp.Height = 32
$btnHelp.Add_Click({ Start-Process "https://www.screenscraper.fr/" })
$form.Controls.Add($btnHelp)

$log = New-Object System.Windows.Forms.TextBox
$log.Location = New-Object System.Drawing.Point(12, 520)
$log.Size = New-Object System.Drawing.Size(780, 70)
$log.Multiline = $true
$log.ScrollBars = "Vertical"
$log.ReadOnly = $true
$form.Controls.Add($log)

function Log([string]$msg) {
  $ts = Get-Date -Format "HH:mm:ss"
  $log.AppendText("[$ts] $msg`r`n")
}

function Get-CfgFromUi {
  return [pscustomobject]@{
    ssid = $txtUser.Text.Trim()
    sspassword = $txtPass.Text
    devid = $txtDevId.Text.Trim()
    devpassword = $txtDevPass.Text
    softname = $txtSoft.Text.Trim()
    outputDir = $txtOut.Text.Trim()
    preferNormalizedVideo = $true
    region = "us"
  }
}

function Validate-Cfg($c) {
  if (-not $c.ssid -or -not $c.sspassword) {
    throw "Preencha utilizador e senha do ScreenScraper (conta gratuita no site)."
  }
  if (-not $c.devid -or -not $c.devpassword) {
    throw "Preencha DevID e DevPassword (peça no forum ScreenScraper como developer)."
  }
  if (-not $c.softname) { $c.softname = "TurboRamaSSDownloader" }
  if (-not $c.outputDir) { $c.outputDir = $DefaultOut }
}

$script:SearchResults = @()

$btnSearch.Add_Click({
  try {
    $c = Get-CfgFromUi
    Validate-Cfg $c
    Save-Config $c

    $q = $txtQuery.Text.Trim()
    if (-not $q) { throw "Digite o nome do jogo para pesquisar." }

    $platName = $cmbPlat.SelectedItem.ToString()
    $sysId = [int]$Platforms[$platName]

    $lst.Items.Clear()
    $script:SearchResults = @()
    Log "A pesquisar '$q' em $platName (systemeid=$sysId)..."
    [System.Windows.Forms.Application]::DoEvents()

    $data = Get-GameSearch $c $sysId $q
    $games = Extract-GamesFromSearch $data
    if ($games.Count -eq 0) {
      # tenta sem filtrar system no parse; alguns JSON diferem
      Log "Nenhum resultado no parse principal. A tentar estrutura alternativa..."
      $json = $data | ConvertTo-Json -Depth 20
      if ($json -match '"id"\s*:\s*"?(\d+)"?') {
        Log "Resposta recebida (verifique credenciais/limites se vazio)."
      }
      throw "Nenhum jogo encontrado. Verifique nome, plataforma e credenciais."
    }

    $script:SearchResults = $games
    foreach ($g in $games) {
      $item = New-Object System.Windows.Forms.ListViewItem($g.Id)
      [void]$item.SubItems.Add($g.Name)
      [void]$item.SubItems.Add([string]$g.Sys)
      $item.Tag = $g
      [void]$lst.Items.Add($item)
    }
    Log "Encontrados: $($games.Count) jogo(s)."
  } catch {
    Log "ERRO: $($_.Exception.Message)"
    [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, "Erro", "OK", "Error")
  }
})

$btnDownload.Add_Click({
  try {
    if ($lst.SelectedItems.Count -eq 0) { throw "Selecione um jogo na lista." }
    $c = Get-CfgFromUi
    Validate-Cfg $c
    Save-Config $c

    $g = $lst.SelectedItems[0].Tag
    $name = $g.Name
    $sysId = if ($g.Sys) { [int]$g.Sys } else { [int]$Platforms[$cmbPlat.SelectedItem.ToString()] }

    Log "A obter media de '$name'..."
    [System.Windows.Forms.Application]::DoEvents()

    # Preferir gameid (mais fiavel); fallback por nome; depois dados da pesquisa
    $info = $null
    try {
      if ($g.Id) {
        Log "A pedir medias por gameid=$($g.Id)..."
        $info = Get-GameInfoById $c ([string]$g.Id)
      }
    } catch {
      Log "jeuInfos por id falhou: $($_.Exception.Message)"
    }
    if (-not $info) {
      try {
        $info = Get-GameInfo $c $sysId $name
      } catch {
        Log "jeuInfos por nome falhou, a usar dados da pesquisa..."
      }
    }

    $gameNode = $null
    if ($info -and $info.response -and $info.response.jeu) { $gameNode = $info.response.jeu }
    elseif ($info -and $info.jeu) { $gameNode = $info.jeu }
    elseif ($g.Raw) { $gameNode = $g.Raw }

    if (-not $gameNode) { throw "Sem dados do jogo para extrair video." }

    $url = Extract-VideoUrl $gameNode $c
    if (-not $url -and $g.Id -and $sysId) {
      $media = if ($c.preferNormalizedVideo) { "video-normalized" } else { "video" }
      $url = "https://www.screenscraper.fr/medias/$sysId/$($g.Id)/$media.mp4"
      Log "A usar URL direta de media: $url"
    }
    if (-not $url) { throw "Este jogo nao tem video no ScreenScraper." }

    $platShort = ($cmbPlat.SelectedItem.ToString() -replace "[^A-Za-z0-9]+","_").Trim("_")
    $file = "$(Sanitize-FileName $platShort)_$(Sanitize-FileName $name)_$($g.Id).mp4"
    $out = Join-Path $c.outputDir $file

    if (Test-Path $out) {
      $ans = [System.Windows.Forms.MessageBox]::Show("Ja existe:`n$out`n`nSubstituir?", "Confirmar", "YesNo", "Question")
      if ($ans -ne "Yes") { return }
    }

    Log "A descarregar: $url"
    [System.Windows.Forms.Application]::DoEvents()
    Download-File $url $out
    Log "OK: $out ($([math]::Round((Get-Item $out).Length/1MB,2)) MB)"
    [System.Windows.Forms.MessageBox]::Show("Video guardado:`n$out", "Download OK")
  } catch {
    Log "ERRO: $($_.Exception.Message)"
    [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, "Erro", "OK", "Error")
  }
})

Log "Pronto. 1) Conta em screenscraper.fr  2) DevID no forum  3) Pesquisar e baixar."
Log "Destino padrao: $DefaultOut"

[void]$form.ShowDialog()
