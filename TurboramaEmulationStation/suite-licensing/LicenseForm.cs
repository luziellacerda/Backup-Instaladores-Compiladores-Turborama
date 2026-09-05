using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security;
using TurboBoxManager.Licensing;

namespace TurboRama.EmulationStation.Access;

internal sealed class LicenseForm : Form
{
    private readonly SuiteLicensingRuntime _runtime;
    private readonly BridgeConnection _bridge;
    private readonly CancellationTokenSource _lifetime;
    private readonly LicenseAccessView _view = new();
    private readonly System.Windows.Forms.Timer _watch = new() { Interval = 250 };
    private bool _busy;
    private bool _silentInitialAuthorization;

    internal LicenseForm(SuiteLicensingRuntime runtime, BridgeConnection bridge,
        CancellationTokenSource lifetime)
    {
        _runtime = runtime;
        _bridge = bridge;
        _lifetime = lifetime;
        ConfigureShell(this, _view);
        AcceptButton = _view.OpenButton;
        CancelButton = _view.CancelAccessButton;

        // Read the same DPAPI convenience cache before creating the native
        // window. A cached identifier is still checked online; opacity zero
        // prevents a login-dialog flash while that existing check is pending.
        var cached = LicenseCache.TryRead();
        if (cached is not null)
        {
            _view.LicenseInput.Text = cached;
            _silentInitialAuthorization = true;
            Opacity = 0;
            ShowInTaskbar = false;
        }

        _view.OpenButton.Click += async (_, _) => await AuthorizeAsync();
        _view.CancelAccessButton.Click += (_, _) => Close();
        Shown += async (_, _) =>
        {
            _watch.Start();
            if (_lifetime.IsCancellationRequested) { Close(); return; }
            if (_silentInitialAuthorization) await AuthorizeAsync();
            else _view.LicenseInput.Focus();
        };
        _watch.Tick += (_, _) =>
        {
            if (_lifetime.IsCancellationRequested
                || (_bridge.WasReady && (!_runtime.IsAvailable
                    || _runtime.CurrentContext?.IsAuthorized != true)))
                Close();
        };
        FormClosing += (_, _) =>
        {
            if (!_bridge.WasReady && !_lifetime.IsCancellationRequested)
                _bridge.CancelAccess();
            _lifetime.Cancel();
        };
    }

    // Used only by the preflight failure path. This dialog has no runtime,
    // activation, license/cache lookup or network behavior.
    internal static void ShowUnavailable(string message)
    {
        using var dialog = new Form();
        var view = new LicenseAccessView();
        ConfigureShell(dialog, view);
        view.PresentUnavailable(message);
        dialog.AcceptButton = view.CancelAccessButton;
        dialog.CancelButton = view.CancelAccessButton;
        view.CancelAccessButton.Click += (_, _) => dialog.Close();
        dialog.ShowDialog();
    }

    private async Task AuthorizeAsync()
    {
        if (_busy || _lifetime.IsCancellationRequested) return;
        _busy = true;
        _view.SetBusy(true);
        _view.SetStatus("Conferindo a ativação existente no servidor…");
        try
        {
            var licenseId = SuiteOnlineLicenseProtocol.RequireIdentifier(
                _view.LicenseInput.Text.Trim(), "LicenseId", 6, 64);
            var context = await _runtime.OpenAsync(licenseId, _lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;
            context.ThrowIfUnauthorized();
            // A cached identifier is convenience only. Never cache authorization,
            // a session, activation code or private key.
            LicenseCache.TrySave(context);
            if (!_bridge.Ready(() => _runtime.IsAvailable && context.IsAuthorized))
            {
                Close();
                return;
            }
            _view.LicenseInput.Clear();
            ShowInTaskbar = false;
            Hide();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is SuiteApiException or SuiteAuthorizationException
            or SuiteLicensingUnavailableException or SecurityException
            or HttpRequestException or TaskCanceledException or ArgumentException)
        {
            if (!_lifetime.IsCancellationRequested)
                _view.SetStatus(AccessFailurePresentation.Describe(ex), isError: true);
        }
        finally
        {
            _busy = false;
            if (!IsDisposed && !_lifetime.IsCancellationRequested && !_bridge.WasReady)
            {
                _view.SetBusy(false);
                if (_silentInitialAuthorization)
                {
                    // A denied/offline cached attempt becomes the same editable
                    // one-field login. No automatic activation/rebinding occurs.
                    _silentInitialAuthorization = false;
                    Opacity = 1;
                    ShowInTaskbar = true;
                    Show();
                    Activate();
                }
                _view.LicenseInput.Focus();
            }
        }
    }

    private static void ConfigureShell(Form form, LicenseAccessView view)
    {
        form.AutoScaleDimensions = new SizeF(96, 96);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.Text = "TurboRama Suite";
        form.StartPosition = FormStartPosition.CenterScreen;
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.MaximizeBox = false;
        form.MinimizeBox = false;
        ConfigureWindowIcon(form);
        form.ClientSize = new Size(540, 340);
        form.BackColor = LicenseAccessView.Canvas;
        form.ForeColor = LicenseAccessView.PrimaryText;
        form.Font = new Font("Segoe UI", 10F);
        view.Dock = DockStyle.Fill;
        form.Controls.Add(view);
        form.HandleCreated += (_, _) =>
        {
            // Windows 10/11 native dark title bar. Unsupported versions simply
            // keep the normal system caption; authentication never depends on it.
            var dark = 1;
            if (DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int)) != 0)
                _ = DwmSetWindowAttribute(form.Handle, 19, ref dark, sizeof(int));
        };
    }

    private static void ConfigureWindowIcon(Form form)
    {
        form.ShowIcon = true;
        try
        {
            // The same icon as the frontend is embedded at build time. Never
            // resolve it from the working directory or another user's file.
            using var resource = typeof(LicenseForm).Assembly.GetManifestResourceStream(
                "TurboRama.Suite.Access.AppIcon.ico");
            if (resource is null) return;
            using var source = new Icon(resource);
            var icon = (Icon)source.Clone();
            try
            {
                form.Icon = icon;
                form.Disposed += (_, _) => icon.Dispose();
            }
            catch
            {
                icon.Dispose();
                throw;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException
            or ExternalException or InvalidOperationException or NotSupportedException)
        {
            // Keep the Windows default icon if presentation fails. Licensing
            // and the cached-login hidden state never depend on icon loading.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute,
        ref int value, int size);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _watch.Dispose();
        base.Dispose(disposing);
    }
}
