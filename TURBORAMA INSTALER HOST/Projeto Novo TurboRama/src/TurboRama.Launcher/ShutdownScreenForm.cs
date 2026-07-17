using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using TurboRama.Core.Paths;

namespace TurboRama.Launcher;

/// <summary>
/// DESLIGAR — ficheiro PRÓPRIO: ShutdownScreenForm.cs (separado de LoadingScreenForm.cs).
/// Visual no mesmo estilo da loading, mas código e assets diferentes.
/// Assets: Assets\logo-shutdown.png (+ opcional Assets\shutdown.wav)
/// Preview: --test-shutdown  |  NÃO usar --test-loading
/// </summary>
internal sealed class ShutdownScreenForm : Form
{
    // Mesmas cores da loading
    private static readonly Color Black = Color.FromArgb(1, 3, 8);
    private static readonly Color DeepMid = Color.FromArgb(0, 20, 48);
    private static readonly Color Green = Color.FromArgb(0, 230, 100);
    private static readonly Color White = Color.FromArgb(245, 255, 250);
    private static readonly Color Red = Color.FromArgb(230, 40, 45);
    private static readonly Color Amber = Color.FromArgb(255, 210, 80);

    /// <summary>Evita repetir o mesmo modelo duas vezes seguidas.</summary>
    private static int _lastModelIndex = -1;

    /// <summary>5 modelos de saudade + agradecimento — cliente sempre sai com um sorriso.</summary>
    private static readonly FarewellModel[] FarewellModels =
    {
        // 1 — Amigo do arcade
        new(
            Name: "Amigo",
            Steps:
            [
                ("OBRIGADO", "Foi otimo ter voce aqui hoje!"),
                ("SAUDADE", "Ja estamos com saudades das partidas..."),
                ("GRACAS", "Obrigado por jogar no TURBORAMA!"),
                ("ATE JA", "Guarde esse sorriso para a proxima!"),
                ("BYE", "Ate logo, campeao!")
            ],
            FinalStatus: "OBRIGADO",
            FinalDetail: "Voce fez o nosso dia! Ate a proxima!",
            Footer: "TURBORAMA  ·  Obrigado por jogar conosco"),

        // 2 — Familia / carinho
        new(
            Name: "Carinho",
            Steps:
            [
                ("VALEU", "Sua visita fez toda a diferenca!"),
                ("CARINHO", "Leve este carinho para casa..."),
                ("OBRIGADO", "Agradecemos de coracao a preferencia!"),
                ("SAUDADE", "Vamos sentir a sua falta no fliperama!"),
                ("ATE BREVE", "Volte sempre — a porta esta aberta!")
            ],
            FinalStatus: "ATE BREVE",
            FinalDetail: "Com carinho, equipe TURBORAMA",
            Footer: "TURBORAMA  ·  Feito com carinho para voce"),

        // 3 — Campeao / motivacional
        new(
            Name: "Campeao",
            Steps:
            [
                ("CAMPEAO", "Que partidas incriveis voce fez!"),
                ("ORGULHO", "Orgulho de ter voce no nosso arcade!"),
                ("OBRIGADO", "Obrigado por cada credito e sorriso!"),
                ("LENDA", "Hoje voce foi lenda no TURBORAMA!"),
                ("RECORDE", "Ate a proxima — quebre mais recordes!")
            ],
            FinalStatus: "LENDA",
            FinalDetail: "Voce e especial. Ate a proxima vitoria!",
            Footer: "TURBORAMA  ·  Aqui todo jogador e campeao"),

        // 4 — Poético / saudade suave
        new(
            Name: "Saudade",
            Steps:
            [
                ("DESCANSO", "Hora de guardar o joystick com carinho..."),
                ("SAUDADE", "As luzes apagam, a saudade fica..."),
                ("MEMORIA", "Obrigado pelas memorias de hoje!"),
                ("SONHO", "Sonhe com a proxima partida..."),
                ("ATE LOGO", "Ate logo — o arcade espera por voce!")
            ],
            FinalStatus: "SAUDADE",
            FinalDetail: "Foi lindo ter voce. Volte em breve!",
            Footer: "TURBORAMA  ·  Cada partida vira uma boa lembranca"),

        // 5 — Energia positiva / festa
        new(
            Name: "Festa",
            Steps:
            [
                ("SHOW", "Que energia boa voce trouxe hoje!"),
                ("FESTA", "Obrigado por animar o TURBORAMA!"),
                ("SORRISO", "Seu sorriso iluminou o arcade!"),
                ("ABRACO", "Um abraco virtual e muito obrigado!"),
                ("VOLTE", "Ja estamos contando os minutos!")
            ],
            FinalStatus: "VOLTE SEMPRE",
            FinalDetail: "Obrigado! Voce e parte da familia TURBORAMA!",
            Footer: "TURBORAMA  ·  Diversao, amizade e boa energia")
    };

    private readonly System.Windows.Forms.Timer _animTimer;
    private Image? _logo;
    private int _progress;
    private float _progressF;
    private float _t;
    private string _status = "OBRIGADO";
    private string _detail = "Obrigado por jogar!";
    private string _footer = "TURBORAMA ARCADE SYSTEM";
    private bool _finalPhase;
    private FarewellModel _model;

    private readonly record struct FarewellModel(
        string Name,
        (string Status, string Detail)[] Steps,
        string FinalStatus,
        string FinalDetail,
        string Footer);

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
        // Logo do DESLIGAR (logo-shutdown.png) — NÃO carrega logo.png do boot
        _logo = LoadShutdownLogo();
        _model = PickFarewellModel();
        _footer = _model.Footer;
        _status = _model.Steps[0].Status;
        _detail = _model.Steps[0].Detail;

        _animTimer = new System.Windows.Forms.Timer { Interval = 30 };
        _animTimer.Tick += (_, _) =>
        {
            _t += 0.03f;
            Invalidate();
        };
    }

    private static FarewellModel PickFarewellModel()
    {
        int n = FarewellModels.Length;
        int idx;
        if (n <= 1)
        {
            idx = 0;
        }
        else
        {
            // Aleatório sem repetir o último
            do
            {
                idx = Random.Shared.Next(n);
            } while (idx == _lastModelIndex && n > 1);
        }

        _lastModelIndex = idx;
        return FarewellModels[idx];
    }

    /// <summary>
    /// Mostra a mesma UI da loading, anima a barra, opcionalmente desliga o Windows,
    /// e mantém a tela por cima enquanto o sistema encerra.
    /// </summary>
    public static void ShowAndHold(int holdMsBefore, int holdMsAfter, Action? shutdownAction)
    {
        holdMsBefore = Math.Clamp(holdMsBefore, 400, 12000);
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

        // Cobre o ecrã principal de imediato (evita “preto vazio” antes do 1º paint)
        try
        {
            var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
            form.Bounds = bounds;
            if (!form.IsHandleCreated)
            {
                form.CreateControl();
            }

            form.Opacity = 1d;
            form.Visible = true;
            form.Show();
            form.TopMost = true;
            form.BringToFront();
            form.Activate();
            form.ForceTopMost();
            // Preenche já com cor de fundo (mesmo se o paint rico falhar)
            using (var g = form.CreateGraphics())
            {
                g.Clear(Black);
            }

            form.Refresh();
            Application.DoEvents();
        }
        catch
        {
            try
            {
                form.Show();
                form.ForceTopMost();
            }
            catch
            {
                // ignore
            }
        }

        try
        {
            Cursor.Hide();
        }
        catch
        {
            // ignore
        }

        form._animTimer.Stop();

        // Fase 1: barra 0→100
        form.RunProgressHold(holdMsBefore, finalPhase: false);

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

        form.ApplyFinalFarewell();
        try
        {
            form.Refresh();
            Application.DoEvents();
        }
        catch
        {
            // ignore
        }

        // Fase 2: mantém ecrã enquanto Windows desliga
        form.RunProgressHold(holdMsAfter, finalPhase: true);

        try
        {
            form._animTimer.Stop();
        }
        catch
        {
            // ignore
        }
    }

    private void RunProgressHold(int holdMs, bool finalPhase)
    {
        _finalPhase = finalPhase;
        var sw = Stopwatch.StartNew();
        // 20 fps — menos GDI/memória do que 30 (evita erro de memória em PCs fracos)
        const int frameMs = 50;

        while (sw.ElapsedMilliseconds < holdMs)
        {
            long elapsed = sw.ElapsedMilliseconds;
            float t = Math.Min(1f, elapsed / (float)Math.Max(1, holdMs));

            if (finalPhase)
            {
                _progressF = 1f;
                _progress = 100;
                ApplyFinalFarewell();
            }
            else
            {
                _progressF = t;
                _progress = Math.Clamp((int)Math.Round(t * 100f), 0, 100);
                ApplyText(t);
            }

            _t += 0.05f;

            try
            {
                Refresh();
            }
            catch
            {
                try
                {
                    Invalidate();
                    Update();
                }
                catch
                {
                    // ignore
                }
            }

            try
            {
                Application.DoEvents();
            }
            catch
            {
                // ignore
            }

            ForceTopMost();

            int spent = (int)sw.ElapsedMilliseconds - (int)elapsed;
            int sleep = frameMs - spent;
            if (sleep > 1)
            {
                Thread.Sleep(sleep);
            }
        }
    }

    public void SetStatus(string text)
    {
        _detail = text ?? "";
        Invalidate();
    }

    private void ApplyFinalFarewell()
    {
        _finalPhase = true;
        _progressF = 1f;
        _progress = 100;
        _status = _model.FinalStatus;
        _detail = _model.FinalDetail;
        _footer = _model.Footer;
    }

    private void ApplyText(float t)
    {
        // 5 etapas do modelo escolhido (saudade + agradecimento)
        var steps = _model.Steps;
        if (steps == null || steps.Length == 0)
        {
            _status = "OBRIGADO";
            _detail = "Obrigado por jogar no TURBORAMA!";
            return;
        }

        int i;
        if (t < 0.2f) i = 0;
        else if (t < 0.4f) i = Math.Min(1, steps.Length - 1);
        else if (t < 0.65f) i = Math.Min(2, steps.Length - 1);
        else if (t < 0.9f) i = Math.Min(3, steps.Length - 1);
        else i = Math.Min(4, steps.Length - 1);

        _status = steps[i].Status;
        _detail = steps[i].Detail;
        _footer = _model.Footer;
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

    // ——— Paint: cópia visual da LoadingScreenForm ———

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        int W = Math.Max(1, ClientSize.Width);
        int H = Math.Max(1, ClientSize.Height);
        bool blink = ((int)(_t * 8)) % 2 == 0;

        try
        {
            PaintFull(g, W, H, blink);
        }
        catch
        {
            // Fallback seguro (sem ondas) — nunca deixar ecrã “morto” preto sem texto
            try
            {
                g.Clear(Black);
                using var f = new Font("Segoe UI", 28f, FontStyle.Bold);
                using var b = new SolidBrush(White);
                string title = string.IsNullOrEmpty(_detail) ? "TURBORAMA" : _detail;
                g.DrawString("TURBORAMA", f, b, 40, H / 2f - 40);
                using var f2 = new Font("Segoe UI", 16f, FontStyle.Bold);
                using var b2 = new SolidBrush(Green);
                g.DrawString(title, f2, b2, 40, H / 2f + 10);
            }
            catch
            {
                // ignore
            }
        }
    }

    private void PaintFull(Graphics g, int W, int H, bool blink)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

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

        int boxW = Math.Min(780, (int)(W * 0.70));
        int boxH = Math.Min(340, (int)(H * 0.46));
        int bx = (W - boxW) / 2;
        int by = (H - boxH) / 2;
        var box = new Rectangle(bx, by, boxW, boxH);

        DrawGlassPanel(g, box);
        DrawContent(g, box, blink);
    }

    private void DrawRealisticWaves(Graphics g, int W, int H)
    {
        DrawWaveLayer(g, W, H,
            baseY: H * 0.28f, amp: H * 0.050f, freq: 1.25f, speed: 0.30f, phase: 0.2f,
            top: Color.FromArgb(32, 10, 60, 110), bot: Color.FromArgb(58, 0, 25, 55), samples: 48);

        DrawWaveLayer(g, W, H,
            baseY: H * 0.44f, amp: H * 0.070f, freq: 1.10f, speed: 0.40f, phase: 1.4f,
            top: Color.FromArgb(42, 15, 110, 170), bot: Color.FromArgb(72, 0, 35, 70), samples: 48);

        DrawWaveLayer(g, W, H,
            baseY: H * 0.58f, amp: H * 0.075f, freq: 1.05f, speed: 0.48f, phase: 2.1f,
            top: Color.FromArgb(48, 25, 160, 210), bot: Color.FromArgb(80, 0, 45, 85), samples: 52);

        DrawWaveLayer(g, W, H,
            baseY: H * 0.72f, amp: H * 0.055f, freq: 1.45f, speed: 0.56f, phase: 0.8f,
            top: Color.FromArgb(38, 70, 200, 230), bot: Color.FromArgb(65, 0, 55, 100), samples: 48);

        DrawSpecularCrests(g, W, H);
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
        using (var br = new LinearGradientBrush(
                   new RectangleF(0, topY, W, Math.Max(8, botY - topY)),
                   top, bot, 90f))
        {
            g.FillPath(br, path);
        }
    }

    private void DrawSpecularCrests(Graphics g, int W, int H)
    {
        int samples = 40;
        for (int layer = 0; layer < 2; layer++)
        {
            float baseY = H * (0.42f + layer * 0.14f);
            float amp = H * (0.055f + layer * 0.015f);
            float speed = 0.36f + layer * 0.08f;
            float phase = layer * 1.1f + 0.4f;
            int alpha = 45 + layer * 22;

            PointF prev = default;
            using var penMain = new Pen(Color.FromArgb(alpha, 200, 240, 255), 1.6f + layer * 0.4f);
            using var penHi = new Pen(Color.FromArgb(alpha / 2, 255, 255, 255), 1f);
            for (int i = 0; i <= samples; i++)
            {
                float nx = i / (float)samples;
                float x = nx * W;
                float y = baseY
                          + amp * (float)Math.Sin(nx * Math.PI * 2 * 1.1 + _t * speed + phase)
                          + amp * 0.35f * (float)Math.Sin(nx * Math.PI * 2 * 1.9 + _t * speed * 1.15f);
                var p = new PointF(x, y);
                if (i > 0)
                {
                    g.DrawLine(penMain, prev, p);
                    g.DrawLine(penHi, prev.X, prev.Y - 1.2f, p.X, p.Y - 1.2f);
                }

                prev = p;
            }
        }
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

    private static void DrawGlassPanel(Graphics g, Rectangle box)
    {
        for (int i = 5; i >= 1; i--)
        {
            using var path = RoundRect(box.X + i, box.Y + i + 2, box.Width, box.Height, 16);
            using var br = new SolidBrush(Color.FromArgb(12 + i * 8, 0, 0, 0));
            g.FillPath(br, path);
        }

        using (var path = RoundRect(box.X, box.Y, box.Width, box.Height, 16))
        using (var br = new LinearGradientBrush(box,
                   Color.FromArgb(200, 0, 28, 40),
                   Color.FromArgb(215, 0, 10, 18), 95f))
        {
            g.FillPath(br, path);
        }

        var hi = new Rectangle(box.X + 10, box.Y + 8, box.Width - 20, box.Height / 2);
        using (var path = RoundRect(hi.X, hi.Y, hi.Width, hi.Height, 12))
        using (var br = new LinearGradientBrush(hi,
                   Color.FromArgb(55, 200, 230, 255),
                   Color.FromArgb(0, 255, 255, 255), 90f))
        {
            g.FillPath(br, path);
        }

        using (var path = RoundRect(box.X, box.Y, box.Width, box.Height, 16))
        using (var pen = new Pen(Color.FromArgb(150, Green), 2f))
        {
            g.DrawPath(pen, path);
        }

        using (var pen = new Pen(Color.FromArgb(160, Red), 2f))
        {
            g.DrawLine(pen, box.X + 28, box.Y + 3, box.Right - 28, box.Y + 3);
        }
    }

    private void DrawContent(Graphics g, Rectangle box, bool blink)
    {
        int pad = 32;
        int x = box.X + pad;
        int y = box.Y + pad;
        int innerW = box.Width - pad * 2;

        var logo = new Rectangle(x, y, 70, 70);
        using (var br = new SolidBrush(Color.FromArgb(180, 0, 40, 30)))
        {
            g.FillEllipse(br, logo);
        }

        using (var pen = new Pen(Green, 2f))
        {
            g.DrawEllipse(pen, logo);
        }

        if (_logo != null)
        {
            var lr = Rectangle.Inflate(logo, -10, -10);
            using (var clip = new GraphicsPath())
            {
                clip.AddEllipse(lr);
                g.SetClip(clip);
                g.DrawImage(_logo, lr);
                g.ResetClip();
            }
        }
        else
        {
            using var f = new Font("Segoe UI", 18f, FontStyle.Bold);
            using var b = new SolidBrush(White);
            g.DrawString("TR", f, b, logo.X + 16, logo.Y + 20);
        }

        using (var f = new Font("Segoe UI", 30f, FontStyle.Bold))
        using (var b = new SolidBrush(White))
        {
            g.DrawString("TURBORAMA", f, b, x + 88, y + 6);
        }

        using (var f = new Font("Segoe UI", 11f, FontStyle.Bold))
        using (var b = new SolidBrush(Green))
        {
            g.DrawString("ARCADE", f, b, x + 90, y + 48);
        }

        using (var f = new Font("Consolas", 12f, FontStyle.Bold))
        using (var b = new SolidBrush(Color.FromArgb(200, 200, 220, 220)))
        {
            g.DrawString(_status + (blink && !_finalPhase ? "  _" : ""), f, b, x, y + 96);
        }

        using (var f = new Font("Segoe UI", 12f))
        using (var b = new SolidBrush(White))
        {
            g.DrawString(_detail, f, b, x, y + 122);
        }

        using (var f = new Font("Consolas", 22f, FontStyle.Bold))
        using (var b = new SolidBrush(Amber))
        {
            string p = _progress.ToString("00") + "%";
            SizeF sz = g.MeasureString(p, f);
            g.DrawString(p, f, b, box.Right - pad - sz.Width, y + 100);
        }

        var bar = new Rectangle(x, y + 158, innerW, 24);
        float barValue = _progressF > 0.001f ? _progressF : _progress / 100f;
        DrawBar(g, bar, barValue);

        using (var f = new Font("Segoe UI", 9f))
        using (var b = new SolidBrush(Color.FromArgb(140, 180, 200, 200)))
        {
            g.DrawString(_footer, f, b, x, box.Bottom - 36);
        }
    }

    private void DrawBar(Graphics g, Rectangle bar, float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        float pulse = 0.55f + 0.45f * (float)Math.Sin(_t * 2.8);
        float pulseFast = 0.5f + 0.5f * (float)Math.Sin(_t * 5.5);

        int aura = (int)(16 + 22 * pulse);
        using (var path = RoundRect(bar.X - 6, bar.Y - 6, bar.Width + 12, bar.Height + 12, 14))
        using (var br = new SolidBrush(Color.FromArgb(aura, 0, 200, 140)))
        {
            g.FillPath(br, path);
        }

        using (var path = RoundRect(bar.X + 2, bar.Y + 4, bar.Width, bar.Height, 12))
        using (var br = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
        {
            g.FillPath(br, path);
        }

        using (var path = RoundRect(bar.X, bar.Y, bar.Width, bar.Height, 12))
        using (var br = new LinearGradientBrush(bar,
                   Color.FromArgb(255, 2, 14, 22),
                   Color.FromArgb(255, 8, 28, 38), 90f))
        {
            g.FillPath(br, path);
        }

        using (var path = RoundRect(bar.X, bar.Y, bar.Width, bar.Height, 12))
        using (var pen = new Pen(Color.FromArgb((int)(100 + 80 * pulse), 40, 200, 180), 1.5f))
        {
            g.DrawPath(pen, path);
        }

        using (var path = RoundRect(bar.X + 2, bar.Y + 2, bar.Width - 4, bar.Height - 4, 10))
        using (var pen = new Pen(Color.FromArgb(55, 120, 220, 230), 1f))
        {
            g.DrawPath(pen, path);
        }

        int pad = 4;
        int ix = bar.X + pad;
        int iy = bar.Y + pad;
        int iw = bar.Width - pad * 2;
        int ih = bar.Height - pad * 2;

        using (var path = RoundRect(ix, iy, iw, ih, 8))
        using (var br = new SolidBrush(Color.FromArgb(255, 0, 18, 26)))
        {
            g.FillPath(br, path);
        }

        if (value > 0.001f)
        {
            int fw = Math.Max(8, (int)Math.Round(iw * value));
            fw = Math.Min(fw, iw);
            var fill = new Rectangle(ix, iy, fw, ih);

            using (var path = RoundRect(fill.X, fill.Y, fill.Width, fill.Height, 8))
            {
                using var br = new LinearGradientBrush(fill,
                    Color.FromArgb(255, 0, 70, 45),
                    Color.FromArgb(255, 40, 255, 170), 0f);
                var blend = new ColorBlend(3)
                {
                    Colors =
                    [
                        Color.FromArgb(255, 0, 80, 50),
                        Color.FromArgb(255, 0, 220, 110),
                        Color.FromArgb(255, 60, 255, 200)
                    ],
                    Positions = [0f, 0.55f, 1f]
                };
                br.InterpolationColors = blend;
                g.FillPath(br, path);
            }

            if (fill.Width > 12)
            {
                var hi = new Rectangle(fill.X + 3, fill.Y + 2, fill.Width - 6, Math.Max(2, ih / 2 - 1));
                using var path = RoundRect(hi.X, hi.Y, hi.Width, Math.Max(2, hi.Height), 6);
                using var br = new LinearGradientBrush(hi,
                    Color.FromArgb((int)(85 + 40 * pulse), 255, 255, 255),
                    Color.FromArgb(0, 255, 255, 255), 90f);
                g.FillPath(br, path);
            }

            float shineT = (_t * 0.45f) % 1.2f;
            if (shineT <= 1f && fill.Width > 20)
            {
                int maxTravel = Math.Max(1, fill.Width - 40);
                int shineX = fill.X + (int)(maxTravel * shineT);
                int shineW = Math.Min(48, fill.Right - shineX);
                if (shineW > 8)
                {
                    var shine = new Rectangle(shineX, fill.Y, shineW, fill.Height);
                    using var path = RoundRect(shine.X, shine.Y, shine.Width, shine.Height, 6);
                    using var br = new LinearGradientBrush(shine,
                        Color.FromArgb(0, 255, 255, 255),
                        Color.FromArgb((int)(100 + 50 * pulseFast), 255, 255, 255), 0f);
                    var cb = new ColorBlend(3)
                    {
                        Colors =
                        [
                            Color.FromArgb(0, 255, 255, 255),
                            Color.FromArgb((int)(110 + 40 * pulseFast), 255, 255, 255),
                            Color.FromArgb(0, 255, 255, 255)
                        ],
                        Positions = [0f, 0.5f, 1f]
                    };
                    br.InterpolationColors = cb;
                    g.SetClip(path);
                    g.FillRectangle(br, shine);
                    g.ResetClip();
                }
            }

            int tipX = fill.Right;
            int tipY = fill.Y + fill.Height / 2;

            using (var br = new SolidBrush(Color.FromArgb((int)(35 + 45 * pulse), 0, 255, 160)))
            {
                g.FillEllipse(br, tipX - 18, tipY - 14, 30, 28);
            }

            using (var pen = new Pen(Color.FromArgb((int)(160 + 60 * pulseFast), 120, 255, 200), 1.5f))
            {
                g.DrawEllipse(pen, tipX - 7, tipY - 7, 14, 14);
            }

            using (var br = new SolidBrush(Color.FromArgb(230, Red)))
            {
                g.FillEllipse(br, tipX - 4, tipY - 4, 8, 8);
            }

            using (var br = new SolidBrush(Color.FromArgb((int)(180 + 50 * pulseFast), 255, 255, 255)))
            {
                g.FillEllipse(br, tipX - 2, tipY - 2, 4, 4);
            }

            using (var pen = new Pen(Color.FromArgb((int)(200 + 50 * pulse), 255, 80, 80), 2f))
            {
                g.DrawLine(pen, tipX - 1, fill.Y + 1, tipX - 1, fill.Bottom - 1);
            }
        }

        using (var pen = new Pen(Color.FromArgb(40, 0, 0, 0), 1f))
        {
            int n = 20;
            for (int i = 1; i < n; i++)
            {
                int lx = ix + (iw * i) / n;
                g.DrawLine(pen, lx, iy + 1, lx, iy + ih - 1);
            }
        }

        using (var pen = new Pen(Color.FromArgb(110, 100, 210, 220), 1f))
        {
            for (int q = 1; q < 4; q++)
            {
                int mx = bar.X + (bar.Width * q) / 4;
                g.DrawLine(pen, mx, bar.Y - 3, mx, bar.Y);
                g.DrawLine(pen, mx, bar.Bottom, mx, bar.Bottom + 3);
            }
        }

        using (var f = new Font("Segoe UI", 8f, FontStyle.Bold))
        using (var b = new SolidBrush(Color.FromArgb(160, 120, 220, 230)))
        {
            // Rótulo carinhoso em vez de LOAD técnico
            g.DrawString("BYE", f, b, bar.X, bar.Y - 14);
            string pct = ((int)Math.Round(value * 100)).ToString("00") + "%";
            SizeF sz = g.MeasureString(pct, f);
            g.DrawString(pct, f, b, bar.Right - sz.Width, bar.Y - 14);
        }
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

    /// <summary>
    /// Só assets de DESLIGAR. Nunca usa boot.wav nem o caminho “só” de boot como principal.
    /// Ficheiro canónico: Assets\logo-shutdown.png (separado de Assets\logo.png).
    /// </summary>
    private static Image? LoadShutdownLogo()
    {
        EnsureShutdownLogoFileExists();

        string[] paths =
        {
            ProductPaths.DefaultShutdownLogoPng, // Assets\logo-shutdown.png
            Path.Combine(AppContext.BaseDirectory, "Assets", "logo-shutdown.png"),
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

    /// <summary>
    /// Garante que logo-shutdown.png existe como ficheiro separado.
    /// Se ainda não existir, cria uma cópia inicial a partir de logo.png (editável à parte).
    /// </summary>
    private static void EnsureShutdownLogoFileExists()
    {
        try
        {
            string shut = ProductPaths.DefaultShutdownLogoPng;
            if (File.Exists(shut))
            {
                return;
            }

            string dir = Path.GetDirectoryName(shut) ?? "";
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string bootLogo = ProductPaths.DefaultBootLogoPng;
            string localBoot = Path.Combine(AppContext.BaseDirectory, "Assets", "logo.png");
            string? src = File.Exists(bootLogo) ? bootLogo
                : File.Exists(localBoot) ? localBoot
                : null;
            if (src != null)
            {
                File.Copy(src, shut, overwrite: false);
            }

            // Cópia ao lado do EXE também (publish)
            string localShut = Path.Combine(AppContext.BaseDirectory, "Assets", "logo-shutdown.png");
            if (!File.Exists(localShut) && src != null)
            {
                string? localDir = Path.GetDirectoryName(localShut);
                if (!string.IsNullOrEmpty(localDir))
                {
                    Directory.CreateDirectory(localDir);
                }

                File.Copy(src, localShut, overwrite: false);
            }
        }
        catch
        {
            // ignore
        }
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
