param(
    [string]$SplashVideoPath = ""
)

$ErrorActionPreference = "Stop"

function Ok($m) { Write-Host "[OK] $m" -ForegroundColor Green }
function Info($m) { Write-Host "[INFO] $m" -ForegroundColor Cyan }
function Warn($m) { Write-Host "[AVISO] $m" -ForegroundColor Yellow }
function Err($m) { Write-Host "[ERRO] $m" -ForegroundColor Red }

function SaveLinesUtf8($path, [string[]]$lines) {
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllLines($path, $lines, $utf8NoBom)
}

try {
    Clear-Host
    Write-Host ""
    Write-Host "======================================================="
    Write-Host " TURBORAMA - FIX SplashVideo.cs erro <>9__2"
    Write-Host "======================================================="
    Write-Host ""

    $here = [System.IO.Path]::GetFullPath((Get-Location).Path)

    if ([string]::IsNullOrWhiteSpace($SplashVideoPath)) {
        $candidates = @(
            (Join-Path $here "SplashVideo.cs"),
            (Join-Path $here "RetroBat\SplashVideo.cs"),
            (Join-Path $here "TurboramaLauncher\RetroBat\SplashVideo.cs"),
            (Join-Path $here "TurboramaLauncher\SplashVideo.cs")
        )

        foreach ($c in $candidates) {
            if (Test-Path -LiteralPath $c) {
                $SplashVideoPath = $c
                break
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($SplashVideoPath) -or !(Test-Path -LiteralPath $SplashVideoPath)) {
        Write-Host "Nao encontrei SplashVideo.cs automaticamente."
        Write-Host ""
        Write-Host "Cole o caminho completo do SplashVideo.cs."
        Write-Host "Exemplo:"
        Write-Host "C:\Users\LZ\Documents\TURBORAMA BINARIOS EXE\RetroBat\SplashVideo.cs"
        Write-Host ""
        $SplashVideoPath = Read-Host "Caminho do SplashVideo.cs"
    }

    if ([string]::IsNullOrWhiteSpace($SplashVideoPath) -or !(Test-Path -LiteralPath $SplashVideoPath)) {
        throw "SplashVideo.cs nao encontrado."
    }

    $SplashVideoPath = [System.IO.Path]::GetFullPath($SplashVideoPath)

    Info "Arquivo: $SplashVideoPath"

    $fileInfo = Get-Item -LiteralPath $SplashVideoPath
    if ($fileInfo.Length -gt 2MB) {
        throw "Esse arquivo esta grande demais para ser SplashVideo.cs. Voce escolheu o arquivo errado."
    }

    Copy-Item -LiteralPath $SplashVideoPath -Destination ($SplashVideoPath + ".bak_showblack_invalido") -Force
    Ok "Backup criado."

    [string[]]$lines = [System.IO.File]::ReadAllLines($SplashVideoPath)

    $start = -1
    for ($i = 0; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match 'public\s+static\s+void\s+ShowBlackSplash\s*\(') {
            $start = $i
            break
        }
    }

    if ($start -lt 0) {
        throw "Nao encontrei o metodo ShowBlackSplash."
    }

    $end = -1
    for ($i = $start + 1; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match 'public\s+static\s+void\s+CloseBlackSplash\s*\(' -or
            $lines[$i] -match '^\s*// Token: 0x0600008A') {
            $end = $i
            break
        }
    }

    if ($end -lt 0) {
        throw "Nao encontrei o fim do metodo ShowBlackSplash. Use CORRECAO_MANUAL.txt."
    }

    $newMethod = @(
"`t`tpublic static void ShowBlackSplash(Screen targetScreen = null)",
"`t`t{",
"`t`t`tif (SplashVideo._blackSplashForm != null)",
"`t`t`t{",
"`t`t`t`treturn;",
"`t`t`t}",
"`t`t`tManualResetEvent splashDone = new ManualResetEvent(false);",
"`t`t`tThread thread = new Thread(delegate()",
"`t`t`t{",
"`t`t`t`tApplication.EnableVisualStyles();",
"`t`t`t`tApplication.SetCompatibleTextRenderingDefault(false);",
"`t`t`t`tScreen screen = targetScreen ?? Screen.PrimaryScreen;",
"`t`t`t`tSplashVideo._blackSplashForm = new Form",
"`t`t`t`t{",
"`t`t`t`t`tBackColor = Color.Black,",
"`t`t`t`t`tFormBorderStyle = FormBorderStyle.None,",
"`t`t`t`t`tStartPosition = FormStartPosition.Manual,",
"`t`t`t`t`tBounds = screen.Bounds,",
"`t`t`t`t`tTopMost = true,",
"`t`t`t`t`tShowInTaskbar = false",
"`t`t`t`t};",
"`t`t`t`tSplashVideo._blackSplashForm.Load += delegate(object s, EventArgs e)",
"`t`t`t`t{",
"`t`t`t`t`ttry",
"`t`t`t`t`t{",
"`t`t`t`t`t`tSplashVideo._blackSplashForm.Focus();",
"`t`t`t`t`t`tSplashVideo._blackSplashForm.Activate();",
"`t`t`t`t`t}",
"`t`t`t`t`tcatch",
"`t`t`t`t`t{",
"`t`t`t`t`t}",
"`t`t`t`t};",
"`t`t`t`tSplashVideo._blackSplashForm.Shown += delegate(object s, EventArgs e)",
"`t`t`t`t{",
"`t`t`t`t`tsplashDone.Set();",
"`t`t`t`t};",
"`t`t`t`tglobal::System.Windows.Forms.Timer watchdog = new global::System.Windows.Forms.Timer();",
"`t`t`t`twatchdog.Interval = 15000;",
"`t`t`t`twatchdog.Tick += delegate(object s, EventArgs e)",
"`t`t`t`t{",
"`t`t`t`t`ttry",
"`t`t`t`t`t{",
"`t`t`t`t`t`twatchdog.Stop();",
"`t`t`t`t`t`tForm blackSplashForm = SplashVideo._blackSplashForm;",
"`t`t`t`t`t`tif (blackSplashForm != null)",
"`t`t`t`t`t`t{",
"`t`t`t`t`t`t`tblackSplashForm.Close();",
"`t`t`t`t`t`t}",
"`t`t`t`t`t}",
"`t`t`t`t`tcatch",
"`t`t`t`t`t{",
"`t`t`t`t`t}",
"`t`t`t`t};",
"`t`t`t`twatchdog.Start();",
"`t`t`t`tApplication.Run(SplashVideo._blackSplashForm);",
"`t`t`t});",
"`t`t`tthread.SetApartmentState(ApartmentState.STA);",
"`t`t`tthread.Start();",
"`t`t`tsplashDone.WaitOne();",
"`t`t}",
""
    )

    $before = @()
    if ($start -gt 0) {
        $before = $lines[0..($start-1)]
    }

    $after = $lines[$end..($lines.Length-1)]

    [string[]]$final = @()
    $final += $before
    $final += $newMethod
    $final += $after

    SaveLinesUtf8 $SplashVideoPath $final

    Ok "ShowBlackSplash corrigido."
    Write-Host ""
    Write-Host "Agora compile novamente no Visual Studio:"
    Write-Host "Release | Any CPU"
    Write-Host ""
}
catch {
    Write-Host ""
    Err $_.Exception.Message
    Write-Host ""
    Write-Host "Use a correcao manual do arquivo CORRECAO_MANUAL.txt se precisar."
    Write-Host ""
}
finally {
    Write-Host "Pressione ENTER para fechar."
    Read-Host | Out-Null
}
