using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using TurboRama.Configuration;
using TurboRama.Core.Paths;

namespace TurboRama.Launcher;

/// <summary>
/// Loading TURBORAMA: fundo PRETO + hiperespaço estrelas BRANCAS brilhantes.
/// UI card: branco / verde / vermelho. Nunca trava em 100% (saída por relógio + stop timer).
/// </summary>
internal sealed class LoadingScreenForm : Form
{
    private static readonly Color Black = Color.FromArgb(0, 0, 0);
    private static readonly Color Green = Color.FromArgb(0, 230, 90);
    private static readonly Color GreenMid = Color.FromArgb(0, 150, 55);
    private static readonly Color GreenDark = Color.FromArgb(0, 60, 24);
    private static readonly Color White = Color.FromArgb(255, 255, 255);
    private static readonly Color WhiteSoft = Color.FromArgb(220, 225, 220);
    private static readonly Color WhiteDim = Color.FromArgb(150, 155, 150);
    private static readonly Color Red = Color.FromArgb(220, 28, 36);
    private static readonly Color RedHot = Color.FromArgb(255, 50, 50);

    private readonly System.Windows.Forms.Timer _animTimer;
    private Image? _logo;
    private int _progress;
    private float _t;
    private string _status = "Inicializando...";
    private string _phase = "BOOT";
    private readonly Stopwatch _sw = new();
    private int _holdMs = 5000;

    private readonly Star[] _stars;
    private readonly Random _rng = new(7);

    private struct Star
    {
        public float X;
        public float Y;
        public float Z;
        public float Speed;
        public float Bright; // 0.5..1
    }

    public LoadingScreenForm(ProductConfiguration config)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        WindowState = FormWindowState.Normal;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        DoubleBuffered = true;
        BackColor = Black;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        UpdateStyles();

        Bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        _logo = LoadLogoImage();
        _stars = new Star[160];
        for (int i = 0; i < _stars.Length; i++)
        {
            _stars[i] = SpawnStar(true);
        }

        // Só anima estrelas — o hold NÃO depende disto para terminar
        _animTimer = new System.Windows.Forms.Timer { Interval = 33 };
        _animTimer.Tick += (_, _) =>
        {
            _t += 0.033f;
            UpdateStars();
            Invalidate();
        };

        Shown += (_, _) => { ForceForeground(); Invalidate(); };
    }

    private Star SpawnStar(bool far)
    {
        double ang = _rng.NextDouble() * Math.PI * 2;
        double rad = 0.08 + _rng.NextDouble() * 1.35;
        return new Star
        {
            X = (float)(Math.Cos(ang) * rad),
            Y = (float)(Math.Sin(ang) * rad * 0.72),
            Z = far ? (float)(0.25 + _rng.NextDouble() * 0.75) : (float)(0.9 + _rng.NextDouble() * 0.1),
            Speed = (float)(0.014 + _rng.NextDouble() * 0.032),
            Bright = (float)(0.55 + _rng.NextDouble() * 0.45)
        };
    }

    private void UpdateStars()
    {
        for (int i = 0; i < _stars.Length; i++)
        {
            ref Star s = ref _stars[i];
            s.Z -= s.Speed * (1.15f + (1f - s.Z) * 2.2f);
            if (s.Z <= 0.035f)
            {
                _stars[i] = SpawnStar(true);
            }
        }
    }

    /// <summary>
    /// Mostra loading e SEMPRE sai após minMs. Depois o Program fecha a form e abre o jogo.
    /// </summary>
    public void ShowBrandHold(int minMs, Action<int, string>? onTick = null)
    {
        if (minMs < 3500)
        {
            minMs = 3500;
        }

        if (minMs > 15000)
        {
            minMs = 15000;
        }

        _holdMs = minMs;
        _progress = 0;
        _status = "Entrando na galáxia...";
        _phase = "BOOT";

        Bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        Show();
        ForceForeground();
        _sw.Restart();
        _animTimer.Start();

        // Loop ÚNICO controlado pelo relógio — saída garantida
        while (true)
        {
            long elapsed = _sw.ElapsedMilliseconds;
            float t = Math.Min(1f, elapsed / (float)_holdMs);

            // 0 → 100 linear no tempo (chega a 100 no fim, sem ficar preso)
            _progress = (int)Math.Round(t * 100f);
            if (_progress > 100)
            {
                _progress = 100;
            }

            ApplyStatus(t);
            UpdateStars();
            onTick?.Invoke(_progress, _status);
            ForceForeground();
            Invalidate();
            Application.DoEvents();

            if (elapsed >= _holdMs)
            {
                _progress = 100;
                _status = "GO";
                _phase = "GO";
                Invalidate();
                Application.DoEvents();
                break;
            }

            Thread.Sleep(16);
        }

        // CRÍTICO: parar animação para não manter a form “viva” em 100%
        try
        {
            _animTimer.Stop();
        }
        catch
        {
        }

        Thread.Sleep(120);
    }

    private void ApplyStatus(float t)
    {
        if (t < 0.2f)
        {
            _phase = "BOOT";
            _status = "Entrando na galáxia...";
        }
        else if (t < 0.4f)
        {
            _phase = "CORE";
            _status = "Hiperespaço ativo...";
        }
        else if (t < 0.6f)
        {
            _phase = "LOAD";
            _status = "A carregar TURBORAMA...";
        }
        else if (t < 0.8f)
        {
            _phase = "SYNC";
            _status = "Sistemas prontos...";
        }
        else if (t < 0.97f)
        {
            _phase = "READY";
            _status = "Saída do hiperespaço...";
        }
        else
        {
            _phase = "GO";
            _status = "GO";
        }
    }

    public void SetStatus(string text)
    {
        if (IsDisposed)
        {
            return;
        }

        _status = text ?? "";
        Invalidate();
        Application.DoEvents();
    }

    public void SetProgress(int value)
    {
        _progress = Math.Clamp(value, 0, 100);
        Invalidate();
        Application.DoEvents();
    }

    public void HideLoading()
    {
        try
        {
            _animTimer.Stop();
            if (Visible)
            {
                Hide();
            }

            Application.DoEvents();
        }
        catch
        {
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        try
        {
            PaintScene(e.Graphics, ClientRectangle);
        }
        catch
        {
            try
            {
                e.Graphics.Clear(Black);
                using var f = new Font("Segoe UI", 36f, FontStyle.Bold);
                using var b = new SolidBrush(White);
                e.Graphics.DrawString("TURBORAMA", f, b, 40, 40);
                using var f2 = new Font("Segoe UI", 16f);
                using var b2 = new SolidBrush(Green);
                e.Graphics.DrawString(_progress + "%", f2, b2, 40, 95);
            }
            catch
            {
            }
        }
    }

    private void PaintScene(Graphics g, Rectangle r)
    {
        if (r.Width < 20 || r.Height < 20)
        {
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        float pulse = 0.55f + 0.45f * (float)Math.Sin(_t * 2.5);

        // FUNDO 100% PRETO
        g.Clear(Black);

        // Estrelas brancas brilhantes (hiperespaço)
        DrawWhiteHyperspace(g, r);

        // Vinheta preta suave (profundidade)
        DrawVignette(g, r);

        // Card UI (verde/branco/vermelho sobre o espaço)
        int cardW = Math.Min(940, (int)(r.Width * 0.76));
        int cardH = Math.Min(440, (int)(r.Height * 0.52));
        var card = new Rectangle((r.Width - cardW) / 2, (r.Height - cardH) / 2, cardW, cardH);
        DrawCard(g, card, pulse);

        int pad = 42;
        int x = card.X + pad;
        int y = card.Y + pad;
        int w = card.Width - pad * 2;

        DrawBadge(g, new Rectangle(x, y, 100, 100), pulse);
        DrawTitle(g, x + 120, y + 8, pulse);

        using (var f = new Font("Segoe UI", 13f, FontStyle.Bold))
        using (var b = new SolidBrush(Green))
        {
            g.DrawString("ARCADE", f, b, x + 120, y + 58);
        }

        using (var pen = new Pen(RedHot, 3f))
        {
            g.DrawLine(pen, x + 120, y + 90, x + 260, y + 90);
        }

        string[] phases = { "BOOT", "CORE", "LOAD", "SYNC", "READY", "GO" };
        int chipY = y + 130;
        int chipW = Math.Max(68, (w - 40) / phases.Length - 8);
        int active = Array.IndexOf(phases, _phase);
        if (active < 0)
        {
            active = Math.Min(phases.Length - 1, _progress / 20);
        }

        for (int i = 0; i < phases.Length; i++)
        {
            DrawChip(g, new Rectangle(x + i * (chipW + 8), chipY, chipW, 26),
                phases[i], i <= active, i == active, pulse);
        }

        using (var f = new Font("Segoe UI", 12.5f))
        using (var b = new SolidBrush(WhiteSoft))
        {
            g.DrawString(_status, f, b, x, chipY + 44);
        }

        string pct = _progress.ToString("00") + "%";
        using (var f = new Font("Consolas", 24f, FontStyle.Bold))
        {
            SizeF sz = g.MeasureString(pct, f);
            using var b = new SolidBrush(Green);
            g.DrawString(pct, f, b, x + w - sz.Width, chipY + 36);
        }

        DrawBar(g, new Rectangle(x, chipY + 84, w, 16), _progress / 100f, pulse);

        using (var f = new Font("Segoe UI", 9.5f))
        using (var b = new SolidBrush(WhiteDim))
        {
            g.DrawString("CONSOLE ARCADE  ·  GALAXY BOOT", f, b, x, card.Bottom - 36);
        }

        DrawBrackets(g, card, pulse);
    }

    /// <summary>Só branco brilhante — sem traços coloridos.</summary>
    private void DrawWhiteHyperspace(Graphics g, Rectangle r)
    {
        float cx = r.Width * 0.5f;
        float cy = r.Height * 0.5f;
        float scale = Math.Min(r.Width, r.Height) * 0.62f;

        for (int i = 0; i < _stars.Length; i++)
        {
            ref readonly Star s = ref _stars[i];
            if (s.Z < 0.03f)
            {
                continue;
            }

            float inv = 1f / s.Z;
            float sx = cx + s.X * scale * inv;
            float sy = cy + s.Y * scale * inv;

            float z2 = Math.Min(1f, s.Z + s.Speed * 5.5f);
            float inv2 = 1f / z2;
            float px = cx + s.X * scale * inv2;
            float py = cy + s.Y * scale * inv2;

            if (sx < -40 || sy < -40 || sx > r.Width + 40 || sy > r.Height + 40)
            {
                continue;
            }

            float near = 1f - s.Z;
            int a = (int)((90 + 165 * near) * s.Bright);
            a = Math.Clamp(a, 40, 255);
            float thick = 0.7f + near * 3.2f * s.Bright;

            // rastro branco
            using (var pen = new Pen(Color.FromArgb(a, 255, 255, 255), thick))
            {
                g.DrawLine(pen, px, py, sx, sy);
            }

            // núcleo brilhante
            float core = 0.7f + near * 2.8f * s.Bright;
            using (var br = new SolidBrush(Color.FromArgb(Math.Min(255, a + 50), 255, 255, 255)))
            {
                g.FillEllipse(br, sx - core, sy - core, core * 2, core * 2);
            }

            // brilho extra nas mais próximas
            if (near > 0.55f)
            {
                float glow = core * 2.2f;
                using var br = new SolidBrush(Color.FromArgb(40, 255, 255, 255));
                g.FillEllipse(br, sx - glow, sy - glow, glow * 2, glow * 2);
            }
        }
    }

    private static void DrawVignette(Graphics g, Rectangle r)
    {
        int band = Math.Max(50, r.Height / 8);
        using (var br = new LinearGradientBrush(new Rectangle(0, 0, r.Width, band),
                   Color.FromArgb(220, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 90f))
        {
            g.FillRectangle(br, 0, 0, r.Width, band);
        }

        using (var br = new LinearGradientBrush(new Rectangle(0, r.Height - band, r.Width, band),
                   Color.FromArgb(0, 0, 0, 0), Color.FromArgb(230, 0, 0, 0), 90f))
        {
            g.FillRectangle(br, 0, r.Height - band, r.Width, band);
        }
    }

    private static void DrawCard(Graphics g, Rectangle card, float pulse)
    {
        for (int i = 5; i >= 1; i--)
        {
            var sh = card;
            sh.Offset(0, i + 2);
            using var path = RoundRect(sh, 12);
            using var br = new SolidBrush(Color.FromArgb(14 + i * 6, 0, 0, 0));
            g.FillPath(br, path);
        }

        using (var path = RoundRect(card, 12))
        using (var br = new LinearGradientBrush(card,
                   Color.FromArgb(248, 12, 14, 12),
                   Color.FromArgb(248, 4, 6, 4), 95f))
        {
            g.FillPath(br, path);
        }

        using (var path = RoundRect(card, 12))
        using (var pen = new Pen(Color.FromArgb((int)(150 + 70 * pulse), Green), 1.8f))
        {
            g.DrawPath(pen, path);
        }

        using (var pen = new Pen(Color.FromArgb((int)(170 + 50 * pulse), RedHot), 2.5f))
        {
            g.DrawLine(pen, card.X + 32, card.Y + 2, card.Right - 32, card.Y + 2);
        }
    }

    private void DrawBadge(Graphics g, Rectangle rect, float pulse)
    {
        using (var path = RoundRect(rect, 10))
        using (var br = new LinearGradientBrush(rect, GreenDark, Black, 45f))
        {
            g.FillPath(br, path);
        }

        using (var path = RoundRect(rect, 10))
        using (var pen = new Pen(Color.FromArgb((int)(170 + 60 * pulse), Green), 2f))
        {
            g.DrawPath(pen, path);
        }

        if (_logo != null)
        {
            g.DrawImage(_logo, Rectangle.Inflate(rect, -11, -11));
        }
        else
        {
            using var f = new Font("Segoe UI", 26f, FontStyle.Bold);
            using var b = new SolidBrush(White);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("TR", f, b, rect, sf);
        }
    }

    private static void DrawTitle(Graphics g, float x, float y, float pulse)
    {
        using var font = new Font("Segoe UI", 40f, FontStyle.Bold);
        using (var br = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
        {
            g.DrawString("TURBORAMA", font, br, x + 3, y + 3);
        }

        int a = (int)(20 + 28 * pulse);
        using (var br = new SolidBrush(Color.FromArgb(a, Green)))
        {
            g.DrawString("TURBORAMA", font, br, x - 1, y);
            g.DrawString("TURBORAMA", font, br, x + 1, y);
        }

        using (var br = new SolidBrush(White))
        {
            g.DrawString("TURBORAMA", font, br, x, y);
        }
    }

    private static void DrawChip(Graphics g, Rectangle rect, string text, bool on, bool cur, float pulse)
    {
        Color fill = on ? Color.FromArgb(cur ? (int)(48 + 36 * pulse) : 36, 0, 36, 14) : Color.FromArgb(28, 18, 18, 18);
        Color border = on ? (cur ? Green : GreenMid) : Color.FromArgb(70, 45, 45, 45);
        Color fg = on ? (cur ? White : Green) : WhiteDim;

        using (var path = RoundRect(rect, 4))
        using (var br = new SolidBrush(fill))
        using (var pen = new Pen(Color.FromArgb(cur ? 220 : 130, border), cur ? 1.5f : 1f))
        {
            g.FillPath(br, path);
            g.DrawPath(pen, path);
        }

        using var f = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        using var bt = new SolidBrush(fg);
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, f, bt, rect, sf);
    }

    private static void DrawBar(Graphics g, Rectangle bar, float value, float pulse)
    {
        value = Math.Clamp(value, 0f, 1f);
        using (var path = RoundRect(bar, 4))
        using (var br = new SolidBrush(Color.FromArgb(255, 12, 14, 12)))
        {
            g.FillPath(br, path);
        }

        if (value > 0.005f)
        {
            int fw = Math.Max(8, (int)(bar.Width * value));
            var fill = new Rectangle(bar.X, bar.Y, fw, bar.Height);
            using (var path = RoundRect(fill, 4))
            using (var br = new LinearGradientBrush(fill, GreenDark, Green, 0f))
            {
                g.FillPath(br, path);
            }

            using var pen = new Pen(Color.FromArgb(230, RedHot), 2.5f);
            g.DrawLine(pen, fill.Right - 1, bar.Y + 2, fill.Right - 1, bar.Bottom - 2);
        }

        using (var path = RoundRect(bar, 4))
        using (var pen = new Pen(Color.FromArgb((int)(120 + 80 * pulse), Green), 1.2f))
        {
            g.DrawPath(pen, path);
        }
    }

    private static void DrawBrackets(Graphics g, Rectangle card, float pulse)
    {
        int len = 24;
        int m = 12;
        int al = (int)(140 + 80 * pulse);
        using var penG = new Pen(Color.FromArgb(al, Green), 2.5f);
        using var penR = new Pen(Color.FromArgb(al, Red), 2.5f);
        g.DrawLine(penG, card.X + m, card.Y + m, card.X + m + len, card.Y + m);
        g.DrawLine(penG, card.X + m, card.Y + m, card.X + m, card.Y + m + len);
        g.DrawLine(penR, card.Right - m, card.Y + m, card.Right - m - len, card.Y + m);
        g.DrawLine(penR, card.Right - m, card.Y + m, card.Right - m, card.Y + m + len);
        g.DrawLine(penR, card.X + m, card.Bottom - m, card.X + m + len, card.Bottom - m);
        g.DrawLine(penR, card.X + m, card.Bottom - m, card.X + m, card.Bottom - m - len);
        g.DrawLine(penG, card.Right - m, card.Bottom - m, card.Right - m - len, card.Bottom - m);
        g.DrawLine(penG, card.Right - m, card.Bottom - m, card.Right - m, card.Bottom - m - len);
    }

    private static GraphicsPath RoundRect(Rectangle bounds, int radius)
    {
        int d = Math.Max(1, radius * 2);
        var path = new GraphicsPath();
        if (radius <= 0 || bounds.Width < d || bounds.Height < d)
        {
            path.AddRectangle(bounds);
            path.CloseFigure();
            return path;
        }

        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void ForceForeground()
    {
        try
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            TopMost = true;
            BringToFront();
            SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        }
        catch
        {
        }
    }

    private static Image? LoadLogoImage()
    {
        string[] paths =
        {
            ProductPaths.DefaultBootLogoPng,
            Path.Combine(AppContext.BaseDirectory, "Assets", "logo.png"),
            Path.Combine(ProductPaths.Root, "Launcher", "assets", "logo.png"),
        };

        foreach (string p in paths)
        {
            try
            {
                if (File.Exists(p))
                {
                    return Image.FromStream(new MemoryStream(File.ReadAllBytes(p)));
                }
            }
            catch
            {
            }
        }

        return null;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x00000008 | 0x00000080;
            return cp;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            return;
        }

        _animTimer.Stop();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animTimer.Dispose();
            _logo?.Dispose();
            _logo = null;
        }

        base.Dispose(disposing);
    }

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
