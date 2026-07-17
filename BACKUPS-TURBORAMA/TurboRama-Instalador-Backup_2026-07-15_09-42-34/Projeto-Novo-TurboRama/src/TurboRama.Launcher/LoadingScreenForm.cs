using System.Drawing;
using System.Drawing.Drawing2D;
using TurboRama.Configuration;
using TurboRama.Core.Paths;

namespace TurboRama.Launcher;

/// <summary>
/// Tela de loading TurboRama após logon do utilizador Arcade (shell).
/// Não altera bolinhas de boot do Windows. Modelo visual + logo do pack estável.
/// </summary>
internal sealed class LoadingScreenForm : Form
{
    private readonly Label _title;
    private readonly Label _subtitle;
    private readonly Label _status;
    private readonly Label _percent;
    private readonly Panel _barHost;
    private readonly System.Windows.Forms.Timer _animTimer;
    private Image? _logo;
    private int _progress;
    private int _pulse;

    public LoadingScreenForm(ProductConfiguration config)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        BackColor = Color.FromArgb(6, 8, 12);
        ForeColor = Color.White;
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        KeyPreview = true;

        var card = new Panel
        {
            Width = 780,
            Height = 380,
            BackColor = Color.FromArgb(16, 20, 28),
        };
        CenterCard(card);
        Controls.Add(card);

        _logo = LoadLogoImage();
        if (_logo != null)
        {
            var pic = new PictureBox
            {
                Image = _logo,
                SizeMode = PictureBoxSizeMode.Zoom,
                Width = 96,
                Height = 96,
                Left = 40,
                Top = 36,
                BackColor = Color.Transparent
            };
            card.Controls.Add(pic);
        }
        else
        {
            var monogram = new Label
            {
                Text = "TR",
                Font = new Font("Segoe UI", 28f, FontStyle.Bold),
                ForeColor = Color.FromArgb(80, 220, 140),
                AutoSize = false,
                Width = 96,
                Height = 96,
                Left = 40,
                Top = 36,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(24, 32, 40)
            };
            card.Controls.Add(monogram);
        }

        _title = new Label
        {
            Text = "TURBORAMA",
            Font = new Font("Segoe UI", 30f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = false,
            Width = 560,
            Height = 48,
            Left = 160,
            Top = 48,
            TextAlign = ContentAlignment.MiddleLeft
        };
        card.Controls.Add(_title);

        _subtitle = new Label
        {
            Text = "ARCADE BOOT",
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Color.FromArgb(80, 220, 140),
            AutoSize = false,
            Width = 560,
            Height = 28,
            Left = 160,
            Top = 100
        };
        card.Controls.Add(_subtitle);

        var accent = new Panel
        {
            BackColor = Color.FromArgb(40, 200, 120),
            Width = 140,
            Height = 3,
            Left = 160,
            Top = 136
        };
        card.Controls.Add(accent);

        _status = new Label
        {
            Text = "Iniciando...",
            Font = new Font("Segoe UI", 12f, FontStyle.Regular),
            ForeColor = Color.FromArgb(170, 180, 190),
            AutoSize = false,
            Width = 700,
            Height = 30,
            Left = 40,
            Top = 180
        };
        card.Controls.Add(_status);

        _percent = new Label
        {
            Text = "0%",
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = Color.FromArgb(80, 220, 140),
            AutoSize = false,
            Width = 80,
            Height = 26,
            Left = 660,
            Top = 230,
            TextAlign = ContentAlignment.MiddleRight
        };
        card.Controls.Add(_percent);

        _barHost = new Panel
        {
            Width = 700,
            Height = 18,
            Left = 40,
            Top = 262,
            BackColor = Color.FromArgb(28, 34, 44)
        };
        _barHost.Paint += BarHost_Paint;
        card.Controls.Add(_barHost);

        var hint = new Label
        {
            Text = "Aguarde — a iniciar o console arcade",
            Font = new Font("Segoe UI", 9f, FontStyle.Regular),
            ForeColor = Color.FromArgb(100, 110, 120),
            AutoSize = false,
            Width = 700,
            Height = 24,
            Left = 40,
            Top = 310
        };
        card.Controls.Add(hint);

        _animTimer = new System.Windows.Forms.Timer { Interval = 40 };
        _animTimer.Tick += (_, _) =>
        {
            _pulse = (_pulse + 1) % 100;
            if (_progress < 90)
            {
                _progress = Math.Min(90, _progress + 1);
                ApplyProgressUi();
            }
            else
            {
                _barHost.Invalidate();
            }
        };

        Resize += (_, _) => CenterCard(card);
    }

    private static Image? LoadLogoImage()
    {
        string[] paths =
        {
            ProductPaths.DefaultBootLogoPng,
            System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "logo.png"),
            System.IO.Path.Combine(ProductPaths.Root, "Launcher", "assets", "logo.png"),
        };

        foreach (string p in paths)
        {
            try
            {
                if (System.IO.File.Exists(p))
                {
                    // Cópia em memória (stream pode fechar; Image precisa dos bytes)
                    byte[] bytes = System.IO.File.ReadAllBytes(p);
                    var ms = new System.IO.MemoryStream(bytes);
                    return Image.FromStream(ms);
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private void CenterCard(Control card)
    {
        card.Left = Math.Max(0, (ClientSize.Width - card.Width) / 2);
        card.Top = Math.Max(0, (ClientSize.Height - card.Height) / 2);
    }

    public void ShowLoading()
    {
        if (!Visible)
        {
            Show();
        }

        Activate();
        BringToFront();
        TopMost = true;
        _progress = 6;
        ApplyProgressUi();
        _animTimer.Start();
        Application.DoEvents();
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
        using (var bg = new SolidBrush(Color.FromArgb(28, 34, 44)))
        {
            g.FillRectangle(bg, bounds);
        }

        int w = Math.Max(0, (int)(bounds.Width * (_progress / 100.0)));
        if (w > 0)
        {
            using var brush = new LinearGradientBrush(
                new Rectangle(0, 0, Math.Max(1, w), bounds.Height),
                Color.FromArgb(40, 200, 120),
                Color.FromArgb(100, 255, 170),
                LinearGradientMode.Horizontal);
            g.FillRectangle(brush, 0, 0, w, bounds.Height);
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
}
