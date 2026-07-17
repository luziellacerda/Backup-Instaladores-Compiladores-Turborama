using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using TurboRama.Configuration;
using TurboRama.Core.Paths;

namespace TurboRama.Launcher;

/// <summary>
/// Menu de segurança TurboRama — substitui Ctrl+Alt+Del.
/// Visual alinhado com a tela de loading (PS3 / ondas / painel de vidro).
/// Atalho: Ctrl+End.
/// </summary>
internal sealed class SystemSecurityForm : Form
{
    private static readonly Color Black = Color.FromArgb(1, 3, 8);
    private static readonly Color DeepMid = Color.FromArgb(0, 20, 48);
    private static readonly Color Green = Color.FromArgb(0, 230, 100);
    private static readonly Color GreenMid = Color.FromArgb(0, 170, 70);
    private static readonly Color White = Color.FromArgb(245, 255, 250);
    private static readonly Color Red = Color.FromArgb(230, 40, 45);
    private static readonly Color Muted = Color.FromArgb(150, 175, 190);
    private static readonly Color Amber = Color.FromArgb(255, 210, 80);

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
    private readonly System.Windows.Forms.Timer _animTimer;
    private Image? _logo;
    private float _t;
    private readonly List<SecurityButton> _buttons = new();

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
        BackColor = Black;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);

        _logo = LoadLogo();

        int w = Math.Min(720, (int)(Bounds.Width * 0.55));
        int h = Math.Min(580, (int)(Bounds.Height * 0.68));
        _card = new Panel
        {
            Width = w,
            Height = h,
            Left = (Bounds.Width - w) / 2,
            Top = (Bounds.Height - h) / 2,
            BackColor = Color.Transparent
        };
        EnableDoubleBuffer(_card);
        _card.Paint += Card_Paint;
        Controls.Add(_card);

        // Layout interno do painel (coordenadas relativas ao card)
        int pad = 36;
        int y = 28;

        // Espaço para logo + títulos (desenhados no Paint do card)
        y = 118;

        var pinLabel = MakeLabel("PIN DE ACESSO TÉCNICO", pad, y, 9f, Muted, FontStyle.Bold);
        _card.Controls.Add(pinLabel);
        y += 22;

        _pinBox = new TextBox
        {
            Left = pad,
            Top = y,
            Width = Math.Min(300, w - pad * 2),
            Height = 36,
            Font = new Font("Segoe UI", 15f, FontStyle.Bold),
            UseSystemPasswordChar = true,
            BackColor = Color.FromArgb(8, 22, 34),
            ForeColor = White,
            BorderStyle = BorderStyle.FixedSingle,
            MaxLength = 32
        };
        _card.Controls.Add(_pinBox);
        y += 48;

        _status = new Label
        {
            Text = "Escolha uma opção  ·  Esc cancela",
            Left = pad,
            Top = y,
            Width = w - pad * 2,
            Height = 22,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Muted,
            BackColor = Color.Transparent
        };
        _card.Controls.Add(_status);
        y += 34;

        int gap = 12;
        int bw = (w - pad * 2 - gap) / 2;
        int bh = 48;

        AddBtn("CONTINUAR ARCADE", pad, y, bw, bh, SecurityAction.Resume, false,
            Color.FromArgb(0, 90, 48), Green, "Voltar ao jogo");
        AddBtn("ABRIR WINDOWS", pad + bw + gap, y, bw, bh, SecurityAction.OpenExplorer, true,
            Color.FromArgb(0, 55, 80), Color.FromArgb(60, 180, 220), "Explorer / desktop");
        y += bh + gap;

        AddBtn("TROCAR USUÁRIO", pad, y, w - pad * 2, bh, SecurityAction.SwitchUser, true,
            Color.FromArgb(28, 36, 72), Color.FromArgb(140, 160, 255), "Ecrã de login Windows");
        y += bh + gap;

        AddBtn("REINICIAR PC", pad, y, bw, bh, SecurityAction.Reboot, true,
            Color.FromArgb(70, 50, 8), Amber, "Reiniciar o sistema");
        AddBtn("DESLIGAR PC", pad + bw + gap, y, bw, bh, SecurityAction.Shutdown, true,
            Color.FromArgb(90, 20, 28), Red, "Desligar o PC");
        y += bh + gap;

        AddBtn("CANCELAR", pad, y, w - pad * 2, 42, SecurityAction.None, false,
            Color.FromArgb(22, 28, 36), Muted, "Fechar este menu");

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                ResultAction = SecurityAction.None;
                DialogResult = DialogResult.Cancel;
                Close();
            }
            else if (e.KeyCode == Keys.Enter && _pinBox.Focused)
            {
                // Enter no PIN não faz acção sozinho — evita desligar por engano
            }
        };

        _animTimer = new System.Windows.Forms.Timer { Interval = 40 };
        _animTimer.Tick += (_, _) =>
        {
            _t += 0.028f;
            Invalidate(false);
            _card.Invalidate();
        };

        Shown += (_, _) =>
        {
            TopMost = true;
            BringToFront();
            _animTimer.Start();
            _pinBox.Focus();
        };

        FormClosed += (_, _) =>
        {
            try { _animTimer.Stop(); } catch { /* ignore */ }
            try { _logo?.Dispose(); } catch { /* ignore */ }
            _logo = null;
        };
    }

    private void AddBtn(
        string text, int x, int y, int width, int height,
        SecurityAction action, bool needPin, Color bg, Color accent, string hint)
    {
        var btn = new SecurityButton
        {
            Text = text,
            Left = x,
            Top = y,
            Width = width,
            Height = height,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold),
            ForeColor = White,
            BackColor = bg,
            Cursor = Cursors.Hand,
            Tag = hint,
            Accent = accent,
            IsDanger = action == SecurityAction.Shutdown
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(
            Math.Min(255, bg.R + 18),
            Math.Min(255, bg.G + 18),
            Math.Min(255, bg.B + 18));
        btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(
            Math.Max(0, bg.R - 10),
            Math.Max(0, bg.G - 10),
            Math.Max(0, bg.B - 10));
        btn.Click += (_, _) => OnClick(action, needPin);
        btn.MouseEnter += (_, _) =>
        {
            if (!string.IsNullOrEmpty(hint) && _status.ForeColor != Red && _status.ForeColor != Amber)
            {
                _status.ForeColor = Muted;
                _status.Text = hint;
            }
        };
        _card.Controls.Add(btn);
        _buttons.Add(btn);
    }

    private static Label MakeLabel(string text, int x, int y, float size, Color color, FontStyle style)
    {
        return new Label
        {
            Text = text,
            Left = x,
            Top = y,
            AutoSize = true,
            Font = new Font("Segoe UI", size, style),
            ForeColor = color,
            BackColor = Color.Transparent
        };
    }

    private void Card_Paint(object? sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        int w = _card.Width;
        int h = _card.Height;
        var box = new Rectangle(0, 0, w - 1, h - 1);

        // sombra
        for (int i = 6; i >= 1; i--)
        {
            using var path = RoundRect(i, i + 2, w - 1, h - 1, 18);
            using var br = new SolidBrush(Color.FromArgb(10 + i * 6, 0, 0, 0));
            g.FillPath(br, path);
        }

        // vidro
        using (var path = RoundRect(0, 0, w - 1, h - 1, 18))
        using (var br = new LinearGradientBrush(box,
                   Color.FromArgb(210, 0, 28, 42),
                   Color.FromArgb(225, 0, 8, 16), 95f))
        {
            g.FillPath(br, path);
        }

        // brilho superior
        var hi = new Rectangle(12, 10, w - 24, h / 3);
        using (var path = RoundRect(hi.X, hi.Y, hi.Width, hi.Height, 14))
        using (var br = new LinearGradientBrush(hi,
                   Color.FromArgb(55, 180, 230, 255),
                   Color.FromArgb(0, 0, 0, 0), 90f))
        {
            g.FillPath(br, path);
        }

        // borda verde + filete vermelho topo (assinatura TurboRama)
        using (var path = RoundRect(1, 1, w - 3, h - 3, 17))
        using (var pen = new Pen(Color.FromArgb(180, Green), 1.8f))
        {
            g.DrawPath(pen, path);
        }

        using (var pen = new Pen(Red, 2.4f))
        {
            g.DrawLine(pen, 28, 4, w - 28, 4);
        }

        // Logo
        int logoH = 52;
        int logoX = 36;
        int logoY = 22;
        if (_logo != null)
        {
            float scale = logoH / (float)_logo.Height;
            int logoW = Math.Max(40, (int)(_logo.Width * scale));
            g.DrawImage(_logo, logoX, logoY, logoW, logoH);
            logoX += logoW + 16;
        }

        // Títulos
        using (var fTitle = new Font("Segoe UI", 22f, FontStyle.Bold))
        using (var fSub = new Font("Segoe UI", 10f, FontStyle.Bold))
        using (var fHint = new Font("Segoe UI", 8.5f))
        using (var brW = new SolidBrush(White))
        using (var brG = new SolidBrush(Green))
        using (var brM = new SolidBrush(Muted))
        {
            g.DrawString("TURBORAMA", fTitle, brW, logoX, logoY + 2);
            g.DrawString("SEGURANÇA DO SISTEMA", fSub, brG, logoX, logoY + 34);
            string hotkey = "Ctrl+Alt+Del desativado  ·  use Ctrl+End";
            g.DrawString(hotkey, fHint, brM, 36, 88);
        }

        // linha divisória suave
        using (var pen = new Pen(Color.FromArgb(60, Green), 1f))
        {
            g.DrawLine(pen, 36, 108, w - 36, 108);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // desenhado em OnPaint
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        int W = Math.Max(1, ClientSize.Width);
        int H = Math.Max(1, ClientSize.Height);

        using (var bg = new LinearGradientBrush(
                   new Rectangle(0, 0, W, H),
                   Black, DeepMid, 95f))
        {
            g.FillRectangle(bg, 0, 0, W, H);
        }

        using (var br = new LinearGradientBrush(
                   new Rectangle(0, 0, W, H / 2),
                   Color.FromArgb(45, 20, 80, 140),
                   Color.FromArgb(0, 0, 0, 0), 90f))
        {
            g.FillRectangle(br, 0, 0, W, H / 2);
        }

        DrawRealisticWaves(g, W, H);
        DrawLightShimmer(g, W, H);
        DrawVignette(g, W, H);

        // rodapé
        using var fFoot = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var brFoot = new SolidBrush(Color.FromArgb(140, 200, 220, 230));
        string foot = "TURBORAMA ARCADE  ·  MENU DE SEGURANÇA  ·  Ctrl+End";
        var sz = g.MeasureString(foot, fFoot);
        g.DrawString(foot, fFoot, brFoot, (W - sz.Width) / 2f, H - 36);
    }

    private void DrawRealisticWaves(Graphics g, int W, int H)
    {
        DrawWaveLayer(g, W, H,
            baseY: H * 0.28f, amp: H * 0.050f, freq: 1.25f, speed: 0.30f, phase: 0.2f,
            top: Color.FromArgb(32, 10, 60, 110), bot: Color.FromArgb(58, 0, 25, 55), samples: 40);
        DrawWaveLayer(g, W, H,
            baseY: H * 0.44f, amp: H * 0.070f, freq: 1.10f, speed: 0.40f, phase: 1.4f,
            top: Color.FromArgb(42, 15, 110, 170), bot: Color.FromArgb(72, 0, 35, 70), samples: 40);
        DrawWaveLayer(g, W, H,
            baseY: H * 0.58f, amp: H * 0.075f, freq: 1.05f, speed: 0.48f, phase: 2.1f,
            top: Color.FromArgb(48, 25, 160, 210), bot: Color.FromArgb(80, 0, 45, 85), samples: 44);
        DrawWaveLayer(g, W, H,
            baseY: H * 0.72f, amp: H * 0.055f, freq: 1.45f, speed: 0.56f, phase: 0.8f,
            top: Color.FromArgb(38, 70, 200, 230), bot: Color.FromArgb(65, 0, 55, 100), samples: 40);
    }

    private void DrawWaveLayer(
        Graphics g, int W, int H,
        float baseY, float amp, float freq, float speed, float phase,
        Color top, Color bot, int samples)
    {
        var crest = new PointF[samples + 1];
        for (int i = 0; i <= samples; i++)
        {
            float nx = i / (float)samples;
            float x = nx * W;
            float y = baseY
                      + amp * (float)Math.Sin(nx * Math.PI * 2 * freq + _t * speed + phase)
                      + amp * 0.42f * (float)Math.Sin(nx * Math.PI * 2 * freq * 1.65 + _t * speed * 1.25f + phase * 1.3f)
                      + amp * 0.18f * (float)Math.Sin(nx * Math.PI * 2 * freq * 0.55 + _t * speed * 0.7f + phase * 0.5f);
            crest[i] = new PointF(x, y);
        }

        using var path = new GraphicsPath();
        var poly = new PointF[samples + 3];
        Array.Copy(crest, poly, samples + 1);
        poly[samples + 1] = new PointF(W + 2, H + 4);
        poly[samples + 2] = new PointF(-2, H + 4);
        path.AddPolygon(poly);

        float topY = baseY - amp * 1.6f;
        float botY = H + 4;
        using var br = new LinearGradientBrush(
            new RectangleF(0, topY, W, Math.Max(8, botY - topY)),
            top, bot, 90f);
        g.FillPath(br, path);
    }

    private void DrawLightShimmer(Graphics g, int W, int H)
    {
        for (int i = 0; i < 4; i++)
        {
            float phase = _t * (0.25f + i * 0.05f) + i * 1.3f;
            float cx = W * (0.18f + 0.16f * i + 0.03f * (float)Math.Sin(phase));
            float cy = H * (0.38f + 0.07f * (float)Math.Cos(phase * 0.8f));
            float rw = W * (0.10f + 0.02f * (float)Math.Sin(phase * 1.2f));
            float rh = H * 0.12f;
            int a = 18 + (i % 2) * 6;
            using var br = new SolidBrush(Color.FromArgb(a, 100, 180, 230));
            g.FillEllipse(br, cx - rw, cy - rh, rw * 2, rh * 2);
        }
    }

    private static void DrawVignette(Graphics g, int W, int H)
    {
        int band = Math.Max(60, H / 8);
        using (var br = new LinearGradientBrush(new Rectangle(0, 0, W, band),
                   Color.FromArgb(190, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 90f))
        {
            g.FillRectangle(br, 0, 0, W, band);
        }

        using (var br = new LinearGradientBrush(new Rectangle(0, H - band, W, band),
                   Color.FromArgb(0, 0, 0, 0), Color.FromArgb(200, 0, 0, 0), 90f))
        {
            g.FillRectangle(br, 0, H - band, W, band);
        }

        int side = Math.Max(40, W / 18);
        using (var br = new LinearGradientBrush(new Rectangle(0, 0, side, H),
                   Color.FromArgb(100, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 0f))
        {
            g.FillRectangle(br, 0, 0, side, H);
        }

        using (var br = new LinearGradientBrush(new Rectangle(W - side, 0, side, H),
                   Color.FromArgb(0, 0, 0, 0), Color.FromArgb(100, 0, 0, 0), 0f))
        {
            g.FillRectangle(br, W - side, 0, side, H);
        }
    }

    private static GraphicsPath RoundRect(int x, int y, int w, int h, int r)
    {
        var path = new GraphicsPath();
        int d = r * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Image? LoadLogo()
    {
        try
        {
            string[] paths =
            {
                Path.Combine(AppContext.BaseDirectory, "Assets", "logo.png"),
                Path.Combine(ProductPaths.AppLauncher, "Assets", "logo.png"),
                Path.Combine(ProductPaths.AppLauncherAssets, "logo.png"),
            };
            foreach (string p in paths)
            {
                if (File.Exists(p))
                {
                    return Image.FromFile(p);
                }
            }
        }
        catch
        {
            // ignore
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
                _status.Text = "PIN incorreto — tente novamente.";
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
        catch
        {
            // ignore
        }
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
                catch
                {
                    // ignore
                }

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

    /// <summary>
    /// Vai para o ecrã de login para trocar de conta (ex.: Arcade → Admin).
    /// </summary>
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
            catch
            {
                // ignore
            }

            try
            {
                using var sysLm = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", true);
                sysLm?.SetValue("HideFastUserSwitching", 0, Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch
            {
                // pode falhar sem admin
            }

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
                catch
                {
                    // fallback logoff
                }
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
            catch
            {
                // ignore
            }

            ExitWindowsEx(EwxLogoff | EwxForce, 0);
        }
        catch
        {
            try
            {
                ExitWindowsEx(EwxLogoff | EwxForce, 0);
            }
            catch
            {
                // ignore
            }
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

    /// <summary>Botão com borda arredondada e filete de cor (estilo loading).</summary>
    private sealed class SecurityButton : Button
    {
        public Color Accent { get; set; } = Green;
        public bool IsDanger { get; set; }

        public SecurityButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            bool hover = ClientRectangle.Contains(PointToClient(Cursor.Position));
            Color fill = hover
                ? Color.FromArgb(
                    Math.Min(255, BackColor.R + 22),
                    Math.Min(255, BackColor.G + 22),
                    Math.Min(255, BackColor.B + 22))
                : BackColor;

            using (var path = RoundRect(0, 0, Width - 1, Height - 1, 10))
            using (var br = new LinearGradientBrush(rect,
                       Color.FromArgb(255, fill),
                       Color.FromArgb(255,
                           Math.Max(0, fill.R - 18),
                           Math.Max(0, fill.G - 18),
                           Math.Max(0, fill.B - 18)), 90f))
            {
                g.FillPath(br, path);
            }

            using (var path = RoundRect(1, 1, Width - 3, Height - 3, 9))
            using (var pen = new Pen(Color.FromArgb(hover ? 230 : 160, Accent), hover ? 2f : 1.4f))
            {
                g.DrawPath(pen, path);
            }

            // barra de destaque à esquerda
            using (var br = new SolidBrush(Color.FromArgb(220, Accent)))
            {
                g.FillRectangle(br, 0, 8, 4, Height - 16);
            }

            TextRenderer.DrawText(
                g,
                Text,
                Font,
                rect,
                ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
