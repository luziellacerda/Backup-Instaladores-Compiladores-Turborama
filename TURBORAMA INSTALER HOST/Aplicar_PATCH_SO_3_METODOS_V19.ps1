param([string]$ProjectRoot = "")

$ErrorActionPreference = "Stop"

function Ok($m){ Write-Host "[OK] $m" -ForegroundColor Green }
function Info($m){ Write-Host "[INFO] $m" -ForegroundColor Cyan }
function Warn($m){ Write-Host "[AVISO] $m" -ForegroundColor Yellow }
function Err($m){ Write-Host "[ERRO] $m" -ForegroundColor Red }

function ReadText($path) {
    return [System.IO.File]::ReadAllText($path).Replace("`r`n","`n").Replace("`r","`n")
}

function SaveText($path, $text) {
    $enc = New-Object System.Text.UTF8Encoding($false)
    $text = $text.Replace("`r`n","`n").Replace("`r","`n").Replace("`n","`r`n")
    [System.IO.File]::WriteAllText($path, $text, $enc)
}

function FindMethodRange($text, $signature) {
    $start = $text.IndexOf($signature)
    if ($start -lt 0) {
        throw "Metodo nao encontrado: $signature"
    }

    $brace = $text.IndexOf("{", $start)
    if ($brace -lt 0) {
        throw "Chave inicial nao encontrada em: $signature"
    }

    $depth = 0
    for ($i = $brace; $i -lt $text.Length; $i++) {
        $ch = $text[$i]
        if ($ch -eq "{") {
            $depth++
        }
        elseif ($ch -eq "}") {
            $depth--
            if ($depth -eq 0) {
                return @{ Start = $start; End = $i + 1 }
            }
        }
    }

    throw "Fim do metodo nao encontrado: $signature"
}

function ReplaceMethod($text, $signature, $replacement) {
    $range = FindMethodRange $text $signature
    return $text.Substring(0, $range.Start) + $replacement.TrimEnd() + $text.Substring($range.End)
}

try {
    Clear-Host
    Write-Host "======================================================="
    Write-Host " TURBORAMA - PATCH SO 3 METODOS V19"
    Write-Host "======================================================="
    Write-Host ""

    if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
        $ProjectRoot = Get-Location
    }

    $ProjectRoot = [System.IO.Path]::GetFullPath($ProjectRoot)
    Info "Pasta: $ProjectRoot"

    $file = Get-ChildItem -LiteralPath $ProjectRoot -Recurse -Filter "PrerequisiteControl.cs" -File -ErrorAction SilentlyContinue |
        Where-Object {
            try {
                $c = [System.IO.File]::ReadAllText($_.FullName)
                return $c.Contains("class PrerequisiteControl") -and
                       $c.Contains("private void BtnNext_Click(object sender, EventArgs e)") -and
                       $c.Contains("private void UpdatePrerequisiteCheckboxes()")
            }
            catch {
                return $false
            }
        } |
        Select-Object -First 1

    if ($file -eq $null) {
        throw "Nao encontrei PrerequisiteControl.cs correto. Rode este BAT dentro da pasta do projeto InstallerHost."
    }

    Info "Arquivo: $($file.FullName)"
    Warn "Este patch NAO mexe no Designer, NAO cria tema, NAO adiciona classe nova."
    Write-Host "Pressione ENTER para corrigir somente 3 metodos."
    Read-Host | Out-Null

    Get-Process devenv -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-Process MSBuild -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-Process VBCSCompiler -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-Process InstallerHost -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

    Copy-Item -LiteralPath $file.FullName -Destination ($file.FullName + ".bak_SO_3_METODOS_V19") -Force

    $text = ReadText $file.FullName

    # Remove chamadas quebradas antigas, mas somente linhas de tema/premium soltas.
    $text = [regex]::Replace($text, '\s*try\s*\{\s*(?:global::)?(?:InstallerHost\.)?(?:global::)?(?:InstallerHost\.)?(?:TurboramaPremiumUi|TurboramaPremiumTheme)\.(?:ApplyTheme|ApplyLicense|Apply)\(this\);\s*\}\s*catch\s*\{\s*\}', "`n", [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    $text = [regex]::Replace($text, '\s*(?:global::)?(?:InstallerHost\.)?(?:global::)?(?:InstallerHost\.)?(?:TurboramaPremiumUi|TurboramaPremiumTheme)\.(?:ApplyTheme|ApplyLicense|Apply)\(this\);\s*', "`n", [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

    $skip = @'
		public bool SkipIfAllInstalled()
		{
			return false;
		}
'@

    $next = @'
		private void BtnNext_Click(object sender, EventArgs e)
		{
			this.UpdatePrerequisiteCheckboxes();
			this.mainForm.ShowInstall();
		}
'@

    $update = @'
		private void UpdatePrerequisiteCheckboxes()
		{
			try
			{
				this.chkVCpp.Enabled = true;
				this.chkVCpp.Checked = true;
				this.chkVCpp.CheckState = CheckState.Checked;

				this.chkDirectX.Enabled = true;
				this.chkDirectX.Checked = true;
				this.chkDirectX.CheckState = CheckState.Checked;

				this.chkDokany.Enabled = true;
				this.chkDokany.Checked = true;
				this.chkDokany.CheckState = CheckState.Checked;

				this.chkwinFSP.Enabled = true;
				this.chkwinFSP.Checked = true;
				this.chkwinFSP.CheckState = CheckState.Checked;

				this.lblAllInstalled.Visible = false;
				this.statusLabel.Visible = false;
				this.progressBar.Visible = false;
				this.progressBar.Value = 0;
				this.progressBar.Maximum = 1;

				this.btnBack.Enabled = true;
				this.btnBack.Visible = true;

				this.btnNext.Enabled = true;
				this.btnNext.Visible = true;

				this.btnCancel.Enabled = true;
				this.btnCancel.Visible = true;

				this.btnBack.BringToFront();
				this.btnNext.BringToFront();
				this.btnCancel.BringToFront();
			}
			catch (Exception ex)
			{
				Logger.Log("Error setting prerequisite checkboxes: " + ex.Message);
			}
		}
'@

    $text = ReplaceMethod $text "public bool SkipIfAllInstalled()" $skip
    $text = ReplaceMethod $text "private void BtnNext_Click(object sender, EventArgs e)" $next
    $text = ReplaceMethod $text "private void UpdatePrerequisiteCheckboxes()" $update

    # Corrige erro real apontado no relatorio: Directory.Delete em arquivo ZIP.
    # Mesmo que os instaladores nao sejam chamados no fluxo novo, isso deixa o arquivo mais correto.
    $text = $text.Replace("Directory.Delete(text3, true);", "File.Delete(text3);")

    SaveText $file.FullName $text

    $projectDir = $file.Directory.FullName
    foreach ($folder in @(".vs","bin","obj")) {
        $target = Join-Path $projectDir $folder
        if (Test-Path $target) {
            Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction SilentlyContinue
            Ok "Removido: $target"
        }
    }

    Write-Host ""
    Write-Host "======================================================="
    Write-Host " V19 APLICADO"
    Write-Host " Alterado somente PrerequisiteControl.cs."
    Write-Host " Compile InstallerHost em Release."
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
