using System.Diagnostics;
using TurboRama.ArcadeTimer.Configuration;
using TurboRama.ArcadeTimer.Models;
using TurboRama.ArcadeTimer.Services;

namespace TurboRama.ArcadeTimer;

public sealed class TimerForm : Form
{
    private readonly Label _titleLabel = new();
    private readonly Label _timeLabel = new();
    private readonly Label _statusLabel = new();

    private readonly TimerConfig _config;
    private readonly CreditManager _creditManager;
    private readonly GlobalKeyboardHook _keyboardHook;
    private readonly CoinInputService _coinInput;
    private readonly EmulatorMonitor _monitor;
    private readonly EmulatorController _controller;
    private readonly System.Windows.Forms.Timer _loopTimer;

    private Keys _coinKey = Keys.F10;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _lastTickMs;
    private TimerState _state = TimerState.Initializing;
    private bool _endingHandled;

    public TimerForm()
    {
        string configPath = Path.Combine(
            AppContext.BaseDirectory,
            "config.json");

        _config = TimerConfig.Load(configPath);

        LogService.Configure(_config.Logging.Enabled);
        LogService.CleanupOldLogs(_config.Logging.RetentionDays);

        ConfigureWindow();
        ConfigureControls();

        var store = new CreditStore(AppContext.BaseDirectory);

        _creditManager = new CreditManager(
            store,
            _config.SaveRemainingTime &&
            _config.RestoreCreditAfterRestart,
            _config.MaxRemainingSeconds,
            minutesPerCoinCap: 60);

        _lastTickMs = _clock.ElapsedMilliseconds;

        _monitor = new EmulatorMonitor(_config.EmulatorProcesses);

        _controller = new EmulatorController(
            _config.EmulatorProcesses,
            _config.ProtectedProcesses,
            _config.GracefulCloseTimeoutMilliseconds,
            _config.ForceCloseAfterTimeout);

        _coinInput = new CoinInputService(
            _config.CoinDebounceMilliseconds);

        _keyboardHook = new GlobalKeyboardHook();

        if (!Enum.TryParse(_config.CoinKey, true, out _coinKey))
            _coinKey = Keys.F10;

        _coinInput.CoinAccepted += HandleCoinAccepted;
        _keyboardHook.KeyPressed += key =>
        {
            if (key != _coinKey)
                return;

            // Hook LL corre em thread do SO — marshalar para UI.
            try
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    BeginInvoke(new Action(() => _coinInput.ReceivePulse()));
                }
                else
                {
                    _coinInput.ReceivePulse();
                }
            }
            catch
            {
                try { _coinInput.ReceivePulse(); } catch { }
            }
        };

        _creditManager.CreditChanged += _ =>
        {
            try
            {
                if (IsHandleCreated && InvokeRequired)
                    BeginInvoke(new Action(UpdateDisplay));
                else
                    UpdateDisplay();
            }
            catch { }
        };

        _creditManager.CreditEnded += HandleCreditEnded;

        _keyboardHook.Start();

        _loopTimer = new System.Windows.Forms.Timer
        {
            Interval = _config.EmulatorCheckIntervalMilliseconds
        };

        _loopTimer.Tick += LoopTick;
        _loopTimer.Start();

        _state = _creditManager.Remaining > TimeSpan.Zero
            ? TimerState.CreditAvailable
            : TimerState.NoCredit;

        _endingHandled = _creditManager.Remaining <= TimeSpan.Zero;

        Shown += (_, _) => PositionWindow();

        UpdateDisplay();
        LogService.Write("TurboRama Arcade Timer iniciado.");
    }

    private void ConfigureWindow()
    {
        Text = "TurboRama Arcade Timer";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = _config.Window.AllowClose;
        TopMost = _config.Window.TopMost;
        int minW = _config.Window.Compact ? 140 : 220;
        int minH = _config.Window.Compact ? 52 : 90;
        Width = Math.Max(minW, _config.Window.Width);
        Height = Math.Max(minH, _config.Window.Height);
        BackColor = Color.FromArgb(10, 10, 18);
        Opacity = Math.Clamp(_config.Window.Opacity, 0.30, 1.0);
        KeyPreview = true;

        KeyDown += (_, e) =>
        {
            if (!_config.Window.AllowClose)
                return;
            if (e.KeyCode == Keys.Escape ||
                (e.Control && e.Shift && e.KeyCode == Keys.Q))
            {
                Close();
            }
        };

        DoubleClick += (_, _) =>
        {
            if (_config.Window.AllowClose)
                Close();
        };

        if (!_config.Window.Enabled)
            WindowState = FormWindowState.Minimized;
    }

    private void ConfigureControls()
    {
        bool compact = _config.Window.Compact;

        _titleLabel.Text = compact ? "TR" : "TURBORAMA ARCADE";
        _titleLabel.Dock = DockStyle.Top;
        _titleLabel.Height = compact ? 14 : 22;
        _titleLabel.TextAlign = ContentAlignment.MiddleCenter;
        _titleLabel.Font = new Font("Segoe UI", compact ? 7f : 9f, FontStyle.Bold);
        _titleLabel.ForeColor = Color.FromArgb(200, 200, 210);
        _titleLabel.DoubleClick += (_, _) => { if (_config.Window.AllowClose) Close(); };

        _timeLabel.Dock = DockStyle.Top;
        _timeLabel.Height = compact ? 28 : 48;
        _timeLabel.TextAlign = ContentAlignment.MiddleCenter;
        _timeLabel.Font = new Font("Segoe UI", compact ? 16f : 26f, FontStyle.Bold);
        _timeLabel.DoubleClick += (_, _) => { if (_config.Window.AllowClose) Close(); };

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleCenter;
        _statusLabel.Font = new Font("Segoe UI", compact ? 7.5f : 10f, FontStyle.Bold);
        _statusLabel.ForeColor = Color.White;
        _statusLabel.DoubleClick += (_, _) => { if (_config.Window.AllowClose) Close(); };

        Controls.Add(_statusLabel);
        Controls.Add(_timeLabel);
        Controls.Add(_titleLabel);
    }

    private void PositionWindow()
    {
        Rectangle area = Screen.PrimaryScreen?.WorkingArea
                         ?? new Rectangle(0, 0, 1920, 1080);

        Left = Math.Max(
            area.Left,
            area.Right - Width - _config.Window.RightMargin);

        Top = area.Top + _config.Window.TopMargin;
    }

    private void HandleCoinAccepted()
    {
        try
        {
            _creditManager.AddCoin(_config.MinutesPerCoin);
            _endingHandled = false;

            LogService.Write(
                $"Ficha aceita. +{_config.MinutesPerCoin} minuto(s). " +
                $"Saldo: {_creditManager.Remaining}.");

            System.Media.SystemSounds.Asterisk.Play();
            UpdateDisplay();
        }
        catch (Exception ex)
        {
            LogService.Write("Falha ao aceitar ficha", ex);
        }
    }

    private void LoopTick(object? sender, EventArgs e)
    {
        try
        {
            // Tempo monotónico (Stopwatch) — imune a mudança de relógio do Windows.
            long nowMs = _clock.ElapsedMilliseconds;
            long deltaMs = nowMs - _lastTickMs;
            _lastTickMs = nowMs;
            if (deltaMs < 0)
                deltaMs = 0;
            if (deltaMs > 5_000)
                deltaMs = 5_000;
            TimeSpan elapsed = TimeSpan.FromMilliseconds(deltaMs);

            IReadOnlyList<Process> running = _monitor.GetRunningEmulators();
            bool emulatorRunning = running.Count > 0;
            bool hasCredit = _creditManager.Remaining > TimeSpan.Zero;

            if (!hasCredit)
            {
                _state = TimerState.NoCredit;

                if (_config.BlockGameWithoutCredit && emulatorRunning)
                    _controller.CloseAuthorizedEmulators(running);
                else
                    DisposeProcesses(running);

                UpdateDisplay();
                return;
            }

            // Contar sempre, ou só com emulador — conforme config.
            bool shouldCount = !_config.CountOnlyWhileEmulatorIsRunning || emulatorRunning;

            if (!emulatorRunning)
            {
                _state = TimerState.CreditAvailable;
                DisposeProcesses(running);

                if (shouldCount)
                    _creditManager.Consume(elapsed);

                UpdateDisplay();
                return;
            }

            DisposeProcesses(running);

            if (shouldCount)
                _creditManager.Consume(elapsed);

            TimeSpan remaining = _creditManager.Remaining;

            if (remaining <= TimeSpan.Zero)
                _state = TimerState.Ending;
            else if (remaining.TotalSeconds <= _config.WarningSeconds)
                _state = TimerState.Warning;
            else
                _state = TimerState.Playing;

            UpdateDisplay();
        }
        catch (Exception ex)
        {
            LogService.Write("Erro no loop do timer", ex);
        }
    }

    private void HandleCreditEnded()
    {
        void Work()
        {
            if (_endingHandled)
                return;

            _endingHandled = true;
            _state = TimerState.Ending;

            if (_config.CloseEmulatorWhenTimeEnds)
            {
                IReadOnlyList<Process> running = _monitor.GetRunningEmulators();
                _controller.CloseAuthorizedEmulators(running);
            }

            System.Media.SystemSounds.Exclamation.Play();
            LogService.Write("Tempo encerrado.");
            UpdateDisplay();
        }

        try
        {
            if (IsHandleCreated && InvokeRequired)
                BeginInvoke(new Action(Work));
            else
                Work();
        }
        catch (Exception ex)
        {
            LogService.Write("Falha ao encerrar tempo", ex);
        }
    }

    private void UpdateDisplay()
    {
        try
        {
            TimeSpan remaining = _creditManager.Remaining;
            int totalHours = (int)remaining.TotalHours;

            _timeLabel.Text =
                $"{totalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";

            bool compact = _config.Window.Compact;
            switch (_state)
            {
                case TimerState.NoCredit:
                    _statusLabel.Text = compact ? $"FICHA ({_coinKey})" : $"INSIRA UMA FICHA — {_coinKey}";
                    _timeLabel.ForeColor = Color.Red;
                    break;

                case TimerState.CreditAvailable:
                    _statusLabel.Text = compact ? "CRÉDITO" : "CRÉDITO DISPONÍVEL — ESCOLHA UM JOGO";
                    _timeLabel.ForeColor = Color.Lime;
                    break;

                case TimerState.Playing:
                    _statusLabel.Text = compact ? "JOGO" : "JOGANDO";
                    _timeLabel.ForeColor = Color.Lime;
                    break;

                case TimerState.Warning:
                    _statusLabel.Text = compact ? "FIM!" : "TEMPO TERMINANDO";
                    _timeLabel.ForeColor = Color.Orange;
                    break;

                case TimerState.Ending:
                    _statusLabel.Text = compact ? "0" : "TEMPO ENCERRADO";
                    _timeLabel.ForeColor = Color.Red;
                    break;

                default:
                    _statusLabel.Text = compact ? "..." : "INICIALIZANDO";
                    _timeLabel.ForeColor = Color.White;
                    break;
            }
        }
        catch (Exception ex)
        {
            LogService.Write("Falha ao atualizar UI", ex);
        }
    }

    private static void DisposeProcesses(IEnumerable<Process> processes)
    {
        foreach (Process process in processes)
        {
            try { process.Dispose(); } catch { }
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        try { _creditManager.Save(); } catch { }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        try
        {
            _loopTimer.Stop();
            _keyboardHook.Dispose();
            LogService.Write("TurboRama Arcade Timer encerrado.");
        }
        catch { }

        base.OnFormClosed(e);
    }
}
