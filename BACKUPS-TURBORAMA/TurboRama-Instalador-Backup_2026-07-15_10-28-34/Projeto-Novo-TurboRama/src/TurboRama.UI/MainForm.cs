using TurboRama.Configuration;
using TurboRama.Core.Ipc;
using TurboRama.Core.Logging;
using TurboRama.Core.Paths;
using TurboRama.Core.Results;
using TurboRama.Core.State;
using TurboRama.Diagnostics;
using TurboRama.Windows.Optional;

namespace TurboRama.UI;

internal sealed class MainForm : Form
{
    private readonly ProductConfiguration _config;
    private readonly ITurboRamaLogger _logger;
    private readonly TextBox _logBox = null!;
    private readonly Label _statusLabel = null!;
    private readonly Label _progressLabel = null!;
    private readonly ProgressBar _progressBar = null!;
    private readonly CheckBox _chkUwf = null!;
    private readonly CheckBox _chkKb = null!;
    private readonly CheckBox _chkBoot = null!;
    private readonly List<Control> _actionButtons = new();
    private bool _busy;

    public MainForm(ProductConfiguration config, ITurboRamaLogger logger)
    {
        _config = config;
        _logger = logger;

        Text = "TurboRama Secure — Projeto Novo (Fases 0–6)";
        Width = 940;
        Height = 760;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10F);

        var title = new Label
        {
            Text = "TurboRama Secure Kiosk",
            Left = 20,
            Top = 10,
            Width = 880,
            Font = new Font("Segoe UI", 16F, FontStyle.Bold)
        };

        var subtitle = new Label
        {
            Text = "Fase 4: módulos OPCIONAIS (UWF / Keyboard Filter / branding). Default = desligado.\n" +
                   "Risco alto — use só em máquina dedicada, com baseline e conta Admin de recuperação.",
            Left = 20,
            Top = 44,
            Width = 880,
            Height = 40
        };

        int y = 90;
        int h = 30;

        // Core
        var btnPreflight = MakeButton("Preflight", 20, y, 100, h);
        btnPreflight.Click += async (_, _) =>
        {
            await RunWithProgressAsync("Preflight", "Verificando...", async () =>
            {
                var report = await Task.Run(() => Program.RunPreflight(_config, _logger, showUi: false));
                Append(string.Join(Environment.NewLine, report.Items.Select(i => i.Severity + ": " + i.Message)));
                return (report.Success, report.Success ? "Preflight OK." : "Preflight com ERROS.");
            }, true);
        };

        var btnPhase2 = MakeButton("Fase 2 Kiosk", 130, y, 120, h);
        btnPhase2.Click += async (_, _) =>
        {
            if (MessageBox.Show("Instalar kiosk básico?", "Fase 2", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            await RunWithProgressAsync("Fase 2", "Kiosk...", async () =>
            {
                OperationResult r = await Program.RunPhase2Async(_config, _logger, true);
                Append(r.ToString());
                return (r.Success, r.Success ? "Kiosk OK." : r.Message);
            }, true);
        };

        var btnPhase3 = MakeButton("Fase 3 Serviços", 260, y, 130, h);
        btnPhase3.BackColor = Color.FromArgb(16, 124, 16);
        btnPhase3.ForeColor = Color.White;
        btnPhase3.FlatStyle = FlatStyle.Flat;
        btnPhase3.Click += async (_, _) =>
        {
            if (MessageBox.Show("Instalar Watchdog + Maintenance?", "Fase 3", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            await RunWithProgressAsync("Fase 3", "Serviços...", async () =>
            {
                OperationResult r = await Program.RunPhase3Async(_config, _logger, true);
                Append(r.ToString());
                return (r.Success, r.Success ? "Serviços OK." : r.Message);
            }, true);
        };

        var btnStatus = MakeButton("Status", 400, y, 90, h);
        btnStatus.Click += async (_, _) =>
        {
            await RunWithProgressAsync("Status", "Consultando (máx. ~5s)...", async () =>
            {
                // Timeout global rígido: a UI NUNCA fica presa no Status
                var work = Task.Run(CollectStatusSnapshot);
                var finished = await Task.WhenAny(work, Task.Delay(5000)).ConfigureAwait(true);
                if (finished != work)
                {
                    string partial = QueryServiceLine("TurboRamaWatchdog") + "\n" +
                                     QueryServiceLine("TurboRamaMaintenance");
                    Append("Status: timeout global 5s (pipe lento). Serviços locais:");
                    Append(partial);
                    return (true,
                        "Status parcial (timeout 5s).\n" + partial +
                        "\n\nFase 3 pode estar OK. Se quiser, confira com: sc query TurboRamaWatchdog");
                }

                var snap = work.Result;
                foreach (string line in snap.LogLines)
                {
                    Append(line);
                }

                return (true, snap.Summary);
            }, true);
        };

        var btnPhase6 = MakeButton("Fase 6 Aceite", 500, y, 120, h);
        btnPhase6.BackColor = Color.DarkSlateBlue;
        btnPhase6.ForeColor = Color.White;
        btnPhase6.FlatStyle = FlatStyle.Flat;
        btnPhase6.Click += async (_, _) =>
        {
            await RunWithProgressAsync("Fase 6", "Validando instalação / segurança...", async () =>
            {
                OperationResult r = await Task.Run(() =>
                    new PostInstallValidationService().RunToResult(_config, clearLocks: true));
                Append(r.ToString());
                return (r.Success, r.Message);
            }, true);
        };

        y += h + 12;

        // Fase 4 options
        var grp = new GroupBox
        {
            Text = "Fase 4 — módulos opcionais (marque só o que aceitar o risco)",
            Left = 20,
            Top = y,
            Width = 880,
            Height = 100
        };

        _chkUwf = new CheckBox
        {
            Text = "UWF (write filter) — IoT/Enterprise",
            Left = 16,
            Top = 28,
            Width = 400,
            Checked = false
        };
        _chkKb = new CheckBox
        {
            Text = "Keyboard Filter — Embedded",
            Left = 16,
            Top = 54,
            Width = 400,
            Checked = false
        };
        _chkBoot = new CheckBox
        {
            Text = "Boot branding leve (não esconde WinRE)",
            Left = 430,
            Top = 28,
            Width = 420,
            Checked = false
        };

        var btnPhase4 = MakeButton("Aplicar opcionais", 430, 52, 150, 28);
        btnPhase4.BackColor = Color.DarkOrange;
        btnPhase4.ForeColor = Color.White;
        btnPhase4.FlatStyle = FlatStyle.Flat;
        btnPhase4.Click += async (_, _) =>
        {
            if (!_chkUwf.Checked && !_chkKb.Checked && !_chkBoot.Checked)
            {
                MessageBox.Show("Nenhum módulo marcado — nada a fazer (default seguro).", "Fase 4",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(
                    "AVISO DE RISCO\n\n" +
                    "UWF: " + (_chkUwf.Checked ? "SIM" : "não") + "\n" +
                    "Keyboard Filter: " + (_chkKb.Checked ? "SIM" : "não") + "\n" +
                    "Boot branding: " + (_chkBoot.Checked ? "SIM" : "não") + "\n\n" +
                    "Requer baseline + conta Admin de recuperação.\nContinuar?",
                    "Fase 4 — Risco",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            await RunWithProgressAsync("Fase 4", "Aplicando módulos opcionais...", async () =>
            {
                OperationResult r = await Program.RunPhase4Async(
                    _config, _logger,
                    _chkUwf.Checked, _chkKb.Checked, _chkBoot.Checked,
                    force: true);
                Append(r.ToString());
                _config.EnableUwf = _chkUwf.Checked;
                _config.EnableKeyboardFilter = _chkKb.Checked;
                _config.EnableBootBranding = _chkBoot.Checked;
                if (_chkUwf.Checked || _chkKb.Checked || _chkBoot.Checked)
                {
                    _config.Profile = "ArcadeDedicated";
                }

                ConfigurationStore.Save(_config);
                return (r.Success, r.Success ? "Opcionais aplicados.\nPode ser necessário reiniciar." : r.Message);
            }, true);
        };

        var btnRollback4 = MakeButton("Rollback opcionais", 590, 52, 150, 28);
        btnRollback4.Click += async (_, _) =>
        {
            await RunWithProgressAsync("Rollback Fase 4", "Revertendo opcionais...", async () =>
            {
                OperationResult r = await Program.RunPhase4RollbackAsync(_config, _logger);
                Append(r.ToString());
                return (r.Success, r.Success ? "Rollback opcionais OK." : r.Message);
            }, true);
        };

        grp.Controls.Add(_chkUwf);
        grp.Controls.Add(_chkKb);
        grp.Controls.Add(_chkBoot);
        grp.Controls.Add(btnPhase4);
        grp.Controls.Add(btnRollback4);

        y += 110;

        var btnEnterMaint = MakeButton("Entrar manutenção", 20, y, 150, h);
        btnEnterMaint.Click += async (_, _) =>
        {
            await RunWithProgressAsync("Manutenção", "ENTER...", async () =>
            {
                OperationResult r = await Task.Run(() => MaintenanceClient.Send(MaintenanceProtocol.Commands.EnterMaintenance));
                Append(r.ToString());
                return (r.Success, r.Success ? "Manutenção ATIVA." : r.Message);
            }, true);
        };

        var btnExitMaint = MakeButton("Sair manutenção", 180, y, 140, h);
        btnExitMaint.Click += async (_, _) =>
        {
            await RunWithProgressAsync("Manutenção", "EXIT...", async () =>
            {
                OperationResult r = await Task.Run(() => MaintenanceClient.Send(MaintenanceProtocol.Commands.ExitMaintenance));
                Append(r.ToString());
                return (r.Success, r.Success ? "Manutenção OFF." : r.Message);
            }, true);
        };

        var btnLogs = MakeButton("Logs", 330, y, 80, h);
        btnLogs.Click += (_, _) => OpenPath(ProductPaths.Logs);

        var btnRoot = MakeButton("C:\\TurboRama", 420, y, 120, h);
        btnRoot.Click += (_, _) => OpenPath(ProductPaths.Root);

        _actionButtons.AddRange(new Control[]
        {
            btnPreflight, btnPhase2, btnPhase3, btnStatus, btnPhase6, btnPhase4, btnRollback4, btnEnterMaint, btnExitMaint
        });

        y += h + 12;

        _progressLabel = new Label
        {
            Text = "Pronto.",
            Left = 20,
            Top = y,
            Width = 880,
            Height = 22,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold)
        };
        y += 26;
        _progressBar = new ProgressBar
        {
            Left = 20,
            Top = y,
            Width = 880,
            Height = 20,
            Style = ProgressBarStyle.Continuous,
            Minimum = 0,
            Maximum = 100
        };
        y += 28;
        _statusLabel = new Label
        {
            Text = "Fase 4 | Perfil: " + _config.Profile + " | Id: " + _config.InstallationId.ToString("D"),
            Left = 20,
            Top = y,
            Width = 880,
            Height = 22
        };
        y += 26;
        _logBox = new TextBox
        {
            Left = 20,
            Top = y,
            Width = 880,
            Height = 300,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            Font = new Font("Consolas", 9F)
        };

        Controls.Add(title);
        Controls.Add(subtitle);
        Controls.Add(btnPreflight);
        Controls.Add(btnPhase2);
        Controls.Add(btnPhase3);
        Controls.Add(btnStatus);
        Controls.Add(btnPhase6);
        Controls.Add(grp);
        Controls.Add(btnEnterMaint);
        Controls.Add(btnExitMaint);
        Controls.Add(btnLogs);
        Controls.Add(btnRoot);
        Controls.Add(_progressLabel);
        Controls.Add(_progressBar);
        Controls.Add(_statusLabel);
        Controls.Add(_logBox);

        Append("Fase 4 — opcionais OFF por padrão.");
        Append("Fluxo completo: Preflight → Fase 2 → Fase 3 → (opcional) Fase 4.");
        Append("Na sua tela atual: clique INSTALAR SERVIÇOS se ainda não instalou a Fase 3.");
    }

    private static Button MakeButton(string text, int left, int top, int width, int height) =>
        new() { Text = text, Left = left, Top = top, Width = width, Height = height };

    private sealed class StatusSnapshot
    {
        public List<string> LogLines { get; } = new();
        public string Summary { get; set; } = "";
    }

    /// <summary>
    /// Snapshot de status com timeouts curtos (roda em background).
    /// </summary>
    private static StatusSnapshot CollectStatusSnapshot()
    {
        var snap = new StatusSnapshot();

        string wd = QueryServiceLine("TurboRamaWatchdog");
        string mt = QueryServiceLine("TurboRamaMaintenance");
        snap.LogLines.Add(wd);
        snap.LogLines.Add(mt);

        OperationResult pipe = MaintenanceClient.Send(MaintenanceProtocol.Commands.Status, timeoutMs: 2000);
        snap.LogLines.Add(pipe.ToString());

        OperationResult uwf = UwfModuleService.GetStatus();
        OperationResult kb = KeyboardFilterModuleService.GetStatus();
        bool lockOn = MaintenanceLock.IsActive();
        snap.LogLines.Add(uwf.ToString());
        snap.LogLines.Add(kb.ToString());
        snap.LogLines.Add("maintenance.lock=" + lockOn);

        bool servicesOk = wd.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) &&
                          mt.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);

        snap.Summary =
            wd + "\n" + mt + "\n" +
            "Pipe Maintenance: " + (pipe.Success ? pipe.Message : "FALHOU — " + pipe.Message) + "\n" +
            "UWF: " + uwf.Message + "\n" +
            "KeyboardFilter: " + kb.Message + "\n" +
            "lock=" + lockOn + "\n" +
            (servicesOk ? "Serviços Windows: OK (ambos RUNNING)." : "Serviços Windows: verifique se ambos estão RUNNING.");

        return snap;
    }

    private static string QueryServiceLine(string serviceName)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = "query \"" + serviceName + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null)
            {
                return serviceName + ": sc não iniciou";
            }

            // Read async + hard wait — evita hang se sc falhar
            var readOut = proc.StandardOutput.ReadToEndAsync();
            var readErr = proc.StandardError.ReadToEndAsync();
            if (!proc.WaitForExit(4000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return serviceName + ": timeout sc query";
            }

            Task.WaitAll(new Task[] { readOut, readErr }, 2000);
            string text = ((readOut.IsCompletedSuccessfully ? readOut.Result : "") + " " +
                           (readErr.IsCompletedSuccessfully ? readErr.Result : ""));
            if (text.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
            {
                return serviceName + ": RUNNING";
            }

            if (text.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
            {
                return serviceName + ": STOPPED";
            }

            if (text.Contains("1060") || text.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
            {
                return serviceName + ": NÃO INSTALADO";
            }

            string state = text.Contains("STATE", StringComparison.OrdinalIgnoreCase)
                ? text.Split('\n').FirstOrDefault(l => l.Contains("STATE", StringComparison.OrdinalIgnoreCase))?.Trim() ?? "?"
                : text.Trim();
            if (state.Length > 80)
            {
                state = state[..80];
            }

            return serviceName + ": " + state;
        }
        catch (Exception ex)
        {
            return serviceName + ": erro " + ex.Message;
        }
    }

    private async Task RunWithProgressAsync(
        string title,
        string busyMessage,
        Func<Task<(bool success, string summary)>> action,
        bool showDoneDialog)
    {
        if (_busy)
        {
            MessageBox.Show("Aguarde a operação atual.", "Ocupado", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _busy = true;
        SetButtonsEnabled(false);
        Cursor = Cursors.WaitCursor;
        _progressLabel.ForeColor = Color.DarkOrange;
        _progressLabel.Text = "Aguarde: " + busyMessage;
        _statusLabel.Text = title + " em andamento...";
        _progressBar.Style = ProgressBarStyle.Marquee;
        _progressBar.MarqueeAnimationSpeed = 30;
        Append(">>> " + title + " iniciado...");
        Application.DoEvents();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool success = false;
        string summary = "Sem resultado.";

        try
        {
            (success, summary) = await action().ConfigureAwait(true);
            sw.Stop();
            Append(">>> " + title + (success ? " CONCLUÍDO" : " FALHOU") + " em " + sw.Elapsed.TotalSeconds.ToString("0.0") + "s");
        }
        catch (Exception ex)
        {
            sw.Stop();
            success = false;
            summary = "Exceção: " + ex.Message;
            Append("EXCEÇÃO: " + ex.Message);
        }
        finally
        {
            _progressBar.Style = ProgressBarStyle.Continuous;
            _progressBar.MarqueeAnimationSpeed = 0;
            _progressBar.Value = success ? 100 : 0;
            _progressLabel.ForeColor = success ? Color.DarkGreen : Color.DarkRed;
            _progressLabel.Text = success
                ? "CONCLUÍDO — " + title + " (" + sw.Elapsed.TotalSeconds.ToString("0.0") + "s)"
                : "FALHOU — " + title;
            _statusLabel.Text = success ? title + " OK" : title + " com erro";
            Cursor = Cursors.Default;
            SetButtonsEnabled(true);
            _busy = false;
        }

        if (showDoneDialog)
        {
            MessageBox.Show(
                summary + "\n\nTempo: " + sw.Elapsed.TotalSeconds.ToString("0.0") + " s",
                success ? "Concluído — " + title : "Erro — " + title,
                MessageBoxButtons.OK,
                success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }
    }

    private void SetButtonsEnabled(bool enabled)
    {
        foreach (Control c in _actionButtons)
        {
            c.Enabled = enabled;
        }
    }

    private void OpenPath(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                Append("Inexistente: " + path);
                return;
            }

            System.Diagnostics.Process.Start("explorer.exe", path);
        }
        catch (Exception ex)
        {
            Append("ERRO: " + ex.Message);
        }
    }

    private void Append(string line)
    {
        if (_logBox.InvokeRequired)
        {
            _logBox.Invoke(() => Append(line));
            return;
        }

        _logBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + line + Environment.NewLine);
    }
}
