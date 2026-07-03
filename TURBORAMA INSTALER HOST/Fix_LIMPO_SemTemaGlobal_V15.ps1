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

function FindMethodRange($text, $signature) {
    $start = $text.IndexOf($signature)
    if($start -lt 0) { return $null }

    $brace = $text.IndexOf("{", $start)
    if($brace -lt 0) { return $null }

    $depth = 0
    for($i = $brace; $i -lt $text.Length; $i++) {
        $ch = $text[$i]
        if($ch -eq "{") { $depth++ }
        elseif($ch -eq "}") {
            $depth--
            if($depth -eq 0) {
                return @{ Start = $start; End = $i + 1 }
            }
        }
    }

    return $null
}

function ReplaceMethodIfExists($text, $signature, $newMethod) {
    $range = FindMethodRange $text $signature
    if($range -eq $null) {
        return $text
    }

    return $text.Substring(0, $range.Start) + $newMethod.TrimEnd() + $text.Substring($range.End)
}

function InsertBeforeFinalClassBraces($text, $code) {
    $normalized = $text.TrimEnd()
    $idx = $normalized.LastIndexOf("}")
    if($idx -lt 0) { return $text }

    $withoutNs = $normalized.Substring(0, $idx).TrimEnd()
    $idx2 = $withoutNs.LastIndexOf("}")
    if($idx2 -lt 0) { return $text }

    return $withoutNs.Substring(0, $idx2).TrimEnd() + "`n" + $code.TrimEnd() + "`n`t}`n}"
}

function RemoveThemeTryBlocks($text) {
    $pattern = '\s*try\s*\{\s*(?:global::)?(?:InstallerHost\.)?(?:global::)?(?:InstallerHost\.)?(?:TurboramaPremiumUi|TurboramaPremiumTheme)\.(?:ApplyTheme|ApplyLicense|Apply)\(this\);\s*\}\s*catch\s*\{\s*\}'
    $text = [regex]::Replace($text, $pattern, "`n", [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

    $text = [regex]::Replace($text, '\s*(?:global::)?(?:InstallerHost\.)?(?:global::)?(?:InstallerHost\.)?(?:TurboramaPremiumUi|TurboramaPremiumTheme)\.(?:ApplyTheme|ApplyLicense|Apply)\(this\);\s*', "`n", [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

    while($text.Contains("global::global::")) { $text = $text.Replace("global::global::", "global::") }
    $text = $text.Replace("InstallerHost.global::InstallerHost.", "InstallerHost.")
    $text = $text.Replace("global::InstallerHost.global::InstallerHost.", "InstallerHost.")
    $text = $text.Replace("InstallerHost.InstallerHost.", "InstallerHost.")
    $text = $text.Replace("global::InstallerHost.global::", "global::")
    return $text
}

function AddHookAfterInitialize($text) {
    if($text.Contains("TurboramaEnsurePrerequisitesReadyV15();")) {
        return $text
    }

    $hook = "InitializeComponent();`n`t`t`tthis.TurboramaEnsurePrerequisitesReadyV15();`n`t`t`tthis.Load += delegate(object s, EventArgs e) { this.TurboramaEnsurePrerequisitesReadyV15(); };`n`t`t`tthis.VisibleChanged += delegate(object s, EventArgs e) { if (this.Visible) { this.TurboramaEnsurePrerequisitesReadyV15(); } };`n"
    return [regex]::Replace($text, 'InitializeComponent\(\);\s*', $hook, 1)
}

try {
    Clear-Host
    Write-Host "======================================================="
    Write-Host " TURBORAMA - FIX LIMPO SEM TEMA GLOBAL V15"
    Write-Host "======================================================="
    Write-Host ""

    if([string]::IsNullOrWhiteSpace($ProjectRoot)) {
        $ProjectRoot = Get-Location
    }

    $ProjectRoot = [System.IO.Path]::GetFullPath($ProjectRoot)
    Info "Pasta: $ProjectRoot"

    $csproj = Get-ChildItem -LiteralPath $ProjectRoot -Recurse -Filter "*.csproj" -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if($csproj -eq $null) {
        throw "Nao encontrei .csproj. Rode dentro da pasta do projeto InstallerHost."
    }

    $projectDir = $csproj.Directory.FullName
    Info "Projeto: $($csproj.FullName)"

    Warn "Feche o Visual Studio antes de aplicar."
    Write-Host "Pressione ENTER para corrigir."
    Read-Host | Out-Null

    Get-Process devenv -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-Process MSBuild -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-Process VBCSCompiler -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-Process InstallerHost -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

    $changed = 0

    foreach($src in Get-ChildItem -LiteralPath $projectDir -Recurse -Filter "*.cs" -File -ErrorAction SilentlyContinue) {
        if($src.Name -eq "TurboramaPremiumUi.cs" -or $src.Name -eq "TurboramaPremiumTheme.cs") {
            continue
        }

        $text = ReadText $src.FullName
        $orig = $text

        $text = RemoveThemeTryBlocks $text

        if($src.Name -eq "WizardPanel.cs" -and !$text.Contains("TurboramaPremiumUi") -and !$text.Contains("TurboramaPremiumTheme")) {
            $text = [regex]::Replace($text, '^\s*using\s+InstallerHost\s*;\s*', "", [System.Text.RegularExpressions.RegexOptions]::Multiline)
        }

        if($text -ne $orig) {
            Copy-Item -LiteralPath $src.FullName -Destination ($src.FullName + ".bak_LIMPO_V15") -Force
            SaveText $src.FullName $text
            $changed++
            Ok "Tema global removido: $($src.Name)"
        }
    }

    $projText = [System.IO.File]::ReadAllText($csproj.FullName)
    $origProj = $projText
    $projText = $projText.Replace('    <Compile Include="TurboramaPremiumTheme.cs" />' + "`r`n", "")
    $projText = $projText.Replace('    <Compile Include="TurboramaPremiumUi.cs" />' + "`r`n", "")
    $projText = $projText.Replace('<Compile Include="TurboramaPremiumTheme.cs" />', "")
    $projText = $projText.Replace('<Compile Include="TurboramaPremiumUi.cs" />', "")

    if($projText -ne $origProj) {
        Copy-Item -LiteralPath $csproj.FullName -Destination ($csproj.FullName + ".bak_LIMPO_V15") -Force
        SaveText $csproj.FullName $projText
        Ok "Helpers de tema removidos do csproj."
    }

    $prereq = Get-ChildItem -LiteralPath $projectDir -Recurse -Filter "PrerequisiteControl.cs" -File -ErrorAction SilentlyContinue |
        Where-Object {
            try {
                $c = [System.IO.File]::ReadAllText($_.FullName)
                return $c.Contains("class PrerequisiteControl")
            } catch { return $false }
        } |
        Select-Object -First 1

    if($prereq -ne $null) {
        Copy-Item -LiteralPath $prereq.FullName -Destination ($prereq.FullName + ".bak_LIMPO_V15") -Force
        $p = ReadText $prereq.FullName

        $p = RemoveThemeTryBlocks $p
        $p = AddHookAfterInitialize $p

        $newSkip = @'
		public bool SkipIfAllInstalled()
		{
			return false;
		}
'@
        $p = ReplaceMethodIfExists $p "public bool SkipIfAllInstalled()" $newSkip

        $newNext = @'
		private void BtnNext_Click(object sender, EventArgs e)
		{
			try
			{
				this.TurboramaEnsurePrerequisitesReadyV15();
				Logger.Log("Prerequisite step confirmed. Continuing to install screen.");
			}
			catch
			{
			}

			this.mainForm.ShowInstall();
		}
'@
        $p = ReplaceMethodIfExists $p "private void BtnNext_Click(object sender, EventArgs e)" $newNext

        $newUpdate = @'
		private void UpdatePrerequisiteCheckboxes()
		{
			this.TurboramaEnsurePrerequisitesReadyV15();
		}
'@
        $p = ReplaceMethodIfExists $p "private void UpdatePrerequisiteCheckboxes()" $newUpdate

        if(!$p.Contains("private void TurboramaEnsurePrerequisitesReadyV15()")) {
            $helpers = @'

		private void TurboramaEnsurePrerequisitesReadyV15()
		{
			try
			{
				this.TurboramaMarkChecksAndButtonsV15(this);

				Form form = this.FindForm();
				if (form != null)
				{
					this.TurboramaMarkChecksAndButtonsV15(form);
				}
			}
			catch
			{
			}
		}

		private void TurboramaMarkChecksAndButtonsV15(Control parent)
		{
			if (parent == null)
			{
				return;
			}

			foreach (Control control in parent.Controls)
			{
				CheckBox checkBox = control as CheckBox;
				if (checkBox != null)
				{
					string info = ((checkBox.Name ?? string.Empty) + " " + (checkBox.Text ?? string.Empty)).ToLowerInvariant();

					if (info.Contains("visual") || info.Contains("c++") || info.Contains("vc++") || info.Contains("vcredist") ||
						info.Contains("directx") || info.Contains("nvidia") || info.Contains("geforce") ||
						info.Contains("dokan") || info.Contains("winfsp"))
					{
						checkBox.Enabled = true;
						checkBox.ThreeState = false;
						checkBox.Checked = true;
						checkBox.CheckState = CheckState.Checked;

						if (checkBox.Visible)
						{
							checkBox.ForeColor = Color.White;
							checkBox.BackColor = Color.FromArgb(18, 24, 20);
						}

						if (info.Contains("nvidia") || info.Contains("geforce"))
						{
							this.chkNvidiaApp = checkBox;
						}
					}
				}

				Button button = control as Button;
				if (button != null)
				{
					string info = ((button.Name ?? string.Empty) + " " + (button.Text ?? string.Empty)).ToLowerInvariant();

					if (info.Contains("next") || info.Contains("back") || info.Contains("cancel") ||
						info.Contains("avancar") || info.Contains("avançar") || info.Contains("voltar") || info.Contains("cancelar"))
					{
						button.Visible = true;
						button.Enabled = true;
						button.BringToFront();
					}
				}

				if (control.HasChildren)
				{
					this.TurboramaMarkChecksAndButtonsV15(control);
				}
			}
		}
'@
            $p = InsertBeforeFinalClassBraces $p $helpers
        }

        SaveText $prereq.FullName $p
        Ok "PrerequisiteControl.cs corrigido sem tema global."
    }
    else {
        Warn "PrerequisiteControl.cs nao encontrado. Pulei essa parte."
    }

    foreach($folder in @(".vs","bin","obj")) {
        $target = Join-Path $projectDir $folder
        if(Test-Path $target) {
            Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction SilentlyContinue
            Ok "Removido: $target"
        }
    }

    Write-Host ""
    Write-Host "======================================================="
    Write-Host " V15 APLICADO"
    Write-Host " Arquivos alterados por limpeza de tema: $changed"
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
