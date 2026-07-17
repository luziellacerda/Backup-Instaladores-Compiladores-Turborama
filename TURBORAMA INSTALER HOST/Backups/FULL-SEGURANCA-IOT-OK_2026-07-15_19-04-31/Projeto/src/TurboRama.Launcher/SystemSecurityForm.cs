using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using TurboRama.Configuration;
using TurboRama.Core.Paths;

namespace TurboRama.Launcher;

/// <summary>
/// Menu de segurança TurboRama — substitui as funções úteis do Ctrl+Alt+Del.
/// Estável (sem animações que piscam). Atalho: Ctrl+End.
/// </summary>
internal sealed class SystemSecurityForm : Form
{
    private static readonly Color Bg = Color.FromArgb(2, 6, 12);
    private static readonly Color Card = Color.FromArgb(10, 24, 34);
    private static readonly Color Green = Color.FromArgb(0, 220, 100);
    private static readonly Color White = Color.FromArgb(245, 255, 250);
    private static readonly Color Red = Color.FromArgb(220, 45, 50);
    private static readonly Color Muted = Color.FromArgb(150, 165, 175);
    private static readonly Color Amber = Color.FromArgb(255, 200, 70);

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
        BackColor = Bg;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);

        int w = Math.Min(680, (int)(Bounds.Width * 0.52));
        int h = Math.Min(540, (int)(Bounds.Height * 0.62));
        _card = new Panel
        {
            Width = w,
            Height = h,
            Left = (Bounds.Width - w) / 2,
            Top = (Bounds.Height - h) / 2,
            BackColor = Card
        };
        EnableDoubleBuffer(_card);
        _card.Paint += (_, e) =>
        {
            using var pen = new Pen(Green, 2f);
            e.Graphics.DrawRectangle(pen, 1, 1, _card.Width - 3, _card.Height - 3);
            using var red = new Pen(Red, 2f);
            e.Graphics.DrawLine(red, 24, 3, _card.Width - 24, 3);
        };
        Controls.Add(_card);

        AddLabel("TURBORAMA", 28, 24, 26f, White, FontStyle.Bold);
        AddLabel("SEGURANÇA DO SISTEMA", 30, 68, 11f, Green, FontStyle.Bold);
        AddLabel("Ctrl+Alt+Del desativado  ·  use Ctrl+End", 30, 96, 9f, Muted, FontStyle.Regular);

        AddLabel("PIN de acesso (ações técnicas)", 30, 130, 9f, Muted, FontStyle.Bold);
        _pinBox = new TextBox
        {
            Left = 30,
            Top = 152,
            Width = 260,
            Font = new Font("Segoe UI", 14f),
            UseSystemPasswordChar = true,
            BackColor = Color.FromArgb(6, 16, 24),
            ForeColor = White,
            BorderStyle = BorderStyle.FixedSingle
        };
        _card.Controls.Add(_pinBox);

        _status = new Label
        {
            Text = "Escolha uma opção. Esc cancela.",
            Left = 30,
            Top = 192,
            Width = w - 60,
            Height = 24,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Muted,
            BackColor = Card
        };
        _card.Controls.Add(_status);

        int y = 230;
        int bw = (w - 60 - 12) / 2;
        Btn("CONTINUAR ARCADE", 30, y, bw, SecurityAction.Resume, false, Color.FromArgb(0, 70, 40));
        Btn("ABRIR WINDOWS", 30 + bw + 12, y, bw, SecurityAction.OpenExplorer, true, Color.FromArgb(0, 45, 60));
        y += 52;
        // Trocar usuário → ecrã de login (Admin / manutenção). PIN obrigatório.
        Btn("TROCAR USUÁRIO", 30, y, w - 60, SecurityAction.SwitchUser, true, Color.FromArgb(35, 40, 70));
        y += 52;
        Btn("REINICIAR PC", 30, y, bw, SecurityAction.Reboot, true, Color.FromArgb(50, 40, 10));
        Btn("DESLIGAR PC", 30 + bw + 12, y, bw, SecurityAction.Shutdown, true, Color.FromArgb(80, 25, 30));
        y += 52;
        Btn("CANCELAR", 30, y, w - 60, SecurityAction.None, false, Color.FromArgb(30, 34, 40));

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                ResultAction = SecurityAction.None;
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };
        Shown += (_, _) => { TopMost = true; BringToFront(); _pinBox.Focus(); };
    }

    private void AddLabel(string text, int x, int y, float size, Color color, FontStyle style)
    {
        _card.Controls.Add(new Label
        {
            Text = text,
            Left = x,
            Top = y,
            AutoSize = true,
            Font = new Font("Segoe UI", size, style),
            ForeColor = color,
            BackColor = Card
        });
    }

    private void Btn(string text, int x, int y, int width, SecurityAction action, bool needPin, Color bg)
    {
        var b = new Button
        {
            Text = text,
            Left = x,
            Top = y,
            Width = width,
            Height = 42,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = White,
            BackColor = bg,
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderColor = action == SecurityAction.Shutdown ? Red : Green;
        b.FlatAppearance.BorderSize = 1;
        b.Click += (_, _) => OnClick(action, needPin);
        _card.Controls.Add(b);
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
                _status.Text = "PIN incorreto.";
                _pinBox.Clear();
                _pinBox.Focus();
                return;
            }
        }

        ResultAction = action;
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Bg);
    protected override void OnPaint(PaintEventArgs e) => e.Graphics.Clear(Bg);

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

    /// <summary>
    /// Vai para o ecrã de login para trocar de conta (ex.: Arcade → Admin).
    /// Tenta Fast User Switching; se bloqueado pelo kiosk, faz logoff da sessão.
    /// </summary>
    public static void SwitchUserNow()
    {
        try
        {
            // Permite ecrã de troca de utilizador nesta sessão (política kiosk)
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
            catch { /* pode falhar sem admin — ok */ }

            // 1) Fast User Switching (mantém sessão Arcade em background)
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
                catch { /* fallback logoff */ }
            }

            // 2) Logoff → ecrã de login Windows (outro utilizador entra)
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

            // 3) API nativa
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
}
