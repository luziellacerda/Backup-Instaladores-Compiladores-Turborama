using System.Net.Http;
using System.Security;
using TurboBoxManager.Licensing;

namespace TurboRama.EmulationStation.Access;

internal sealed class LicenseForm : Form
{
    private readonly SuiteLicensingRuntime _runtime;
    private readonly BridgeConnection _bridge;
    private readonly CancellationTokenSource _lifetime;
    private readonly TextBox _license = new() { MaxLength = 64, Width = 440 };
    private readonly Label _status = new() { AutoSize = false, Width = 460, Height = 72 };
    private readonly Button _open = new() { Text = "Abrir EmulationStation", AutoSize = true };
    private readonly Button _cancel = new() { Text = "Cancelar", AutoSize = true };
    private readonly System.Windows.Forms.Timer _watch = new() { Interval = 250 };
    private bool _busy;

    internal LicenseForm(SuiteLicensingRuntime runtime, BridgeConnection bridge,
        CancellationTokenSource lifetime)
    {
        _runtime = runtime;
        _bridge = bridge;
        _lifetime = lifetime;
        Text = "TurboRama — EmulationStation Suite";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(500, 280);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10);
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(20), AutoSize = false
        };
        layout.Controls.Add(new Label
        {
            Text = "Use a mesma licença já ativada no TurboRama Suite.",
            AutoSize = false, Width = 460, Height = 32
        });
        layout.Controls.Add(_license);
        layout.Controls.Add(_status);
        var buttons = new FlowLayoutPanel { AutoSize = true, Width = 460 };
        buttons.Controls.Add(_open);
        buttons.Controls.Add(_cancel);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        AcceptButton = _open;
        CancelButton = _cancel;
        _status.Text = "Informe o identificador da licença usado na Suite. "
            + "Este computador precisa estar ativado na mesma conta do Windows.";
        _open.Click += async (_, _) => await AuthorizeAsync();
        _cancel.Click += (_, _) => Close();
        Shown += async (_, _) =>
        {
            _watch.Start();
            if (_lifetime.IsCancellationRequested) { Close(); return; }
            var cached = LicenseCache.TryRead();
            if (cached is not null)
            {
                _license.Text = cached;
                await AuthorizeAsync();
            }
        };
        _watch.Tick += (_, _) =>
        {
            if (_lifetime.IsCancellationRequested
                || (_bridge.WasReady && (!_runtime.IsAvailable
                    || _runtime.CurrentContext?.IsAuthorized != true)))
                Close();
        };
        FormClosing += (_, _) => _lifetime.Cancel();
    }

    private async Task AuthorizeAsync()
    {
        if (_busy || _lifetime.IsCancellationRequested) return;
        _busy = true;
        _open.Enabled = false;
        _license.Enabled = false;
        _status.Text = "Conferindo a ativação existente no servidor...";
        try
        {
            var licenseId = SuiteOnlineLicenseProtocol.RequireIdentifier(
                _license.Text.Trim(), "LicenseId", 6, 64);
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
            _license.Clear();
            ShowInTaskbar = false;
            Hide();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is SuiteApiException or SuiteAuthorizationException
            or SuiteLicensingUnavailableException or SecurityException
            or HttpRequestException or TaskCanceledException or ArgumentException)
        {
            if (!_lifetime.IsCancellationRequested)
                _status.Text = ex switch
                {
                    SuiteApiException { StatusCode: 404 or 503 } =>
                        "O servidor ainda não disponibilizou o acesso do EmulationStation. "
                        + "Tente novamente após a atualização do servidor.",
                    HttpRequestException or TaskCanceledException =>
                        "Não foi possível confirmar a licença. Confira a internet e tente novamente.",
                    _ => "A licença não foi confirmada para este computador. "
                        + "Confira a licença, a ativação da Suite e a conta do Windows."
                };
        }
        finally
        {
            _busy = false;
            if (!IsDisposed && !_lifetime.IsCancellationRequested && !_bridge.WasReady)
            {
                _open.Enabled = true;
                _license.Enabled = true;
                _license.Focus();
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _watch.Dispose();
        base.Dispose(disposing);
    }
}
