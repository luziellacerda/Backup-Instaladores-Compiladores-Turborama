using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using TurboRama.Configuration;
using TurboRama.Core.Paths;

namespace TurboRama.Launcher;

/// <summary>
/// Painel de operador TurboRama (Alt+End).
/// Versão estável sem animação de repaint (evita ecrã a “piscar” / alternar cores).
/// </summary>
internal sealed class OperatorConsoleForm : Form
{
    private static readonly Color Black = Color.FromArgb(2, 6, 12);
    private static readonly Color PanelBg = Color.FromArgb(8, 22, 32);
    private static readonly Color PanelBg2 = Color.FromArgb(4, 12, 20);
    private static readonly Color Green = Color.FromArgb(0, 220, 100);
    private static readonly Color White = Color.FromArgb(245, 255, 250);
    private static readonly Color Red = Color.FromArgb(220, 45, 50);
    private static readonly Color Amber = Color.FromArgb(255, 200, 70);
    private static readonly Color Muted = Color.FromArgb(140, 160, 170);

    public enum OperatorAction
    {
        None = 0,
        ResumeArcade,
        OpenDesktop,
        RebootMachine,
        PowerOffMachine,
    }

    public OperatorAction ChosenAction { get; private set; } = OperatorAction.None;

    private readonly string _expectedPin;
    private readonly TextBox _pinBox;
    private readonly Label _status;
    private readonly Panel _card;
    private Image? _logo;

    public OperatorConsoleForm(string expectedPin)
    {
        _expectedPin = expectedPin ?? "";

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        TopMost = true;
        ShowInTaskbar = false;
        KeyPreview = true;
        DoubleBuffered = true;
        // Fundo SÓLIDO — sem gradiente animado no form (causa principal do piscar)
        BackColor = Black;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
        UpdateStyles();

        _logo = TryLoadLogo();

        int cardW = Math.Min(720, (int)(Bounds.Width * 0.55));
        int cardH = Math.Min(460, (int)(Bounds.Height * 0.58));
        _card = new Panel
        {
            Width = cardW,
            Height = cardH,
            Left = Math.Max(0, (Bounds.Width - cardW) / 2),
            Top = Math.Max(0, (Bounds.Height - cardH) / 2),
            // OPAQUE (nunca usar alpha baixo — provoca alternância de cores)
            BackColor = PanelBg
        };
        EnableDoubleBuffer(_card);
        _card.Paint += Card_Paint;
        Controls.Add(_card);

        var title = new Label
        {
            Text = "TURBORAMA",
            Font = new Font("Segoe UI", 26f, FontStyle.Bold),
            ForeColor = White,
            AutoSize = true,
            Left = 36,
            Top = 28,
            BackColor = PanelBg
        };
        _card.Controls.Add(title);

        var badge = new Label
        {
            Text = "SEGURANÇA DO SISTEMA  ·  CTRL+END  (substitui Ctrl+Alt+Del)",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Green,
            AutoSize = true,
            Left = 38,
            Top = 72,
            BackColor = PanelBg
        };
        _card.Controls.Add(badge);

        var pinLbl = new Label
        {
            Text = "PIN de acesso",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Muted,
            AutoSize = true,
            Left = 36,
            Top = 110,
            BackColor = PanelBg
        };
        _card.Controls.Add(pinLbl);

        _pinBox = new TextBox
        {
            Left = 36,
            Top = 132,
            Width = Math.Min(280, cardW - 72),
            Font = new Font("Segoe UI", 14f),
            UseSystemPasswordChar = true,
            BackColor = Color.FromArgb(12, 24, 32),
            ForeColor = White,
            BorderStyle = BorderStyle.FixedSingle
        };
        _pinBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                ChosenAction = OperatorAction.None;
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };
        _card.Controls.Add(_pinBox);

        _status = new Label
        {
            Text = "Digite o PIN para ações técnicas. Esc cancela.",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Muted,
            AutoSize = false,
            Width = cardW - 72,
            Height = 28,
            Left = 36,
            Top = 172,
            BackColor = PanelBg
        };
        _card.Controls.Add(_status);

        int y = 220;
        int bw = (cardW - 36 * 2 - 12) / 2;
        int bh = 40;

        AddBtn("VOLTAR AO ARCADE", 36, y, bw, bh, OperatorAction.ResumeArcade, needPin: false);
        AddBtn("ABRIR WINDOWS", 36 + bw + 12, y, bw, bh, OperatorAction.OpenDesktop, needPin: true);
        y += bh + 12;
        AddBtn("REINICIAR PC", 36, y, bw, bh, OperatorAction.RebootMachine, needPin: true);
        AddBtn("DESLIGAR PC", 36 + bw + 12, y, bw, bh, OperatorAction.PowerOffMachine, needPin: true);
        y += bh + 12;
        AddBtn("CANCELAR", 36, y, cardW - 72, bh, OperatorAction.None, needPin: false, muted: true);

        var footer = new Label
        {
            Text = "Ctrl+Alt+Del desativado  ·  use Ctrl+End  ·  PIN obrigatório nas ações técnicas",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.FromArgb(120, 140, 150),
            AutoSize = true,
            Left = 36,
            Top = cardH - 36,
            BackColor = PanelBg
        };
        _card.Controls.Add(footer);

        // SEM timer de animação — era o que fazia a tela piscar / alternar cores

        Shown += (_, _) =>
        {
            TopMost = true;
            BringToFront();
            _pinBox.Focus();
        };
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                ChosenAction = OperatorAction.None;
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };
    }

    private static void EnableDoubleBuffer(Control c)
    {
        try
        {
            typeof(Control).InvokeMember(
                "DoubleBuffered",
                BindingFlags.SetProperty | BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                c,
                new object[] { true });
        }
        catch
        {
            // ignore
        }
    }

    private void AddBtn(string text, int x, int y, int w, int h, OperatorAction action, bool needPin, bool muted = false)
    {
        var btn = new Button
        {
            Text = text,
            Left = x,
            Top = y,
            Width = w,
            Height = h,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = muted ? Muted : White,
            BackColor = muted
                ? Color.FromArgb(28, 32, 38)
                : action == OperatorAction.PowerOffMachine
                    ? Color.FromArgb(90, 28, 32)
                    : action == OperatorAction.ResumeArcade
                        ? Color.FromArgb(0, 80, 45)
                        : Color.FromArgb(0, 48, 62),
            Cursor = Cursors.Hand,
            TabStop = true
        };
        btn.FlatAppearance.BorderColor = action == OperatorAction.PowerOffMachine ? Red : Green;
        btn.FlatAppearance.BorderSize = 1;
        btn.Click += (_, _) => OnAction(action, needPin);
        _card.Controls.Add(btn);
    }

    private void OnAction(OperatorAction action, bool needPin)
    {
        if (action == OperatorAction.None)
        {
            ChosenAction = OperatorAction.None;
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        if (action == OperatorAction.ResumeArcade)
        {
            ChosenAction = OperatorAction.ResumeArcade;
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        if (needPin)
        {
            if (string.IsNullOrEmpty(_expectedPin))
            {
                _status.ForeColor = Amber;
                _status.Text = "PIN não configurado. Use a conta Admin para recuperar.";
                return;
            }

            if (!string.Equals(_pinBox.Text, _expectedPin, StringComparison.Ordinal))
            {
                _status.ForeColor = Red;
                _status.Text = "PIN incorreto.";
                _pinBox.Clear();
                _pinBox.Focus();
                return;
            }
        }

        ChosenAction = action;
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Evita erase/paint default que provoca flash
        e.Graphics.Clear(Black);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // Fundo estático sólido (sem gradiente animado)
        e.Graphics.Clear(Black);
    }

    private void Card_Paint(object? sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var r = _card.ClientRectangle;

        // Preenchimento opaco estável
        using (var br = new LinearGradientBrush(r, PanelBg, PanelBg2, 95f))
        {
            g.FillRectangle(br, r);
        }

        using (var path = RoundRect(1, 1, r.Width - 3, r.Height - 3, 12))
        using (var pen = new Pen(Color.FromArgb(180, Green), 2f))
        {
            g.DrawPath(pen, path);
        }

        using (var pen = new Pen(Color.FromArgb(200, Red), 2f))
        {
            g.DrawLine(pen, 24, 3, r.Width - 24, 3);
        }

        if (_logo != null)
        {
            try
            {
                g.DrawImage(_logo, r.Width - 88, 24, 56, 56);
            }
            catch
            {
                // ignore
            }
        }
    }

    private static GraphicsPath RoundRect(int x, int y, int w, int h, int radius)
    {
        int d = Math.Max(1, radius * 2);
        var p = new GraphicsPath();
        p.AddArc(x, y, d, d, 180, 90);
        p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        p.AddArc(x, y + h - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static Image? TryLoadLogo()
    {
        try
        {
            string[] paths =
            {
                ProductPaths.DefaultBootLogoPng,
                Path.Combine(AppContext.BaseDirectory, "Assets", "logo.png"),
            };
            foreach (string path in paths)
            {
                if (File.Exists(path))
                {
                    return Image.FromStream(new MemoryStream(File.ReadAllBytes(path)));
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _logo?.Dispose();
        }

        base.Dispose(disposing);
    }

    public static void ExecuteAction(OperatorAction action, Action? onResumeArcade = null)
    {
        switch (action)
        {
            case OperatorAction.ResumeArcade:
                onResumeArcade?.Invoke();
                break;

            case OperatorAction.OpenDesktop:
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        UseShellExecute = true
                    });
                }
                catch
                {
                    // ignore
                }

                break;

            case OperatorAction.RebootMachine:
                PowerShutdownHelper.RebootNow(out _, null);
                break;

            case OperatorAction.PowerOffMachine:
                PowerShutdownHelper.ShutdownNow(out _, null);
                break;
        }
    }
}
