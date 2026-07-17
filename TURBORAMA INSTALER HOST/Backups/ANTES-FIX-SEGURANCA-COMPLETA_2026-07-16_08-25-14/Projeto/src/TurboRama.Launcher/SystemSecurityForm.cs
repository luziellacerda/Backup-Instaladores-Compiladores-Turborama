using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using TurboRama.Configuration;
using TurboRama.Core.Paths;

namespace TurboRama.Launcher;

/// <summary>
/// Menu de segurança profissional — limpo, estável, legível.
/// Fundo HUD opcional (Assets\security-hud-bg.png). Atalho: Ctrl+End.
/// </summary>
internal sealed class SystemSecurityForm : Form
{
    private static readonly Color Green = Color.FromArgb(0, 180, 90);
    private static readonly Color GreenSoft = Color.FromArgb(40, 200, 120);
    private static readonly Color GreenDark = Color.FromArgb(0, 55, 30);
    private static readonly Color BgFallback = Color.FromArgb(8, 12, 14);
    private static readonly Color CardBg = Color.FromArgb(235, 12, 18, 16);
    private static readonly Color CardBorder = Color.FromArgb(180, 0, 170, 85);
    private static readonly Color White = Color.FromArgb(245, 250, 248);
    private static readonly Color Muted = Color.FromArgb(150, 170, 160);
    private static readonly Color Red = Color.FromArgb(200, 55, 55);
    private static readonly Color Amber = Color.FromArgb(220, 170, 50);
    private static readonly Color BtnBg = Color.FromArgb(18, 28, 24);
    private static readonly Color BtnBgHover = Color.FromArgb(28, 48, 38);
    private static readonly Color BtnBorder = Color.FromArgb(0, 150, 75);

    public enum SecurityAction
    {
        None,
        Resume,
        OpenExplorer,
        SwitchUser,
        Reboot,
        Shutdown,
    }

    public SecurityAction ResultAction { get; private set; } = SecurityAction.None;

    private readonly string _pin;
    private readonly TextBox _pinBox;
    private readonly Label _status;
    private readonly Panel _card;
    private Image? _bg;
    private Image? _logo;

    public SystemSecurityForm(string pin)
    {
        _pin = pin ?? "";

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        TopMost = true;
        ShowInTaskbar = false;
        KeyPreview = true;
        DoubleBuffered = true;
        BackColor = BgFallback;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);

        _bg = LoadHudBackground();
        _logo = LoadLogo();

        int cardW = Math.Min(640, (int)(Bounds.Width * 0.48));
        int cardH = Math.Min(560, (int)(Bounds.Height * 0.68));
        _card = new Panel
        {
            Width = cardW,
            Height = cardH,
            Left = (Bounds.Width - cardW) / 2,
            Top = (Bounds.Height - cardH) / 2,
            BackColor = Color.Transparent
        };
        EnableDoubleBuffer(_card);
        _card.Paint += Card_Paint;
        Controls.Add(_card);

        int pad = 36;
        int y = 108;
        int innerW = cardW - pad * 2;

        // PIN label
        _card.Controls.Add(new Label
        {
            Text = "PIN de acesso técnico",
            Left = pad,
            Top = y,
            AutoSize = true,
            Font = new Font("Segoe UI", 9f, FontStyle.Regular),
            ForeColor = Muted,
            BackColor = Color.Transparent
        });
        y += 24;

        _pinBox = new TextBox
        {
            Left = pad,
            Top = y,
            Width = Math.Min(280, innerW),
            Height = 34,
            Font = new Font("Segoe UI", 13f),
            UseSystemPasswordChar = true,
            BackColor = Color.FromArgb(10, 16, 14),
            ForeColor = White,
            BorderStyle = BorderStyle.FixedSingle,
            MaxLength = 32
        };
        _card.Controls.Add(_pinBox);
        y += 48;

        _status = new Label
        {
            Text = "Escolha uma opção. Esc cancela.",
            Left = pad,
            Top = y,
            Width = innerW,
            Height = 22,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Muted,
            BackColor = Color.Transparent
        };
        _card.Controls.Add(_status);
        y += 36;

        int gap = 12;
        int bw = (innerW - gap) / 2;
        int bh = 44;

        // Acções principais
        AddButton("Continuar arcade", pad, y, bw, bh, SecurityAction.Resume, false,
            Green, "Voltar ao jogo", primary: true);
        AddButton("Abrir Windows", pad + bw + gap, y, bw, bh, SecurityAction.OpenExplorer, true,
            BtnBorder, "Abrir o Explorador de ficheiros");
        y += bh + gap;

        AddButton("Reiniciar PC", pad, y, bw, bh, SecurityAction.Reboot, true,
            Amber, "Reiniciar o computador");
        AddButton("Desligar PC", pad + bw + gap, y, bw, bh, SecurityAction.Shutdown, true,
            Red, "Desligar o computador", danger: true);
        y += bh + gap;

        AddButton("Cancelar", pad, y, innerW, 40, SecurityAction.None, false,
            Muted, "Fechar este menu", ghost: true);
        y += 40 + gap + 8;

        // Último
        AddButton("Trocar usuário", pad, y, innerW, bh, SecurityAction.SwitchUser, true,
            GreenSoft, "Ir para o ecrã de login do Windows");

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                ResultAction = SecurityAction.None;
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };

        Shown += (_, _) =>
        {
            TopMost = true;
            BringToFront();
            _pinBox.Focus();
        };

        FormClosed += (_, _) =>
        {
            try { _bg?.Dispose(); } catch { /* ignore */ }
            try { _logo?.Dispose(); } catch { /* ignore */ }
            _bg = null;
            _logo = null;
        };
    }

    private void AddButton(
        string text, int x, int y, int width, int height,
        SecurityAction action, bool needPin, Color accent, string hint,
        bool primary = false, bool danger = false, bool ghost = false)
    {
        var btn = new ProfessionalButton
        {
            Text = text,
            Left = x,
            Top = y,
            Width = width,
            Height = height,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f, primary ? FontStyle.Bold : FontStyle.Regular),
            ForeColor = White,
            Cursor = Cursors.Hand,
            Accent = accent,
            IsPrimary = primary,
            IsDanger = danger,
            IsGhost = ghost
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.Click += (_, _) => OnClick(action, needPin);
        btn.MouseEnter += (_, _) =>
        {
            if (_status.ForeColor != Red && _status.ForeColor != Amber)
            {
                _status.ForeColor = Muted;
                _status.Text = hint;
            }
        };
        btn.MouseLeave += (_, _) =>
        {
            if (_status.ForeColor != Red && _status.ForeColor != Amber)
            {
                _status.Text = "Escolha uma opção. Esc cancela.";
            }
        };
        _card.Controls.Add(btn);
    }

    private void Card_Paint(object? sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        int w = _card.Width;
        int h = _card.Height;

        // sombra discreta
        using (var path = RoundRect(6, 8, w - 6, h - 6, 12))
        using (var br = new SolidBrush(Color.FromArgb(100, 0, 0, 0)))
        {
            g.FillPath(br, path);
        }

        // cartão
        using (var path = RoundRect(0, 0, w - 1, h - 1, 12))
        using (var br = new SolidBrush(CardBg))
        {
            g.FillPath(br, path);
        }

        using (var path = RoundRect(0, 0, w - 1, h - 1, 12))
        using (var pen = new Pen(CardBorder, 1.5f))
        {
            g.DrawPath(pen, path);
        }

        // linha superior de acento
        using (var pen = new Pen(Green, 3f))
        {
            g.DrawLine(pen, 24, 2, w - 24, 2);
        }

        // logo + título
        int lx = 32;
        int ly = 22;
        if (_logo != null)
        {
            int lh = 40;
            float sc = lh / (float)_logo.Height;
            int lw = Math.Max(32, (int)(_logo.Width * sc));
            g.DrawImage(_logo, lx, ly, lw, lh);
            lx += lw + 14;
        }

        using (var fTitle = new Font("Segoe UI", 18f, FontStyle.Bold))
        using (var fSub = new Font("Segoe UI", 9.5f))
        using (var brW = new SolidBrush(White))
        using (var brG = new SolidBrush(GreenSoft))
        using (var brM = new SolidBrush(Muted))
        {
            g.DrawString("TURBORAMA", fTitle, brW, lx, ly);
            g.DrawString("Segurança do sistema", fSub, brG, lx, ly + 28);
            g.DrawString("Ctrl+Alt+Del desativado  ·  use Ctrl+End", fSub, brM, 32, 72);
        }

        // divisor
        using (var pen = new Pen(Color.FromArgb(50, Green), 1f))
        {
            g.DrawLine(pen, 32, 96, w - 32, 96);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.None;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        int W = Math.Max(1, ClientSize.Width);
        int H = Math.Max(1, ClientSize.Height);

        if (_bg != null)
        {
            DrawCover(g, _bg, W, H);
            // escurecer levemente para legibilidade
            using var dim = new SolidBrush(Color.FromArgb(90, 0, 0, 0));
            g.FillRectangle(dim, 0, 0, W, H);
        }
        else
        {
            using var br = new SolidBrush(BgFallback);
            g.FillRectangle(br, 0, 0, W, H);
        }

        // rodapé discreto
        using var f = new Font("Segoe UI", 8.5f);
        using var brF = new SolidBrush(Color.FromArgb(140, Muted));
        string foot = "TurboRama Arcade  ·  Menu de segurança";
        var sz = g.MeasureString(foot, f);
        g.DrawString(foot, f, brF, (W - sz.Width) / 2f, H - 28);
    }

    private static void DrawCover(Graphics g, Image img, int W, int H)
    {
        float scale = Math.Max(W / (float)img.Width, H / (float)img.Height);
        int dw = (int)(img.Width * scale);
        int dh = (int)(img.Height * scale);
        g.DrawImage(img, (W - dw) / 2, (H - dh) / 2, dw, dh);
    }

    private static GraphicsPath RoundRect(int x, int y, int w, int h, int r)
    {
        int d = r * 2;
        var p = new GraphicsPath();
        p.AddArc(x, y, d, d, 180, 90);
        p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        p.AddArc(x, y + h - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static Image? LoadHudBackground()
    {
        string[] paths =
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "security-hud-bg.png"),
            Path.Combine(ProductPaths.AppLauncherAssets, "security-hud-bg.png"),
            Path.Combine(ProductPaths.AppLauncher, "Assets", "security-hud-bg.png"),
        };
        foreach (string p in paths)
        {
            try
            {
                if (!File.Exists(p)) continue;
                byte[] bytes = File.ReadAllBytes(p);
                using var ms = new MemoryStream(bytes);
                return Image.FromStream(ms);
            }
            catch { /* next */ }
        }

        return null;
    }

    private static Image? LoadLogo()
    {
        string[] paths =
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "logo.png"),
            Path.Combine(ProductPaths.AppLauncherAssets, "logo.png"),
            Path.Combine(ProductPaths.AppLauncher, "Assets", "logo.png"),
        };
        foreach (string p in paths)
        {
            try
            {
                if (File.Exists(p)) return Image.FromFile(p);
            }
            catch { /* ignore */ }
        }

        return null;
    }

    private void OnClick(SecurityAction action, bool needPin)
    {
        if (action == SecurityAction.None)
        {
            ResultAction = SecurityAction.None;
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        if (action == SecurityAction.Resume)
        {
            ResultAction = SecurityAction.Resume;
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        if (needPin)
        {
            if (string.IsNullOrEmpty(_pin))
            {
                _status.ForeColor = Amber;
                _status.Text = "PIN não configurado.";
                return;
            }

            if (!string.Equals(_pinBox.Text, _pin, StringComparison.Ordinal))
            {
                _status.ForeColor = Red;
                _status.Text = "PIN incorreto. Tente novamente.";
                _pinBox.Clear();
                _pinBox.Focus();
                return;
            }
        }

        ResultAction = action;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static void EnableDoubleBuffer(Control c)
    {
        try
        {
            typeof(Control).InvokeMember("DoubleBuffered",
                BindingFlags.SetProperty | BindingFlags.Instance | BindingFlags.NonPublic,
                null, c, new object[] { true });
        }
        catch { /* ignore */ }
    }

    public static void RunAction(SecurityAction action)
    {
        switch (action)
        {
            case SecurityAction.OpenExplorer:
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = "explorer.exe", UseShellExecute = true });
                }
                catch { /* ignore */ }
                break;
            case SecurityAction.SwitchUser:
                SwitchUserNow();
                break;
            case SecurityAction.Reboot:
                PowerShutdownHelper.RebootNow(out _, null);
                break;
            case SecurityAction.Shutdown:
                PowerShutdownHelper.ShutdownNow(out _, null);
                break;
        }
    }

    public static void SwitchUserNow()
    {
        try
        {
            try
            {
                using var sys = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Policies\System", true);
                sys?.SetValue("HideFastUserSwitching", 0, Microsoft.Win32.RegistryValueKind.DWord);
                using var exp = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", true);
                exp?.SetValue("NoLogoff", 0, Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch { /* ignore */ }

            try
            {
                using var sysLm = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", true);
                sysLm?.SetValue("HideFastUserSwitching", 0, Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch { /* ignore */ }

            string tsdiscon = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "tsdiscon.exe");
            if (File.Exists(tsdiscon))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = tsdiscon,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    return;
                }
                catch { /* fallback */ }
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "shutdown.exe",
                    Arguments = "/l /f",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                return;
            }
            catch { /* ignore */ }

            ExitWindowsEx(EwxLogoff | EwxForce, 0);
        }
        catch
        {
            try { ExitWindowsEx(EwxLogoff | EwxForce, 0); } catch { /* ignore */ }
        }
    }

    private const uint EwxLogoff = 0x00000000;
    private const uint EwxForce = 0x00000004;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

    public static string ResolvePin(ProductConfiguration config)
    {
        if (!string.IsNullOrWhiteSpace(config.SecurityMenuPin))
        {
            return config.SecurityMenuPin.Trim();
        }

        return FactoryDefaults.ResolveKioskPassword(config);
    }

    /// <summary>Botão estático, hover só muda cor — sem animação contínua.</summary>
    private sealed class ProfessionalButton : Button
    {
        public Color Accent { get; set; } = Green;
        public bool IsPrimary { get; set; }
        public bool IsDanger { get; set; }
        public bool IsGhost { get; set; }

        public ProfessionalButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            bool hover = ClientRectangle.Contains(PointToClient(Cursor.Position));
            bool down = hover && (MouseButtons & MouseButtons.Left) != 0;

            Color fill;
            if (IsGhost)
            {
                fill = hover ? Color.FromArgb(40, 30, 40, 35) : Color.FromArgb(20, 20, 28, 24);
            }
            else if (IsPrimary)
            {
                fill = hover ? Color.FromArgb(255, 12, 55, 32) : Color.FromArgb(255, 8, 40, 24);
            }
            else if (IsDanger)
            {
                fill = hover ? Color.FromArgb(255, 48, 22, 22) : BtnBg;
            }
            else
            {
                fill = hover ? BtnBgHover : BtnBg;
            }

            if (down)
            {
                fill = Color.FromArgb(
                    fill.A,
                    Math.Max(0, fill.R - 12),
                    Math.Max(0, fill.G - 12),
                    Math.Max(0, fill.B - 12));
            }

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = RoundRect(0, 0, Width - 1, Height - 1, 8))
            using (var br = new SolidBrush(fill))
            {
                g.FillPath(br, path);
            }

            Color border = IsGhost
                ? Color.FromArgb(hover ? 160 : 90, Accent)
                : Color.FromArgb(hover ? 255 : 200, Accent);

            using (var path = RoundRect(0, 0, Width - 1, Height - 1, 8))
            using (var pen = new Pen(border, hover ? 1.8f : 1.2f))
            {
                g.DrawPath(pen, path);
            }

            // acento esquerdo fino
            if (!IsGhost)
            {
                using var br = new SolidBrush(Accent);
                g.FillRectangle(br, 0, 8, 3, Height - 16);
            }

            Color textCol = IsGhost && !hover ? Muted : White;
            TextRenderer.DrawText(
                g, Text, Font, rect, textCol,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis);
        }
    }
}
