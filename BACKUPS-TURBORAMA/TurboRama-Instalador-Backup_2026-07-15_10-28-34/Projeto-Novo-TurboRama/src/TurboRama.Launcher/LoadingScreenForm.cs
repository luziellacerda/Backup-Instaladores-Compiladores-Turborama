using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using TurboRama.Configuration;
using TurboRama.Core.Paths;

namespace TurboRama.Launcher;

/// <summary>
/// Boot screen TURBORAMA — paleta: preto, verde, branco, vermelho.
/// Progresso estável (timer). Marca ANTES do jogo. Sem bolinhas Windows (não mexe BCD).
/// </summary>
internal sealed class LoadingScreenForm : Form
{
    private static readonly Color Black = Color.FromArgb(0, 0, 0);
    private static readonly Color BlackSoft = Color.FromArgb(12, 14, 12);
    private static readonly Color Green = Color.FromArgb(0, 220, 80);
    private static readonly Color GreenDim = Color.FromArgb(0, 140, 50);
    private static readonly Color GreenDark = Color.FromArgb(0, 60, 25);
    private static readonly Color White = Color.FromArgb(245, 245, 245);
    private static readonly Color WhiteDim = Color.FromArgb(170, 175, 170);
    private static readonly Color Red = Color.FromArgb(220, 30, 40);
    private static readonly Color RedSoft = Color.FromArgb(160, 20, 30);

    private readonly System.Windows.Forms.Timer _animTimer;
    private Image? _logo;
    private int _progress;
    private float _t;
    private string _status = "A iniciar...";
    private string _phase = "BOOT";
    private bool _holding;
    private readonly Stopwatch _sw = new();
    private int _holdMs = 5000;
    private bool _holdDone;
    private Bitmap? _gridCache;
    private Size _gridSize;

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

        Rectangle screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        Bounds = screen;

        _logo = LoadLogoImage();

        _animTimer = new System.Windows.Forms.Timer { Interval = 33 };
        _animTimer.Tick += AnimTimer_Tick;

        Shown += (_, _) =>
        {
            ForceForeground();
            Invalidate();
        };
    }

    private void AnimTimer_Tick(object? sender, EventArgs e)
    {
        _t += 0.033f;
        if (_t > 100f)
        {
            _t = 0;
        }

        if (_holding && !_holdDone)
        {
            double elapsed = _sw.ElapsedMilliseconds;
            double t = Math.Min(1.0, elapsed / Math.Max(1, _holdMs));
            double eased = 1.0 - Math.Pow(1.0 - t, 2.0);
            _progress = Math.Max(_progress, Math.Min(99, (int)(eased * 99)));

            if (t < 0.25)
            {
                _phase = "BOOT";
                _status = "Sistema arcade a iniciar...";
            }
            else if (t < 0.5)
            {
                _phase = "LOAD";
                _status = "A carregar TURBORAMA...";
            }
            else if (t < 0.75)
            {
                _phase = "SYNC";
                _status = "A preparar a sessão...";
            }
            else if (t < 0.95)
            {
                _phase = "READY";
                _status = "Quase pronto...";
            }
            else
            {
                _phase = "GO";
                _status = "A abrir o jogo...";
            }

            if (elapsed >= _holdMs)
            {
                _progress = 100;
                _status = "Pronto";
                _phase = "GO";
                _holdDone = true;
            }

            if ((int)elapsed % 400 < 40)
            {
                ForceForeground();
            }
        }

        Invalidate();
    }

    public void ShowBrandHold(int minMs, Action<int, string>? onTick = null)
    {
        if (minMs < 3500)
        {
            minMs = 3500;
        }

        _holdMs = minMs;
        _holdDone = false;
        _progress = 0;
        _status = "Sistema arcade a iniciar...";
        _phase = "BOOT";
        _holding = true;

        Bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        Show();
        ForceForeground();
        _sw.Restart();
        _animTimer.Start();

        for (int i = 0; i < 6; i++)
        {
            Application.DoEvents();
            Thread.Sleep(16);
        }

        int safety = _holdMs + 8000;
        var gate = Stopwatch.StartNew();
        while (!_holdDone && gate.ElapsedMilliseconds < safety)
        {
            onTick?.Invoke(_progress, _status);
            Application.DoEvents();
            Thread.Sleep(20);
        }

        if (!_holdDone)
        {
            _progress = 100;
            _status = "Pronto";
            _phase = "GO";
            _holdDone = true;
            Invalidate();
            Application.DoEvents();
        }

        Thread.Sleep(200);
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
        if (_progress >= 100)
        {
            _holdDone = true;
        }

        Invalidate();
        Application.DoEvents();
    }

    public void HideLoading()
    {
        try
        {
            _holding = false;
            _animTimer.Stop();
            Hide();
        }
        catch
        {
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        try
        {
            PaintScreen(e.Graphics, ClientRectangle);
        }
        catch
        {
            try
            {
                e.Graphics.Clear(Black);
                using var f = new Font("Segoe UI", 32f, FontStyle.Bold);
                using var b = new SolidBrush(Green);
                e.Graphics.DrawString("TURBORAMA", f, b, 48, 48);
                using var f2 = new Font("Segoe UI", 14f);
                using var b2 = new SolidBrush(White);
                e.Graphics.DrawString(_progress + "%  " + _status, f2, b2, 48, 100);
            }
            catch
            {
            }
        }
    }

    private void PaintScreen(Graphics g, Rectangle r)
    {
        if (r.Width < 20 || r.Height < 20)
        {
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // Fundo preto → verde muito escuro
        using (var bg = new LinearGradientBrush(r, Black, BlackSoft, 90f))
        {
            g.FillRectangle(bg, r);
        }

        DrawGrid(g, r);

        float pulse = 0.55f + 0.45f * (float)Math.Sin(_t * 2.8);

        // Linhas HUD verde / vermelho
        using (var penG = new Pen(Color.FromArgb((int)(120 + 100 * pulse), Green), 2f))
        using (var penR = new Pen(Color.FromArgb((int)(90 + 70 * pulse), Red), 1.5f))
        {
            g.DrawLine(penG, r.Width * 0.10f, r.Height * 0.11f, r.Width * 0.90f, r.Height * 0.11f);
            g.DrawLine(penR, r.Width * 0.14f, r.Height * 0.89f, r.Width * 0.86f, r.Height * 0.89f);
        }

        // Scanline suave branca
        float scanY = r.Height * ((_t * 0.12f) % 1f);
        using (var penScan = new Pen(Color.FromArgb(28, White), 1.5f))
        {
            g.DrawLine(penScan, 0, scanY, r.Width, scanY);
        }

        int cardW = Math.Min(920, (int)(r.Width * 0.76));
        int cardH = Math.Min(440, (int)(r.Height * 0.52));
        var card = new Rectangle((r.Width - cardW) / 2, (r.Height - cardH) / 2, cardW, cardH);
        DrawPanel(g, card, pulse);

        int pad = 44;
        int x = card.X + pad;
        int y = card.Y + pad;
        int w = card.Width - pad * 2;

        // Logo
        var logoR = new Rectangle(x, y, 96, 96);
        DrawLogoBox(g, logoR, pulse);

        float tx = x + 116;
        float ty = y + 8;
        DrawGlowText(g, "TURBORAMA", new Font("Segoe UI", 40f, FontStyle.Bold), tx, ty, White, Green, pulse);

        using (var f = new Font("Segoe UI", 13f, FontStyle.Bold))
        using (var b = new SolidBrush(Green))
        {
            g.DrawString("ARCADE", f, b, tx, ty + 58);
        }

        using (var pen = new Pen(Red, 3f))
        {
            g.DrawLine(pen, tx, ty + 90, tx + 120, ty + 90);
        }

        using (var pen = new Pen(Color.FromArgb(180, Green), 1.5f))
        {
            g.DrawLine(pen, tx + 4, ty + 96, tx + 90, ty + 96);
        }

        // Fases
        string[] phases = { "BOOT", "LOAD", "SYNC", "READY", "GO" };
        int chipY = y + 125;
        int chipW = Math.Max(72, (w - 32) / phases.Length - 8);
        int active = Array.IndexOf(phases, _phase);
        if (active < 0)
        {
            active = Math.Min(phases.Length - 1, _progress / 25);
        }

        for (int i = 0; i < phases.Length; i++)
        {
            int cx = x + i * (chipW + 10);
            DrawChip(g, new Rectangle(cx, chipY, chipW, 28), phases[i], i <= active, i == active, pulse);
        }

        using (var f = new Font("Segoe UI", 12.5f, FontStyle.Regular))
        using (var b = new SolidBrush(WhiteDim))
        {
            g.DrawString(_status, f, b, x, chipY + 48);
        }

        string pct = _progress.ToString("00") + "%";
        using (var f = new Font("Consolas", 20f, FontStyle.Bold))
        using (var b = new SolidBrush(Green))
        {
            SizeF sz = g.MeasureString(pct, f);
            g.DrawString(pct, f, b, x + w - sz.Width, chipY + 40);
        }

        var bar = new Rectangle(x, chipY + 86, w, 14);
        DrawBar(g, bar, _progress / 100f, pulse);

        using (var f = new Font("Segoe UI", 9.5f, FontStyle.Regular))
        using (var b = new SolidBrush(Color.FromArgb(130, WhiteDim)))
        {
            g.DrawString("Console arcade  ·  sessão segura", f, b, x, card.Bottom - 40);
        }

        DrawCorners(g, card, Green, Red, pulse);
    }

    private void DrawGrid(Graphics g, Rectangle r)
    {
        if (_gridCache == null || _gridSize != r.Size)
        {
            _gridCache?.Dispose();
            _gridCache = new Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height));
            _gridSize = r.Size;
            using var gg = Graphics.FromImage(_gridCache);
            gg.Clear(Color.Transparent);
            using var pen = new Pen(Color.FromArgb(22, GreenDim), 1f);
            const int step = 52;
            for (int gx = 0; gx < r.Width; gx += step)
            {
                gg.DrawLine(pen, gx, 0, gx, r.Height);
            }

            for (int gy = 0; gy < r.Height; gy += step)
            {
                gg.DrawLine(pen, 0, gy, r.Width, gy);
            }
        }

        g.DrawImageUnscaled(_gridCache, 0, 0);
    }

    private static void DrawPanel(Graphics g, Rectangle card, float pulse)
    {
        var sh = card;
        sh.Offset(0, 10);
        using (var path = RoundRect(sh, 8))
        using (var br = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
        {
            g.FillPath(br, path);
        }

        using (var path = RoundRect(card, 8))
        using (var br = new LinearGradientBrush(card,
                   Color.FromArgb(240, 10, 14, 10),
                   Color.FromArgb(240, 4, 6, 4), 90f))
        {
            g.FillPath(br, path);
        }

        using (var path = RoundRect(card, 8))
        using (var pen = new Pen(Color.FromArgb((int)(140 + 70 * pulse), Green), 1.5f))
        {
            g.DrawPath(pen, path);
        }

        using (var pen = new Pen(Color.FromArgb((int)(160 + 60 * pulse), Green), 2f))
        {
            g.DrawLine(pen, card.X + 28, card.Y + 2, card.Right - 28, card.Y + 2);
        }
    }

    private void DrawLogoBox(Graphics g, Rectangle rect, float pulse)
    {
        using (var path = RoundRect(rect, 8))
        using (var br = new LinearGradientBrush(rect, GreenDark, Black, 45f))
        {
            g.FillPath(br, path);
        }

        using (var path = RoundRect(rect, 8))
        using (var pen = new Pen(Color.FromArgb((int)(160 + 70 * pulse), Green), 1.5f))
        {
            g.DrawPath(pen, path);
        }

        if (_logo != null)
        {
            g.DrawImage(_logo, Rectangle.Inflate(rect, -10, -10));
        }
        else
        {
            using var f = new Font("Segoe UI", 26f, FontStyle.Bold);
            using var b = new SolidBrush(Green);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("TR", f, b, rect, sf);
        }
    }

    private static void DrawGlowText(Graphics g, string text, Font font, float x, float y, Color main, Color glow, float pulse)
    {
        int a = (int)(30 + 50 * pulse);
        using (var br = new SolidBrush(Color.FromArgb(a, glow)))
        {
            g.DrawString(text, font, br, x + 2, y + 2);
            g.DrawString(text, font, br, x - 1, y);
            g.DrawString(text, font, br, x + 1, y);
        }

        using (var br = new SolidBrush(main))
        {
            g.DrawString(text, font, br, x, y);
        }
    }

    private static void DrawChip(Graphics g, Rectangle rect, string text, bool on, bool cur, float pulse)
    {
        Color fill = on
            ? Color.FromArgb(cur ? (int)(50 + 40 * pulse) : 35, 0, 50, 20)
            : Color.FromArgb(28, 20, 20, 20);
        Color border = on ? (cur ? Green : GreenDim) : Color.FromArgb(80, 60, 60, 60);
        Color fg = on ? (cur ? White : Green) : WhiteDim;

        using (var path = RoundRect(rect, 4))
        using (var br = new SolidBrush(fill))
        using (var pen = new Pen(Color.FromArgb(cur ? 220 : 120, border), cur ? 1.5f : 1f))
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
        using (var path = RoundRect(bar, 3))
        using (var br = new SolidBrush(Color.FromArgb(255, 18, 20, 18)))
        {
            g.FillPath(br, path);
        }

        if (value > 0.005f)
        {
            int fw = Math.Max(6, (int)(bar.Width * value));
            var fill = new Rectangle(bar.X, bar.Y, fw, bar.Height);
            // Verde → branco no fim; detalhe vermelho no head
            using (var path = RoundRect(fill, 3))
            using (var br = new LinearGradientBrush(fill, GreenDim, Green, 0f))
            {
                g.FillPath(br, path);
            }

            using var pen = new Pen(Color.FromArgb(200, Red), 2f);
            g.DrawLine(pen, fill.Right - 1, bar.Y + 1, fill.Right - 1, bar.Bottom - 1);
        }

        using (var path = RoundRect(bar, 3))
        using (var pen = new Pen(Color.FromArgb((int)(100 + 80 * pulse), Green), 1f))
        {
            g.DrawPath(pen, path);
        }
    }

    private static void DrawCorners(Graphics g, Rectangle card, Color a, Color b, float pulse)
    {
        int len = 20;
        int m = 10;
        int al = (int)(130 + 70 * pulse);
        using var penA = new Pen(Color.FromArgb(al, a), 2f);
        using var penB = new Pen(Color.FromArgb(al, b), 2f);
        g.DrawLine(penA, card.X + m, card.Y + m, card.X + m + len, card.Y + m);
        g.DrawLine(penA, card.X + m, card.Y + m, card.X + m, card.Y + m + len);
        g.DrawLine(penB, card.Right - m, card.Y + m, card.Right - m - len, card.Y + m);
        g.DrawLine(penB, card.Right - m, card.Y + m, card.Right - m, card.Y + m + len);
        g.DrawLine(penB, card.X + m, card.Bottom - m, card.X + m + len, card.Bottom - m);
        g.DrawLine(penB, card.X + m, card.Bottom - m, card.X + m, card.Bottom - m - len);
        g.DrawLine(penA, card.Right - m, card.Bottom - m, card.Right - m - len, card.Bottom - m);
        g.DrawLine(penA, card.Right - m, card.Bottom - m, card.Right - m, card.Bottom - m - len);
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
            _gridCache?.Dispose();
            _logo = null;
            _gridCache = null;
        }

        base.Dispose(disposing);
    }

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
