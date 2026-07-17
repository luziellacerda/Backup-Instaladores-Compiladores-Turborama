using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using TurboRama.Configuration;
using TurboRama.Core.Paths;

namespace TurboRama.Launcher;

/// <summary>
/// Tela de loading TURBORAMA após logon Arcade (shell).
/// Fica visível e TopMost ANTES de abrir o jogo — preenche o ecrã preto do shell.
/// </summary>
internal sealed class LoadingScreenForm : Form
{
    private readonly Panel _card;
    private readonly Label _title;
    private readonly Label _subtitle;
    private readonly Label _status;
    private readonly Label _percent;
    private readonly Panel _barHost;
    private readonly System.Windows.Forms.Timer _animTimer;
    private Image? _logo;
    private int _progress;

    public LoadingScreenForm(ProductConfiguration config)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        WindowState = FormWindowState.Normal;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        DoubleBuffered = true;
        BackColor = Color.Black;
        ForeColor = Color.White;
        // Tamanho real do monitor primário
        Rectangle screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        Bounds = screen;
        Location = screen.Location;
        Size = screen.Size;

        _card = new Panel
        {
            Width = 860,
            Height = 420,
            BackColor = Color.FromArgb(22, 28, 38),
            BorderStyle = BorderStyle.FixedSingle
        };
        Controls.Add(_card);

        _logo = LoadLogoImage();
        if (_logo != null)
        {
            var pic = new PictureBox
            {
                Image = _logo,
                SizeMode = PictureBoxSizeMode.Zoom,
                Width = 110,
                Height = 110,
                Left = 48,
                Top = 40,
                BackColor = Color.FromArgb(14, 18, 24)
            };
            _card.Controls.Add(pic);
        }
        else
        {
            _card.Controls.Add(new Label
            {
                Text = "TR",
                Font = new Font("Segoe UI", 36f, FontStyle.Bold),
                ForeColor = Color.FromArgb(80, 255, 150),
                AutoSize = false,
                Width = 110,
                Height = 110,
                Left = 48,
                Top = 40,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(14, 18, 24)
            });
        }

        _title = new Label
        {
            Text = "TURBORAMA",
            Font = new Font("Segoe UI", 42f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = false,
            Width = 620,
            Height = 64,
            Left = 180,
            Top = 48,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _card.Controls.Add(_title);

        _subtitle = new Label
        {
            Text = "ARCADE",
            Font = new Font("Segoe UI", 18f, FontStyle.Bold),
            ForeColor = Color.FromArgb(70, 230, 140),
            AutoSize = false,
            Width = 620,
            Height = 36,
            Left = 180,
            Top = 112
        };
        _card.Controls.Add(_subtitle);

        _card.Controls.Add(new Panel
        {
            BackColor = Color.FromArgb(50, 220, 130),
            Width = 180,
            Height = 4,
            Left = 180,
            Top = 156
        });

        _status = new Label
        {
            Text = "A iniciar...",
            Font = new Font("Segoe UI", 14f, FontStyle.Regular),
            ForeColor = Color.FromArgb(200, 210, 220),
            AutoSize = false,
            Width = 760,
            Height = 36,
            Left = 48,
            Top = 200
        };
        _card.Controls.Add(_status);

        _percent = new Label
        {
            Text = "0%",
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            ForeColor = Color.FromArgb(70, 230, 140),
            AutoSize = false,
            Width = 100,
            Height = 32,
            Left = 708,
            Top = 260,
            TextAlign = ContentAlignment.MiddleRight
        };
        _card.Controls.Add(_percent);

        _barHost = new Panel
        {
            Width = 760,
            Height = 22,
            Left = 48,
            Top = 300,
            BackColor = Color.FromArgb(40, 48, 60)
        };
        _barHost.Paint += BarHost_Paint;
        _card.Controls.Add(_barHost);

        _card.Controls.Add(new Label
        {
            Text = "Console arcade — aguarde",
            Font = new Font("Segoe UI", 11f, FontStyle.Regular),
            ForeColor = Color.FromArgb(130, 140, 150),
            AutoSize = false,
            Width = 760,
            Height = 28,
            Left = 48,
            Top = 350
        });

        _animTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _animTimer.Tick += (_, _) =>
        {
            // Mantém TopMost (outro processo não deve tapar durante a marca)
            ForceForeground();
            _barHost.Invalidate();
        };

        Load += (_, _) => Relayout();
        Shown += (_, _) =>
        {
            Relayout();
            ForceForeground();
            Refresh();
        };
        Resize += (_, _) => Relayout();
    }

    /// <summary>
    /// Mostra a marca TURBORAMA e mantém o ecrã o tempo mínimo — SEM abrir o jogo.
    /// Garante paint + message pump (corrige ecrã preto).
    /// </summary>
    public void ShowBrandHold(int minMs, Action<int, string>? onTick = null)
    {
        if (minMs < 3000)
        {
            minMs = 3000;
        }

        // Ecrã completo
        Rectangle screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        Bounds = screen;
        Show();
        WindowState = FormWindowState.Normal;
        Relayout();
        ForceForeground();
        _progress = 5;
        ApplyProgressUi();
        _status.Text = "TURBORAMA Arcade";
        _animTimer.Start();

        // Força pintura inicial (várias voltas DoEvents)
        for (int i = 0; i < 8; i++)
        {
            Invalidate(true);
            Update();
            Refresh();
            Application.DoEvents();
            Thread.Sleep(20);
        }

        var sw = Stopwatch.StartNew();
        string[] phases =
        {
            "A carregar sistema...",
            "A preparar consola arcade...",
            "TURBORAMA a iniciar...",
            "Quase pronto..."
        };

        while (sw.ElapsedMilliseconds < minMs)
        {
            double t = sw.ElapsedMilliseconds / (double)minMs;
            int p = 5 + (int)(90 * Math.Min(1.0, t));
            _progress = Math.Min(95, p);
            ApplyProgressUi();

            int phaseIdx = Math.Min(phases.Length - 1, (int)(t * phases.Length));
            _status.Text = phases[phaseIdx];
            onTick?.Invoke(_progress, _status.Text);

            // Reafirmar TopMost periodicamente
            if (sw.ElapsedMilliseconds % 400 < 40)
            {
                ForceForeground();
            }

            Application.DoEvents();
            Thread.Sleep(30);
        }

        _progress = 100;
        _status.Text = "A iniciar o jogo...";
        ApplyProgressUi();
        ForceForeground();
        Application.DoEvents();
        Thread.Sleep(250);
    }

    public void SetStatus(string text)
    {
        if (IsDisposed)
        {
            return;
        }

        _status.Text = text ?? "";
        Application.DoEvents();
    }

    public void SetProgress(int value)
    {
        _progress = Math.Clamp(value, 0, 100);
        ApplyProgressUi();
        Application.DoEvents();
    }

    public void HideLoading()
    {
        try
        {
            _animTimer.Stop();
            _progress = 100;
            ApplyProgressUi();
            Application.DoEvents();
            Hide();
        }
        catch
        {
        }
    }

    private void Relayout()
    {
        if (_card == null || IsDisposed)
        {
            return;
        }

        _card.Left = Math.Max(0, (ClientSize.Width - _card.Width) / 2);
        _card.Top = Math.Max(0, (ClientSize.Height - _card.Height) / 2);
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
            // HWND_TOPMOST
            SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        }
        catch
        {
        }
    }

    private void ApplyProgressUi()
    {
        if (IsDisposed)
        {
            return;
        }

        _percent.Text = _progress + "%";
        _barHost.Invalidate();
    }

    private void BarHost_Paint(object? sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle bounds = _barHost.ClientRectangle;
        using (var bg = new SolidBrush(Color.FromArgb(40, 48, 60)))
        {
            g.FillRectangle(bg, bounds);
        }

        int w = Math.Max(0, (int)(bounds.Width * (_progress / 100.0)));
        if (w > 0)
        {
            using var brush = new LinearGradientBrush(
                new Rectangle(0, 0, Math.Max(1, w), bounds.Height),
                Color.FromArgb(40, 200, 120),
                Color.FromArgb(120, 255, 180),
                LinearGradientMode.Horizontal);
            g.FillRectangle(brush, 0, 0, w, bounds.Height);
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
                    byte[] bytes = File.ReadAllBytes(p);
                    var ms = new MemoryStream(bytes);
                    return Image.FromStream(ms);
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
            // WS_EX_TOPMOST | WS_EX_TOOLWINDOW (sem botão na barra)
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
