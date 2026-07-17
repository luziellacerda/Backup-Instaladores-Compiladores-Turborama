using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using TurboRama.Core.Paths;

namespace TurboRama.Launcher;

/// <summary>
/// Tela de desligar TurboRama — visual premium (power-off cinemático).
/// Independente da loading intro (LoadingScreenForm não é alterada).
/// </summary>
internal sealed class ShutdownScreenForm : Form
{
    private static readonly Color Black = Color.FromArgb(0, 1, 4);
    private static readonly Color Deep = Color.FromArgb(4, 8, 22);
    private static readonly Color DeepRed = Color.FromArgb(18, 4, 10);
    private static readonly Color Green = Color.FromArgb(0, 230, 105);
    private static readonly Color Cyan = Color.FromArgb(40, 210, 230);
    private static readonly Color White = Color.FromArgb(248, 252, 255);
    private static readonly Color Red = Color.FromArgb(255, 55, 60);
    private static readonly Color RedSoft = Color.FromArgb(200, 40, 55);
    private static readonly Color Amber = Color.FromArgb(255, 200, 70);
    private static readonly Color Muted = Color.FromArgb(130, 150, 165);

    private readonly System.Windows.Forms.Timer _animTimer;
    private Image? _logo;
    private float _t;
    private float _progressF; // 0..1 na fase 1 (desligar a decorrer)
    private string _status = "POWER OFF";
    private string _detail = "A preparar o desligamento...";
    private bool _finalPhase;
    private float _fade; // 0..1 escurece no final

    // partículas leves (estrelas / faíscas de energia)
    private readonly float[] _px = new float[28];
    private readonly float[] _py = new float[28];
    private readonly float[] _ps = new float[28];
    private readonly float[] _pa = new float[28];

    public ShutdownScreenForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = Black;
        KeyPreview = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);

        Bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        _logo = LoadLogo();
        InitParticles();

        _animTimer = new System.Windows.Forms.Timer { Interval = 33 };
        _animTimer.Tick += (_, _) =>
        {
            _t += 0.033f;
            Invalidate();
        };
    }

    private void InitParticles()
    {
        var rng = new Random(42);
        for (int i = 0; i < _px.Length; i++)
        {
            _px[i] = (float)rng.NextDouble();
            _py[i] = (float)rng.NextDouble();
            _ps[i] = 0.4f + (float)rng.NextDouble() * 1.8f;
            _pa[i] = (float)rng.NextDouble() * (float)Math.PI * 2f;
        }
    }

    /// <summary>
    /// Splash → (opcional) power-off real → mantém ecrã por cima do Windows.
    /// </summary>
    public static void ShowAndHold(int holdMsBefore, int holdMsAfter, Action? shutdownAction)
    {
        holdMsBefore = Math.Clamp(holdMsBefore, 400, 10000);
        holdMsAfter = Math.Clamp(holdMsAfter, 500, 30000);

        using var form = new ShutdownScreenForm();
        try
        {
            SetProcessShutdownParameters(0x100, 0);
        }
        catch
        {
            // ignore
        }

        form.Show();
        form.TopMost = true;
        form.BringToFront();
        form.Activate();
        form.ForceTopMost();
        try
        {
            Cursor.Hide();
        }
        catch
        {
            // ignore
        }

        form._animTimer.Start();
        form.Refresh();
        Application.DoEvents();

        RunHold(form, holdMsBefore, finalPhase: false, form.ForceTopMost);

        if (shutdownAction != null)
        {
            try
            {
                shutdownAction();
            }
            catch
            {
                // ignore
            }
        }

        form.SetFinalStatus();
        form.Refresh();
        Application.DoEvents();

        RunHold(form, holdMsAfter, finalPhase: true, form.ForceTopMost);

        try
        {
            form._animTimer.Stop();
        }
        catch
        {
            // ignore
        }
    }

    private static void RunHold(ShutdownScreenForm form, int holdMs, bool finalPhase, Action onTick)
    {
        form._finalPhase = finalPhase;
        var sw = Stopwatch.StartNew();
        const int frameMs = 33;

        while (sw.ElapsedMilliseconds < holdMs)
        {
            long elapsed = sw.ElapsedMilliseconds;
            float t = Math.Min(1f, elapsed / (float)Math.Max(1, holdMs));

            if (finalPhase)
            {
                form._progressF = 1f;
                form._fade = Math.Min(1f, t * 0.85f);
                form._status = "OFFLINE";
                form._detail = "ATE A PROXIMA PARTIDA";
            }
            else
            {
                form._progressF = t;
                form._fade = 0f;
                form.ApplyText(t);
            }

            form._t += 0.033f;

            try
            {
                form.Refresh();
            }
            catch
            {
                try
                {
                    form.Invalidate();
                    form.Update();
                }
                catch
                {
                    // ignore
                }
            }

            Application.DoEvents();
            onTick();

            int spent = (int)sw.ElapsedMilliseconds - (int)elapsed;
            int sleep = frameMs - spent;
            if (sleep > 1)
            {
                Thread.Sleep(sleep);
            }
        }
    }

    public void SetFinalStatus()
    {
        _finalPhase = true;
        _progressF = 1f;
        _status = "OFFLINE";
        _detail = "ATE A PROXIMA PARTIDA";
    }

    public void SetStatus(string text)
    {
        _detail = text ?? "";
        Invalidate();
    }

    private void ApplyText(float t)
    {
        if (t < 0.22f)
        {
            _status = "POWER OFF";
            _detail = "A preparar o desligamento...";
        }
        else if (t < 0.45f)
        {
            _status = "CLOSING";
            _detail = "A fechar sessao TURBORAMA...";
        }
        else if (t < 0.72f)
        {
            _status = "SHUTDOWN";
            _detail = "A desligar o sistema...";
        }
        else
        {
            _status = "GOODBYE";
            _detail = "Ate a proxima partida...";
        }
    }

    private void ForceTopMost()
    {
        try
        {
            TopMost = true;
            if (IsHandleCreated)
            {
                SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            }
        }
        catch
        {
            // ignore
        }
    }

    protected override void WndProc(ref Message m)
    {
        const int WmQueryEndSession = 0x0011;
        const int WmEndSession = 0x0016;
        if (m.Msg is WmQueryEndSession or WmEndSession)
        {
            ForceTopMost();
            m.Result = (IntPtr)1;
            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;

        int W = Math.Max(1, ClientSize.Width);
        int H = Math.Max(1, ClientSize.Height);

        DrawBackground(g, W, H);
        DrawAmbientGlow(g, W, H);
        DrawWaves(g, W, H);
        DrawParticles(g, W, H);
        DrawVignette(g, W, H);

        // Anel de power no centro (ícone hero)
        float ringY = H * 0.36f;
        DrawPowerRing(g, W * 0.5f, ringY, Math.Min(W, H) * 0.11f);

        // Painel glass inferior
        int boxW = Math.Min(820, (int)(W * 0.72));
        int boxH = Math.Min(280, (int)(H * 0.34));
        int bx = (W - boxW) / 2;
        int by = (int)(H * 0.58f);
        if (by + boxH > H - 40)
        {
            by = H - boxH - 40;
        }

        var box = new Rectangle(bx, by, boxW, boxH);
        DrawGlassPanel(g, box);
        DrawPanelContent(g, box);

        // Fade final (ecrã a apagar)
        if (_fade > 0.01f)
        {
            int a = (int)(200 * _fade);
            using var br = new SolidBrush(Color.FromArgb(a, 0, 0, 0));
            g.FillRectangle(br, 0, 0, W, H);
        }
    }

    private void DrawBackground(Graphics g, int W, int H)
    {
        // gradiente diagonal sutil preto → deep navy → deep red
        using var path = new GraphicsPath();
        path.AddRectangle(new Rectangle(0, 0, W, H));
        using var br = new LinearGradientBrush(
            new Rectangle(0, 0, W, H),
            Black, Deep, 105f);
        g.FillRectangle(br, 0, 0, W, H);

        // lavagem vermelha baixa (energia a morrer)
        float pulse = 0.5f + 0.5f * (float)Math.Sin(_t * 1.6);
        using (var redWash = new LinearGradientBrush(
                   new Rectangle(0, H / 2, W, H / 2 + 4),
                   Color.FromArgb(0, 0, 0, 0),
                   Color.FromArgb((int)(28 + 18 * pulse), 40, 0, 12), 90f))
        {
            g.FillRectangle(redWash, 0, H / 2, W, H / 2);
        }
    }

    private void DrawAmbientGlow(Graphics g, int W, int H)
    {
        // glows suaves com elipses (barato, sem PathGradient)
        float pulse = 0.55f + 0.45f * (float)Math.Sin(_t * 2.1);
        float cx = W * 0.5f;
        float cy = H * 0.34f;

        // halo grande ciano/verde fraco
        int rw = (int)(W * 0.28f);
        int rh = (int)(H * 0.16f);
        using (var br = new SolidBrush(Color.FromArgb((int)(18 + 14 * pulse), 0, 80, 110)))
        {
            g.FillEllipse(br, cx - rw, cy - rh, rw * 2, rh * 2);
        }

        // núcleo vermelho (power)
        int rw2 = (int)(W * 0.12f);
        int rh2 = (int)(H * 0.08f);
        using (var br = new SolidBrush(Color.FromArgb((int)(30 + 25 * pulse), 160, 20, 40)))
        {
            g.FillEllipse(br, cx - rw2, cy - rh2, rw2 * 2, rh2 * 2);
        }
    }

    private void DrawWaves(Graphics g, int W, int H)
    {
        DrawWaveLayer(g, W, H, H * 0.48f, H * 0.035f, 1.15f, 0.18f, 0.3f,
            Color.FromArgb(26, 10, 40, 70), Color.FromArgb(40, 0, 12, 28));
        DrawWaveLayer(g, W, H, H * 0.62f, H * 0.048f, 1.0f, 0.26f, 1.4f,
            Color.FromArgb(30, 20, 60, 90), Color.FromArgb(50, 0, 18, 35));
        DrawWaveLayer(g, W, H, H * 0.76f, H * 0.040f, 1.35f, 0.34f, 0.7f,
            Color.FromArgb(28, 40, 20, 50), Color.FromArgb(45, 0, 10, 22));
    }

    private void DrawWaveLayer(Graphics g, int W, int H, float baseY, float amp, float freq, float speed, float phase, Color top, Color bot)
    {
        int samples = 44;
        var poly = new PointF[samples + 3];
        for (int i = 0; i <= samples; i++)
        {
            float nx = i / (float)samples;
            float x = nx * W;
            float y = baseY
                      + amp * (float)Math.Sin(nx * Math.PI * 2 * freq + _t * speed + phase)
                      + amp * 0.38f * (float)Math.Sin(nx * Math.PI * 2 * freq * 1.6 + _t * speed * 1.15f + phase * 0.5f);
            poly[i] = new PointF(x, y);
        }

        poly[samples + 1] = new PointF(W + 2, H + 4);
        poly[samples + 2] = new PointF(-2, H + 4);
        using var path = new GraphicsPath();
        path.AddPolygon(poly);
        using var br = new LinearGradientBrush(
            new RectangleF(0, baseY - amp * 2f, W, Math.Max(8, H - baseY + amp * 3f)),
            top, bot, 90f);
        g.FillPath(br, path);
    }

    private void DrawParticles(Graphics g, int W, int H)
    {
        for (int i = 0; i < _px.Length; i++)
        {
            // sobem e piscam
            float x = _px[i] * W + 8f * (float)Math.Sin(_t * 0.7f + _pa[i]);
            float y = ((_py[i] - _t * 0.03f * _ps[i]) % 1f + 1f) % 1f * H;
            float a = 0.35f + 0.65f * (0.5f + 0.5f * (float)Math.Sin(_t * 3f + _pa[i]));
            int alpha = (int)(40 + 90 * a * (1f - _fade));
            if (alpha < 8)
            {
                continue;
            }

            float sz = 1.2f + _ps[i];
            Color c = i % 3 == 0
                ? Color.FromArgb(alpha, Cyan)
                : i % 3 == 1
                    ? Color.FromArgb(alpha, Green)
                    : Color.FromArgb(alpha, 255, 180, 180);
            using var br = new SolidBrush(c);
            g.FillEllipse(br, x, y, sz, sz);
        }
    }

    private static void DrawVignette(Graphics g, int W, int H)
    {
        int band = Math.Max(90, H / 6);
        using (var br = new LinearGradientBrush(new Rectangle(0, 0, W, band),
                   Color.FromArgb(220, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 90f))
        {
            g.FillRectangle(br, 0, 0, W, band);
        }

        using (var br = new LinearGradientBrush(new Rectangle(0, H - band, W, band),
                   Color.FromArgb(0, 0, 0, 0), Color.FromArgb(230, 0, 0, 0), 90f))
        {
            g.FillRectangle(br, 0, H - band, W, band);
        }

        int side = Math.Max(50, W / 14);
        using (var br = new LinearGradientBrush(new Rectangle(0, 0, side, H),
                   Color.FromArgb(120, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 0f))
        {
            g.FillRectangle(br, 0, 0, side, H);
        }

        using (var br = new LinearGradientBrush(new Rectangle(W - side, 0, side, H),
                   Color.FromArgb(0, 0, 0, 0), Color.FromArgb(120, 0, 0, 0), 0f))
        {
            g.FillRectangle(br, W - side, 0, side, H);
        }
    }

    /// <summary>Ícone power animado: anel que esvazia + traço vertical + pulso.</summary>
    private void DrawPowerRing(Graphics g, float cx, float cy, float radius)
    {
        float pulse = 0.55f + 0.45f * (float)Math.Sin(_t * 2.8);
        float p = Math.Clamp(_progressF, 0f, 1f);

        // Na fase final o anel "morre" (encolhe / desvanece)
        float die = _finalPhase ? (1f - _fade * 0.7f) : 1f;
        float r = radius * die;

        // aura exterior
        float auraR = r * (1.55f + 0.08f * pulse);
        using (var br = new SolidBrush(Color.FromArgb((int)(22 + 20 * pulse), RedSoft)))
        {
            g.FillEllipse(br, cx - auraR, cy - auraR, auraR * 2, auraR * 2);
        }

        using (var br = new SolidBrush(Color.FromArgb((int)(18 + 16 * pulse), 0, 100, 90)))
        {
            g.FillEllipse(br, cx - r * 1.25f, cy - r * 1.25f, r * 2.5f, r * 2.5f);
        }

        // track do anel
        float penW = Math.Max(4f, r * 0.12f);
        using (var pen = new Pen(Color.FromArgb(70, 40, 60, 70), penW))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
        }

        // arco de progresso (esvazia: começa cheio e some — power a ir embora)
        // sweep: de 360° até 0° conforme p sobe
        float remaining = _finalPhase ? 0f : (1f - p);
        if (remaining > 0.001f || (!_finalPhase && p < 0.02f))
        {
            float sweep = _finalPhase ? 0f : 360f * (1f - p);
            // começar do topo
            float start = -90f;
            Color arcCol = p < 0.55f
                ? Color.FromArgb((int)(200 + 40 * pulse), Green)
                : Color.FromArgb((int)(200 + 40 * pulse), Red);

            if (sweep > 1f)
            {
                using var pen = new Pen(arcCol, penW + 1f);
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, start, -sweep);
            }
        }

        // anel interno fino
        using (var pen = new Pen(Color.FromArgb((int)(90 + 50 * pulse), Cyan), 1.5f))
        {
            float ir = r * 0.78f;
            g.DrawEllipse(pen, cx - ir, cy - ir, ir * 2, ir * 2);
        }

        // símbolo power (barra vertical + gap no anel)
        float stemH = r * 0.55f;
        float stemW = Math.Max(3f, r * 0.10f);
        Color stemCol = _finalPhase
            ? Color.FromArgb((int)(120 * (1f - _fade)), Muted)
            : Color.FromArgb((int)(220 + 30 * pulse), White);

        using (var pen = new Pen(stemCol, stemW))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            g.DrawLine(pen, cx, cy - stemH * 0.15f, cx, cy - stemH);
        }

        // núcleo
        float core = r * 0.12f * (0.85f + 0.15f * pulse);
        using (var br = new SolidBrush(Color.FromArgb((int)(180 + 50 * pulse), Red)))
        {
            g.FillEllipse(br, cx - core, cy - core * 0.3f, core * 2, core * 2);
        }

        // percentagem grande sob o anel
        if (!_finalPhase)
        {
            string pct = ((int)Math.Round(p * 100)).ToString("00") + "%";
            using var f = new Font("Consolas", Math.Max(14f, r * 0.22f), FontStyle.Bold);
            SizeF sz = g.MeasureString(pct, f);
            using var b = new SolidBrush(Amber);
            g.DrawString(pct, f, b, cx - sz.Width / 2f, cy + r * 1.25f);
        }
        else
        {
            using var f = new Font("Segoe UI", Math.Max(12f, r * 0.18f), FontStyle.Bold);
            SizeF sz = g.MeasureString("OFF", f);
            using var b = new SolidBrush(Color.FromArgb((int)(200 * (1f - _fade * 0.5f)), Red));
            g.DrawString("OFF", f, b, cx - sz.Width / 2f, cy + r * 1.2f);
        }
    }

    private void DrawGlassPanel(Graphics g, Rectangle box)
    {
        for (int i = 6; i >= 1; i--)
        {
            using var path = RoundRect(box.X + i, box.Y + i + 3, box.Width, box.Height, 18);
            using var br = new SolidBrush(Color.FromArgb(10 + i * 7, 0, 0, 0));
            g.FillPath(br, path);
        }

        using (var path = RoundRect(box.X, box.Y, box.Width, box.Height, 18))
        using (var br = new LinearGradientBrush(box,
                   Color.FromArgb(210, 4, 16, 28),
                   Color.FromArgb(230, 2, 6, 12), 95f))
        {
            g.FillPath(br, path);
        }

        // highlight vidro
        var hi = new Rectangle(box.X + 12, box.Y + 8, box.Width - 24, box.Height / 2);
        using (var path = RoundRect(hi.X, hi.Y, hi.Width, hi.Height, 14))
        using (var br = new LinearGradientBrush(hi,
                   Color.FromArgb(50, 180, 220, 255),
                   Color.FromArgb(0, 255, 255, 255), 90f))
        {
            g.FillPath(br, path);
        }

        using (var path = RoundRect(box.X, box.Y, box.Width, box.Height, 18))
        using (var pen = new Pen(Color.FromArgb(150, Green), 2f))
        {
            g.DrawPath(pen, path);
        }

        // linha vermelha topo (assinatura marca)
        using (var pen = new Pen(Color.FromArgb(200, Red), 2.5f))
        {
            g.DrawLine(pen, box.X + 30, box.Y + 3, box.Right - 30, box.Y + 3);
        }
    }

    private void DrawPanelContent(Graphics g, Rectangle box)
    {
        int pad = 30;
        int x = box.X + pad;
        int y = box.Y + pad;
        int innerW = box.Width - pad * 2;
        bool blink = ((int)(_t * 7)) % 2 == 0;

        // logo
        var logo = new Rectangle(x, y, 62, 62);
        using (var br = new SolidBrush(Color.FromArgb(170, 12, 8, 14)))
        {
            g.FillEllipse(br, logo);
        }

        using (var pen = new Pen(Color.FromArgb(200, Red), 2f))
        {
            g.DrawEllipse(pen, logo);
        }

        if (_logo != null)
        {
            var lr = Rectangle.Inflate(logo, -9, -9);
            using var clip = new GraphicsPath();
            clip.AddEllipse(lr);
            g.SetClip(clip);
            g.DrawImage(_logo, lr);
            g.ResetClip();
        }
        else
        {
            using var f = new Font("Segoe UI", 16f, FontStyle.Bold);
            using var b = new SolidBrush(White);
            g.DrawString("TR", f, b, logo.X + 14, logo.Y + 16);
        }

        using (var f = new Font("Segoe UI", 26f, FontStyle.Bold))
        using (var b = new SolidBrush(White))
        {
            g.DrawString("TURBORAMA", f, b, x + 78, y + 2);
        }

        using (var f = new Font("Segoe UI", 10f, FontStyle.Bold))
        using (var b = new SolidBrush(Green))
        {
            g.DrawString("ARCADE  ·  SYSTEM POWER", f, b, x + 80, y + 40);
        }

        // status
        Color statusCol = _finalPhase ? Green : Amber;
        using (var f = new Font("Consolas", 12f, FontStyle.Bold))
        using (var b = new SolidBrush(statusCol))
        {
            string s = _status + (blink && !_finalPhase ? "  _" : "");
            g.DrawString(s, f, b, x, y + 78);
        }

        using (var f = new Font("Segoe UI", _finalPhase ? 16f : 13f, FontStyle.Bold))
        using (var b = new SolidBrush(_finalPhase ? Green : White))
        {
            g.DrawString(_detail, f, b, x, y + 104);
        }

        // barra de energia a esvaziar (1 → 0 visualmente via fill = 1-progress no sentido "queda")
        var bar = new Rectangle(x, y + 148, innerW, 16);
        float energy = _finalPhase ? 0f : Math.Max(0f, 1f - _progressF);
        // mostra preenchimento restante de energia
        DrawEnergyBar(g, bar, energy);

        using (var f = new Font("Segoe UI", 8.5f))
        using (var b = new SolidBrush(Muted))
        {
            g.DrawString("TurboRama Arcade — desligamento seguro do console", f, b, x, box.Bottom - 32);
        }
    }

    private void DrawEnergyBar(Graphics g, Rectangle bar, float energyLeft)
    {
        float pulse = 0.55f + 0.45f * (float)Math.Sin(_t * 2.6);
        energyLeft = Math.Clamp(energyLeft, 0f, 1f);

        using (var path = RoundRect(bar.X, bar.Y, bar.Width, bar.Height, 8))
        using (var br = new SolidBrush(Color.FromArgb(255, 3, 10, 16)))
        {
            g.FillPath(br, path);
        }

        using (var path = RoundRect(bar.X, bar.Y, bar.Width, bar.Height, 8))
        using (var pen = new Pen(Color.FromArgb((int)(80 + 60 * pulse), 60, 100, 110), 1.4f))
        {
            g.DrawPath(pen, path);
        }

        if (energyLeft > 0.004f)
        {
            int pad = 3;
            int fw = Math.Max(4, (int)Math.Round((bar.Width - pad * 2) * energyLeft));
            var fill = new Rectangle(bar.X + pad, bar.Y + pad, fw, bar.Height - pad * 2);

            using var path = RoundRect(fill.X, fill.Y, fill.Width, fill.Height, 6);
            using var br = new LinearGradientBrush(fill,
                Color.FromArgb(255, 0, 160, 80),
                Color.FromArgb(255, 255, 60, 50), 0f);
            var blend = new ColorBlend(3)
            {
                Colors =
                [
                    energyLeft > 0.4f
                        ? Color.FromArgb(255, 0, 200, 110)
                        : Color.FromArgb(255, 200, 40, 40),
                    energyLeft > 0.4f
                        ? Color.FromArgb(255, 40, 230, 160)
                        : Color.FromArgb(255, 255, 100, 40),
                    energyLeft > 0.4f
                        ? Color.FromArgb(255, 80, 255, 200)
                        : Color.FromArgb(255, 255, 180, 60)
                ],
                Positions = [0f, 0.5f, 1f]
            };
            br.InterpolationColors = blend;
            g.FillPath(br, path);

            // brilho superior
            if (fill.Width > 10)
            {
                var hi = new Rectangle(fill.X + 2, fill.Y + 1, fill.Width - 4, Math.Max(2, fill.Height / 2));
                using var hp = RoundRect(hi.X, hi.Y, hi.Width, hi.Height, 4);
                using var hb = new LinearGradientBrush(hi,
                    Color.FromArgb((int)(70 + 40 * pulse), 255, 255, 255),
                    Color.FromArgb(0, 255, 255, 255), 90f);
                g.FillPath(hb, hp);
            }
        }

        // label energia
        using var f = new Font("Segoe UI", 8f, FontStyle.Bold);
        using var b = new SolidBrush(Color.FromArgb(160, 140, 200, 210));
        g.DrawString("ENERGY", f, b, bar.X, bar.Y - 14);
        string right = _finalPhase ? "0%" : ((int)Math.Round(energyLeft * 100)).ToString("00") + "%";
        SizeF sz = g.MeasureString(right, f);
        g.DrawString(right, f, b, bar.Right - sz.Width, bar.Y - 14);
    }

    private static GraphicsPath RoundRect(int x, int y, int w, int h, int r)
    {
        int d = Math.Max(1, r * 2);
        var p = new GraphicsPath();
        if (w < d || h < d)
        {
            p.AddRectangle(new Rectangle(x, y, w, h));
            p.CloseFigure();
            return p;
        }

        p.AddArc(x, y, d, d, 180, 90);
        p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        p.AddArc(x, y + h - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static Image? LoadLogo()
    {
        string[] paths =
        {
            ProductPaths.DefaultBootLogoPng,
            Path.Combine(AppContext.BaseDirectory, "Assets", "logo.png"),
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
                // ignore
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
        }

        base.Dispose(disposing);
    }

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessShutdownParameters(uint dwLevel, uint dwFlags);
}
