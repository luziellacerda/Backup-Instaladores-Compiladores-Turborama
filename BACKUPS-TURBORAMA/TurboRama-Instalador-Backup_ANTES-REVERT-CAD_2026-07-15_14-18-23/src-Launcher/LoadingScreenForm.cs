using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using TurboRama.Configuration;
using TurboRama.Core.Paths;

namespace TurboRama.Launcher;

/// <summary>
/// LOADING / BOOT inicial — ficheiro próprio: LoadingScreenForm.cs
/// NÃO é a tela de desligar (essa é ShutdownScreenForm.cs).
/// Assets: Assets\logo.png + Assets\boot.wav
/// 5 modelos de boas-vindas (aleatório a cada boot).
/// Preview: --test-loading
/// </summary>
internal sealed class LoadingScreenForm : Form
{
    private static readonly Color Black = Color.FromArgb(1, 3, 8);
    private static readonly Color Deep = Color.FromArgb(0, 12, 32);
    private static readonly Color DeepMid = Color.FromArgb(0, 20, 48);
    private static readonly Color Green = Color.FromArgb(0, 230, 100);
    private static readonly Color GreenMid = Color.FromArgb(0, 170, 70);
    private static readonly Color GreenDark = Color.FromArgb(0, 60, 28);
    private static readonly Color White = Color.FromArgb(245, 255, 250);
    private static readonly Color Red = Color.FromArgb(230, 40, 45);
    private static readonly Color Amber = Color.FromArgb(255, 210, 80);

    /// <summary>Evita repetir o mesmo modelo de boas-vindas duas vezes seguidas.</summary>
    private static int _lastWelcomeIndex = -1;

    /// <summary>5 modelos de boas-vindas — cliente chega sempre com um sorriso.</summary>
    private static readonly WelcomeModel[] WelcomeModels =
    {
        // 1 — Amigo
        new(
            Name: "Amigo",
            Steps:
            [
                ("BEM-VINDO", "Que bom ter voce de volta!"),
                ("OI", "Prepara o sorriso — a diversao comeca!"),
                ("TURBORAMA", "Seu arcade favorito esta a carregar..."),
                ("QUASE", "Ja quase! Segura a empolgacao!"),
                ("VAMOS", "Hora de jogar — boa sorte!")
            ],
            FinalStatus: "BEM-VINDO",
            FinalDetail: "Divirta-se no TURBORAMA!",
            Footer: "TURBORAMA  ·  Bem-vindo ao arcade"),

        // 2 — Festa / energia
        new(
            Name: "Festa",
            Steps:
            [
                ("OLA", "O fliperama acendeu por sua causa!"),
                ("FESTA", "Hoje e dia de pontuacao alta!"),
                ("ENERGIA", "Carregando muita energia positiva..."),
                ("SHOW", "Quase la — o show vai comecar!"),
                ("GO", "Pode comecar — e so alegria!")
            ],
            FinalStatus: "VAMOS LA",
            FinalDetail: "A festa e sua. Bom jogo!",
            Footer: "TURBORAMA  ·  Diversao e boa energia"),

        // 3 — Campeão
        new(
            Name: "Campeao",
            Steps:
            [
                ("CAMPEAO", "O campeao chegou no TURBORAMA!"),
                ("LENDA", "Prepara-te para novas vitorias..."),
                ("RECORDE", "Hoje pode ser dia de recorde!"),
                ("FOCO", "Mira no top — tu consegues!"),
                ("START", "Boa sorte, lenda do arcade!")
            ],
            FinalStatus: "CAMPEAO",
            FinalDetail: "Mostre o seu melhor. Bom jogo!",
            Footer: "TURBORAMA  ·  Aqui todo jogador e campeao"),

        // 4 — Carinho / familia
        new(
            Name: "Carinho",
            Steps:
            [
                ("BEM-VINDO", "Seja muito bem-vindo(a)!"),
                ("CASA", "Sinta-se em casa no nosso arcade..."),
                ("CARINHO", "Preparando tudo com carinho para voce!"),
                ("QUASE", "So mais um instante..."),
                ("PRONTO", "Pode jogar — estamos com voce!")
            ],
            FinalStatus: "BEM-VINDO",
            FinalDetail: "Obrigado por escolher o TURBORAMA!",
            Footer: "TURBORAMA  ·  Feito com carinho para voce"),

        // 5 — Aventura
        new(
            Name: "Aventura",
            Steps:
            [
                ("AVENTURA", "Uma nova aventura esta a comecar!"),
                ("MUNDO", "Mundos e jogos a carregar..."),
                ("MAGIA", "A magia do arcade a despertar..."),
                ("PORTA", "A porta dos jogos esta a abrir..."),
                ("ENTRE", "Entre e divirta-se sem limites!")
            ],
            FinalStatus: "AVENTURA",
            FinalDetail: "A aventura espera por voce!",
            Footer: "TURBORAMA  ·  Cada partida e uma aventura")
    };

    private readonly System.Windows.Forms.Timer _animTimer;
    private Image? _logo;
    private int _progress;
    /// <summary>Progresso fino 0..1 — atualizado no TIMER (único sítio que pinta a barra).</summary>
    private float _progressF;
    private float _t;
    private string _status = "BEM-VINDO";
    private string _detail = "Bem-vindo ao TURBORAMA!";
    private string _footer = "TURBORAMA ARCADE SYSTEM";
    private readonly Stopwatch _sw = new();
    private int _holdMs = 5000;
    private bool _holding;
    private bool _holdFinished;
    private Action<int, string>? _holdTick;
    private WelcomeModel _welcome;

    private readonly record struct WelcomeModel(
        string Name,
        (string Status, string Detail)[] Steps,
        string FinalStatus,
        string FinalDetail,
        string Footer);

    public LoadingScreenForm(ProductConfiguration config)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = Black;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);

        Bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        _logo = LoadLogo();
        _welcome = PickWelcomeModel();
        _footer = _welcome.Footer;
        _status = _welcome.Steps[0].Status;
        _detail = _welcome.Steps[0].Detail;

        // Timer = coração do progresso: a cada tick atualiza % e redesenha
        _animTimer = new System.Windows.Forms.Timer { Interval = 30 };
        _animTimer.Tick += AnimTimer_OnTick;

        Shown += (_, _) =>
        {
            TopMost = true;
            BringToFront();
            Invalidate();
        };
    }

    private static WelcomeModel PickWelcomeModel()
    {
        int n = WelcomeModels.Length;
        int idx;
        if (n <= 1)
        {
            idx = 0;
        }
        else
        {
            do
            {
                idx = Random.Shared.Next(n);
            } while (idx == _lastWelcomeIndex && n > 1);
        }

        _lastWelcomeIndex = idx;
        return WelcomeModels[idx];
    }

    private void AnimTimer_OnTick(object? sender, EventArgs e)
    {
        // Timer só anima as ondas quando NÃO estamos no hold controlado.
        // No hold, o loop de ShowBrandHold avança _t + progresso e faz Refresh síncrono.
        if (_holding)
        {
            return;
        }

        _t += 0.03f;
        Invalidate();
    }

    /// <summary>
    /// Hold com barra sincronizada no tempo real.
    /// Progresso + Refresh no loop (não no Timer): o Launcher não tem Application.Run,
    /// então o Timer WinForms falha e a barra ficava em 00% até saltar para 100%.
    /// </summary>
    public void ShowBrandHold(int minMs, Action<int, string>? onTick = null)
    {
        minMs = Math.Clamp(minMs, 3000, 12000);
        _holdMs = minMs;
        _progress = 0;
        _progressF = 0f;
        // Novo modelo de boas-vindas a cada hold (boot / preview)
        _welcome = PickWelcomeModel();
        _footer = _welcome.Footer;
        _status = _welcome.Steps[0].Status;
        _detail = _welcome.Steps[0].Detail;
        _holdFinished = false;
        _holding = true;
        _holdTick = onTick;

        Bounds = Screen.PrimaryScreen?.Bounds ?? Bounds;
        if (!IsHandleCreated)
        {
            CreateControl();
        }

        Show();
        TopMost = true;
        BringToFront();
        Activate();

        // Timer parado durante hold — progresso 100% controlado pelo loop + Stopwatch
        try
        {
            _animTimer.Stop();
        }
        catch
        {
        }

        _sw.Restart();

        // 1º frame já visível em 00%
        try
        {
            Refresh();
        }
        catch
        {
            Invalidate();
            Update();
        }

        Application.DoEvents();

        // ~20 fps: menos repaint = menos flicker em monitores lentos
        const int frameMs = 50;
        int safety = _holdMs + 5000;
        var gate = Stopwatch.StartNew();

        while (!_holdFinished && gate.ElapsedMilliseconds < safety)
        {
            long elapsed = _sw.ElapsedMilliseconds;
            float t = Math.Min(1f, elapsed / (float)Math.Max(1, _holdMs));
            _progressF = t;
            _progress = Math.Clamp((int)Math.Round(t * 100f), 0, 100);
            ApplyText(t);

            // Animação das ondas (mais lenta = menos sensação de “piscar”)
            _t += 0.04f;

            try
            {
                _holdTick?.Invoke(_progress, _status);
            }
            catch
            {
            }

            // Refresh = paint SÍNCRONO com o % atual (Invalidate sozinho era assíncrono e falhava)
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
                }
            }

            Application.DoEvents();

            if (elapsed >= _holdMs)
            {
                ApplyFinalWelcome();
                _holdFinished = true;
                break;
            }

            // Sleep só o resto do frame (se o paint for lento, não dorme — mantém fluidez)
            int spent = (int)_sw.ElapsedMilliseconds - (int)elapsed;
            int sleep = frameMs - spent;
            if (sleep > 1)
            {
                Thread.Sleep(sleep);
            }
        }

        // Garantia final em 100%
        ApplyFinalWelcome();
        _holdFinished = true;
        _holding = false;
        _holdTick = null;

        try
        {
            Refresh();
        }
        catch
        {
            Invalidate();
            Application.DoEvents();
        }

        Application.DoEvents();
        Thread.Sleep(80);
    }

    private void ApplyFinalWelcome()
    {
        _progressF = 1f;
        _progress = 100;
        _status = _welcome.FinalStatus;
        _detail = _welcome.FinalDetail;
        _footer = _welcome.Footer;
    }

    private void ApplyText(float t)
    {
        var steps = _welcome.Steps;
        if (steps == null || steps.Length == 0)
        {
            _status = "BEM-VINDO";
            _detail = "Bem-vindo ao TURBORAMA!";
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
        _footer = _welcome.Footer;
    }

    public void SetStatus(string text)
    {
        _status = text ?? "";
        Invalidate();
        Application.DoEvents();
    }

    public void SetProgress(int value)
    {
        _progress = Math.Clamp(value, 0, 100);
        _progressF = _progress / 100f;
        Invalidate();
        Application.DoEvents();
    }

    public void HideLoading()
    {
        try
        {
            _animTimer.Stop();
            Hide();
            Application.DoEvents();
        }
        catch
        {
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        int W = Math.Max(1, ClientSize.Width);
        int H = Math.Max(1, ClientSize.Height);
        bool blink = ((int)(_t * 8)) % 2 == 0;

        // Fundo oceano profundo (PS3)
        using (var bg = new LinearGradientBrush(
                   new Rectangle(0, 0, W, H),
                   Black, DeepMid, 95f))
        {
            g.FillRectangle(bg, 0, 0, W, H);
        }

        // Luz ambiente suave no topo (como XMB)
        using (var br = new LinearGradientBrush(
                   new Rectangle(0, 0, W, H / 2),
                   Color.FromArgb(45, 20, 80, 140),
                   Color.FromArgb(0, 0, 0, 0), 90f))
        {
            g.FillRectangle(br, 0, 0, W, H / 2);
        }

        // Ondas realistas
        DrawRealisticWaves(g, W, H);

        // Reflexos de luz / caustics leves
        DrawLightShimmer(g, W, H);

        DrawVignette(g, W, H);

        // Painel central
        int boxW = Math.Min(780, (int)(W * 0.70));
        int boxH = Math.Min(340, (int)(H * 0.46));
        int bx = (W - boxW) / 2;
        int by = (H - boxH) / 2;
        var box = new Rectangle(bx, by, boxW, boxH);

        DrawGlassPanel(g, box);
        DrawContent(g, box, blink);
    }

    /// <summary>Água / XMB PS3 com profundidade, gradiente e cristas luminosas.</summary>
    private void DrawRealisticWaves(Graphics g, int W, int H)
    {
        // 4 camadas (suficiente para look PS3, paint leve o bastante para ~30 fps na barra)
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
            // 3 senoides = água mais real (não “seno perfeito”)
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

    /// <summary>
    /// Manchas de luz na água — elipses simples (sem PathGradientBrush).
    /// PathGradient era demasiado pesado e fazia o paint demorar tanto que a barra
    /// só redesenhava no início (00%) e no fim (100%).
    /// </summary>
    private void DrawLightShimmer(Graphics g, int W, int H)
    {
        // Alpha fixo e movimento lento — evita “piscar” / alternar cores
        for (int i = 0; i < 3; i++)
        {
            float phase = _t * (0.12f + i * 0.03f) + i * 1.3f;
            float cx = W * (0.20f + 0.18f * i + 0.02f * (float)Math.Sin(phase));
            float cy = H * (0.40f + 0.05f * (float)Math.Cos(phase * 0.7f));
            float rw = W * 0.10f;
            float rh = H * 0.10f;
            using var br = new SolidBrush(Color.FromArgb(16, 90, 160, 210));
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
        // sombra difusa
        for (int i = 5; i >= 1; i--)
        {
            using var path = RoundRect(box.X + i, box.Y + i + 2, box.Width, box.Height, 16);
            using var br = new SolidBrush(Color.FromArgb(12 + i * 8, 0, 0, 0));
            g.FillPath(br, path);
        }

        // vidro com tom azul do fundo (reflete a água)
        using (var path = RoundRect(box.X, box.Y, box.Width, box.Height, 16))
        using (var br = new LinearGradientBrush(box,
                   Color.FromArgb(200, 0, 28, 40),
                   Color.FromArgb(215, 0, 10, 18), 95f))
        {
            g.FillPath(br, path);
        }

        // reflexo de luz superior (vidro real)
        var hi = new Rectangle(box.X + 10, box.Y + 8, box.Width - 20, box.Height / 2);
        using (var path = RoundRect(hi.X, hi.Y, hi.Width, hi.Height, 12))
        using (var br = new LinearGradientBrush(hi,
                   Color.FromArgb(55, 200, 230, 255),
                   Color.FromArgb(0, 255, 255, 255), 90f))
        {
            g.FillPath(br, path);
        }

        // borda
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

        // logo
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

        // status
        using (var f = new Font("Consolas", 12f, FontStyle.Bold))
        using (var b = new SolidBrush(Color.FromArgb(200, 200, 220, 220)))
        {
            g.DrawString(_status + (blink ? "  _" : ""), f, b, x, y + 96);
        }

        using (var f = new Font("Segoe UI", 12f))
        using (var b = new SolidBrush(White))
        {
            g.DrawString(_detail, f, b, x, y + 122);
        }

        // percent
        using (var f = new Font("Consolas", 22f, FontStyle.Bold))
        using (var b = new SolidBrush(Amber))
        {
            string p = _progress.ToString("00") + "%";
            SizeF sz = g.MeasureString(p, f);
            g.DrawString(p, f, b, box.Right - pad - sz.Width, y + 100);
        }

        // barra elegante e viva (preenchimento contínuo no tempo)
        var bar = new Rectangle(x, y + 158, innerW, 24);
        float barValue = _progressF > 0.001f ? _progressF : _progress / 100f;
        DrawBar(g, bar, barValue);

        using (var f = new Font("Segoe UI", 9f))
        using (var b = new SolidBrush(Color.FromArgb(140, 180, 200, 200)))
        {
            g.DrawString(_footer, f, b, x, box.Bottom - 36);
        }
    }

    /// <summary>
    /// Barra elegante e viva: glow suave, fill contínuo, brilho que desliza, ponta luminosa.
    /// Progresso ao pixel (não salta). Só a barra — resto da tela igual.
    /// </summary>
    private void DrawBar(Graphics g, Rectangle bar, float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        float pulse = 0.55f + 0.45f * (float)Math.Sin(_t * 2.8);
        float pulseFast = 0.5f + 0.5f * (float)Math.Sin(_t * 5.5);

        // aura externa (respiração elegante)
        int aura = (int)(16 + 22 * pulse);
        using (var path = RoundRect(bar.X - 6, bar.Y - 6, bar.Width + 12, bar.Height + 12, 14))
        using (var br = new SolidBrush(Color.FromArgb(aura, 0, 200, 140)))
        {
            g.FillPath(br, path);
        }

        // sombra sob a barra
        using (var path = RoundRect(bar.X + 2, bar.Y + 4, bar.Width, bar.Height, 12))
        using (var br = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
        {
            g.FillPath(br, path);
        }

        // corpo / track (vidro escuro)
        using (var path = RoundRect(bar.X, bar.Y, bar.Width, bar.Height, 12))
        using (var br = new LinearGradientBrush(bar,
                   Color.FromArgb(255, 2, 14, 22),
                   Color.FromArgb(255, 8, 28, 38), 90f))
        {
            g.FillPath(br, path);
        }

        // borda exterior
        using (var path = RoundRect(bar.X, bar.Y, bar.Width, bar.Height, 12))
        using (var pen = new Pen(Color.FromArgb((int)(100 + 80 * pulse), 40, 200, 180), 1.5f))
        {
            g.DrawPath(pen, path);
        }

        // anel interno fino
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

        // canal interno
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

            // fill principal — gradiente rico (verde escuro → verde vivo → ciano)
            using (var path = RoundRect(fill.X, fill.Y, fill.Width, fill.Height, 8))
            {
                using var br = new LinearGradientBrush(fill,
                    Color.FromArgb(255, 0, 70, 45),
                    Color.FromArgb(255, 40, 255, 170), 0f);
                // blend com ponto médio
                var blend = new ColorBlend(3)
                {
                    Colors = new[]
                    {
                        Color.FromArgb(255, 0, 80, 50),
                        Color.FromArgb(255, 0, 220, 110),
                        Color.FromArgb(255, 60, 255, 200)
                    },
                    Positions = new[] { 0f, 0.55f, 1f }
                };
                br.InterpolationColors = blend;
                g.FillPath(br, path);
            }

            // camada de brilho superior (vidro / volume 3D)
            if (fill.Width > 12)
            {
                var hi = new Rectangle(fill.X + 3, fill.Y + 2, fill.Width - 6, Math.Max(2, ih / 2 - 1));
                using var path = RoundRect(hi.X, hi.Y, hi.Width, Math.Max(2, hi.Height), 6);
                using var br = new LinearGradientBrush(hi,
                    Color.FromArgb((int)(85 + 40 * pulse), 255, 255, 255),
                    Color.FromArgb(0, 255, 255, 255), 90f);
                g.FillPath(br, path);
            }

            // brilho que viaja (elegante, largo e suave)
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
                        Colors = new[]
                        {
                            Color.FromArgb(0, 255, 255, 255),
                            Color.FromArgb((int)(110 + 40 * pulseFast), 255, 255, 255),
                            Color.FromArgb(0, 255, 255, 255)
                        },
                        Positions = new[] { 0f, 0.5f, 1f }
                    };
                    br.InterpolationColors = cb;
                    g.SetClip(path);
                    g.FillRectangle(br, shine);
                    g.ResetClip();
                }
            }

            // ponta luminosa (vida na cabeça da barra)
            int tipX = fill.Right;
            int tipY = fill.Y + fill.Height / 2;

            // halo
            using (var br = new SolidBrush(Color.FromArgb((int)(35 + 45 * pulse), 0, 255, 160)))
            {
                g.FillEllipse(br, tipX - 18, tipY - 14, 30, 28);
            }

            // anel
            using (var pen = new Pen(Color.FromArgb((int)(160 + 60 * pulseFast), 120, 255, 200), 1.5f))
            {
                g.DrawEllipse(pen, tipX - 7, tipY - 7, 14, 14);
            }

            // núcleo
            using (var br = new SolidBrush(Color.FromArgb(230, Red)))
            {
                g.FillEllipse(br, tipX - 4, tipY - 4, 8, 8);
            }

            using (var br = new SolidBrush(Color.FromArgb((int)(180 + 50 * pulseFast), 255, 255, 255)))
            {
                g.FillEllipse(br, tipX - 2, tipY - 2, 4, 4);
            }

            // linha de energia na ponta
            using (var pen = new Pen(Color.FromArgb((int)(200 + 50 * pulse), 255, 80, 80), 2f))
            {
                g.DrawLine(pen, tipX - 1, fill.Y + 1, tipX - 1, fill.Bottom - 1);
            }
        }

        // divisórias suaves (elegantes, bem transparentes)
        using (var pen = new Pen(Color.FromArgb(40, 0, 0, 0), 1f))
        {
            int n = 20;
            for (int i = 1; i < n; i++)
            {
                int lx = ix + (iw * i) / n;
                g.DrawLine(pen, lx, iy + 1, lx, iy + ih - 1);
            }
        }

        // ticks de escala
        using (var pen = new Pen(Color.FromArgb(110, 100, 210, 220), 1f))
        {
            for (int q = 1; q < 4; q++)
            {
                int mx = bar.X + (bar.Width * q) / 4;
                g.DrawLine(pen, mx, bar.Y - 3, mx, bar.Y);
                g.DrawLine(pen, mx, bar.Bottom, mx, bar.Bottom + 3);
            }
        }

        // labels
        using (var f = new Font("Segoe UI", 8f, FontStyle.Bold))
        using (var b = new SolidBrush(Color.FromArgb(160, 120, 220, 230)))
        {
            g.DrawString("LOAD", f, b, bar.X, bar.Y - 14);
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

    /// <summary>Só assets de BOOT/LOADING — nunca logo-shutdown.png.</summary>
    private static Image? LoadLogo()
    {
        string[] paths =
        {
            ProductPaths.DefaultBootLogoPng, // Assets\logo.png
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
}
