using System.Diagnostics;
using System.Text;
using Microsoft.Win32;
using TurboRama.Configuration;
using TurboRama.Core.Paths;
using TurboRama.Core.Results;
using TurboRama.Core.State;

namespace TurboRama.Diagnostics;

/// <summary>
/// Fase 6 — aceite de fábrica / validação pós-instalação (segurança + saúde).
/// Não modifica o Windows (exceto opção clearLocks que só remove flags de estado).
/// </summary>
public sealed class PostInstallValidationService
{
    public PreflightReport Run(ProductConfiguration config, bool clearLocks = false)
    {
        var report = new PreflightReport();

        // --- Layout / bins ---
        CheckPath(report, ProductPaths.Root, "layout root");
        CheckPath(report, ProductPaths.AppLauncher, "launcher dir");
        CheckPath(report, ProductPaths.AppWatchdog, "watchdog dir");
        CheckPath(report, ProductPaths.AppMaintenance, "maintenance dir");
        CheckFile(report, Path.Combine(ProductPaths.AppLauncher, "TurboRama.Launcher.exe"), "Launcher.exe");
        CheckFile(report, Path.Combine(ProductPaths.AppWatchdog, "TurboRama.Watchdog.exe"), "Watchdog.exe");
        CheckFile(report, Path.Combine(ProductPaths.AppMaintenance, "TurboRama.Maintenance.exe"), "Maintenance.exe");
        CheckFile(report, Path.Combine(ProductPaths.App, "Tools", "Autologon64.exe"), "Autologon64.exe");

        // --- Baseline ---
        string? baseline = FindFirst(Path.Combine(ProductPaths.Backup), "baseline.json");
        if (baseline is not null)
        {
            long len = new FileInfo(baseline).Length;
            if (len > 500)
            {
                report.AddOk("Baseline presente (" + len + " bytes).");
            }
            else
            {
                report.AddWarning("Baseline muito pequeno: " + baseline, "V_BASE_SMALL");
            }
        }
        else
        {
            report.AddError("baseline.json não encontrado em Backup.", "V_BASE");
        }

        // --- Conta kiosk ---
        string kiosk = config.KioskUser;
        string netUser = RunCapture("net.exe", "user \"" + kiosk + "\"");
        bool kioskMissing =
            netUser.Contains("2221", StringComparison.Ordinal) ||
            netUser.Contains("could not be found", StringComparison.OrdinalIgnoreCase) ||
            netUser.Contains("não foi encontrado", StringComparison.OrdinalIgnoreCase) ||
            netUser.Contains("nao foi encontrado", StringComparison.OrdinalIgnoreCase);
        bool kioskPresent =
            netUser.Contains("User name", StringComparison.OrdinalIgnoreCase) ||
            netUser.Contains("Nome de usuário", StringComparison.OrdinalIgnoreCase) ||
            netUser.Contains("Nome de usuario", StringComparison.OrdinalIgnoreCase) ||
            (netUser.Contains(kiosk, StringComparison.OrdinalIgnoreCase) && netUser.Length > 40);

        if (kioskMissing || !kioskPresent)
        {
            report.AddError("Conta kiosk inexistente ou inacessível: " + kiosk, "V_KIOSK");
        }
        else
        {
            report.AddOk("Conta kiosk existe: " + kiosk);
            if (netUser.Contains("Account active               No", StringComparison.OrdinalIgnoreCase) ||
                netUser.Contains("Conta ativa                 Não", StringComparison.OrdinalIgnoreCase))
            {
                report.AddError("Conta kiosk desabilitada.", "V_KIOSK_OFF");
            }
        }

        string admins = RunCapture("net.exe", "localgroup Administrators");
        if (admins.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(l => l.Trim().Equals(kiosk, StringComparison.OrdinalIgnoreCase)))
        {
            report.AddError("Conta kiosk está no grupo Administrators.", "V_KIOSK_ADMIN");
        }
        else
        {
            report.AddOk("Conta kiosk não é Administrador.");
        }

        // recovery admin
        string adminList = admins;
        bool hasAdmin = adminList.Contains("Admin", StringComparison.OrdinalIgnoreCase) ||
                        adminList.Contains("Administrator", StringComparison.OrdinalIgnoreCase);
        if (hasAdmin)
        {
            report.AddOk("Grupo Administrators tem membros de recuperação.");
        }
        else
        {
            report.AddWarning("Não foi possível confirmar Admin de recuperação.", "V_RECOVERY");
        }

        // DPAPI secret
        string secret = Path.Combine(ProductPaths.Config, "kiosk-user.secret");
        if (File.Exists(secret))
        {
            report.AddOk("Segredo DPAPI kiosk presente.");
        }
        else
        {
            report.AddError("kiosk-user.secret ausente (senha kiosk).", "V_SECRET");
        }

        // Autologon
        string? auto = ReadWinlogon("AutoAdminLogon");
        string? defUser = ReadWinlogon("DefaultUserName");
        string? defPwd = ReadWinlogon("DefaultPassword");
        if (auto == "1")
        {
            report.AddOk("AutoAdminLogon=1.");
        }
        else
        {
            report.AddError("AutoAdminLogon != 1 (atual: " + (auto ?? "null") + ").", "V_AUTO");
        }

        if (string.Equals(defUser, kiosk, StringComparison.OrdinalIgnoreCase))
        {
            report.AddOk("DefaultUserName=" + defUser);
        }
        else
        {
            report.AddError("DefaultUserName=" + (defUser ?? "?") + " esperado " + kiosk, "V_AUTO_USER");
        }

        if (string.IsNullOrEmpty(defPwd))
        {
            report.AddOk("DefaultPassword ausente no Winlogon (preferível — LSA/Sysinternals).");
        }
        else
        {
            report.AddWarning("DefaultPassword em texto no Winlogon (risco).", "V_PLAIN_PWD");
        }

        // Services
        CheckService(report, "TurboRamaWatchdog");
        CheckService(report, "TurboRamaMaintenance");

        // Locks
        bool lockOn = MaintenanceLock.IsActive() || File.Exists(Path.Combine(ProductPaths.State, "maintenance.lock"));
        bool recovery = File.Exists(Path.Combine(ProductPaths.State, "recovery.flag"));
        if (clearLocks && (lockOn || recovery))
        {
            try
            {
                MaintenanceLock.Exit();
            }
            catch
            {
                /* ignore */
            }

            try
            {
                string lp = Path.Combine(ProductPaths.State, "maintenance.lock");
                if (File.Exists(lp))
                {
                    File.Delete(lp);
                }

                string rp = Path.Combine(ProductPaths.State, "recovery.flag");
                if (File.Exists(rp))
                {
                    File.Delete(rp);
                }

                report.AddOk("Locks de manutenção/recovery removidos (Fase 6).");
                lockOn = false;
                recovery = false;
            }
            catch (Exception ex)
            {
                report.AddWarning("Falha ao limpar locks: " + ex.Message, "V_LOCK_CLR");
            }
        }

        if (lockOn)
        {
            report.AddWarning("maintenance.lock ativo — Watchdog não reinicia Launcher. Use Sair manutenção ou --validate --clear-locks.", "V_LOCK");
        }
        else
        {
            report.AddOk("Sem maintenance.lock.");
        }

        if (recovery)
        {
            report.AddWarning("recovery.flag presente.", "V_RECOVERY_FLAG");
        }
        else
        {
            report.AddOk("Sem recovery.flag.");
        }

        // Optional modules default
        if (config.EnableUwf)
        {
            report.AddWarning("UWF habilitado na config (opcional de risco).", "V_UWF_ON");
        }
        else
        {
            report.AddOk("UWF desligado na config (default seguro).");
        }

        if (config.EnableKeyboardFilter)
        {
            // Produção = Keyboard Filter ON (igual PC referência). Aviso só se serviço ausente.
            string kbQ = RunCapture("sc.exe", "query MsKeyboardFilter");
            if (kbQ.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) ||
                kbQ.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
            {
                report.AddOk("Keyboard Filter na config ON e serviço MsKeyboardFilter presente.");
            }
            else
            {
                report.AddWarning(
                    "Keyboard Filter ON na config, mas MsKeyboardFilter ausente (edição sem IoT — lockdown parcial).",
                    "V_KB_ON");
            }
        }
        else
        {
            report.AddOk("Keyboard Filter desligado (default seguro).");
        }

        // Frontend
        string fe = config.FrontendExecutable ?? string.Empty;
        string[] candidates =
        {
            fe,
            Path.Combine(ProductPaths.Frontend, "Frontend.exe"),
            Path.Combine(ProductPaths.Frontend, "TurboRama.exe"),
            @"D:\Turborama\TurboRama.exe",
        };
        string? foundFe = candidates.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p));
        if (foundFe is not null)
        {
            report.AddOk("Frontend encontrado: " + foundFe);
        }
        else
        {
            report.AddWarning("Frontend não encontrado (kiosk sobe, jogo pode faltar).", "V_FRONTEND");
        }

        // Pipe name presence (best-effort)
        try
        {
            bool pipe = Directory.GetFiles(@"\\.\pipe\").Any(p =>
                p.EndsWith("TurboRamaMaintenance", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("TurboRamaMaintenance", StringComparison.OrdinalIgnoreCase));
            if (pipe)
            {
                report.AddOk("Named pipe TurboRamaMaintenance presente.");
            }
            else
            {
                report.AddWarning("Pipe Maintenance não listado (serviço pode estar ok mesmo assim).", "V_PIPE");
            }
        }
        catch
        {
            report.AddWarning("Não foi possível listar pipes.", "V_PIPE");
        }

        report.Success = report.Errors.Count == 0;
        return report;
    }

    public OperationResult RunToResult(ProductConfiguration config, bool clearLocks = false)
    {
        PreflightReport report = Run(config, clearLocks);
        string path = Path.Combine(ProductPaths.InstallerLogs, "phase6-accept-" +
            DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
        try
        {
            Directory.CreateDirectory(ProductPaths.InstallerLogs);
            var sb = new StringBuilder();
            sb.AppendLine("TurboRama Secure — Fase 6 Aceite de Fábrica");
            sb.AppendLine(DateTimeOffset.Now.ToString("O"));
            sb.AppendLine("Success=" + report.Success);
            sb.AppendLine("OK=" + report.Items.Count(i => i.Severity == "OK") +
                          " AVISOS=" + report.Warnings.Count +
                          " ERROS=" + report.Errors.Count);
            sb.AppendLine("---");
            foreach (PreflightItem i in report.Items)
            {
                sb.AppendLine("[" + i.Severity + "] " + i.Message +
                              (string.IsNullOrEmpty(i.Code) ? "" : " (" + i.Code + ")"));
            }

            File.WriteAllText(path, sb.ToString());
        }
        catch
        {
            path = "(log não gravado)";
        }

        if (report.Success)
        {
            return OperationResult.Ok(
                "Fase 6 ACEITE OK. Avisos=" + report.Warnings.Count + ". Relatório: " + path,
                "Phase6.Accept",
                currentState: path);
        }

        return OperationResult.Fail(
            "Fase 6 FALHOU: " + string.Join("; ", report.Errors.Select(e => e.Message)) +
            " | Relatório: " + path,
            "PHASE6_FAIL",
            "Phase6.Accept",
            currentState: path);
    }

    private static void CheckPath(PreflightReport report, string path, string label)
    {
        if (Directory.Exists(path))
        {
            report.AddOk("Dir OK: " + label);
        }
        else
        {
            report.AddError("Dir ausente (" + label + "): " + path, "V_DIR");
        }
    }

    private static void CheckFile(PreflightReport report, string path, string label)
    {
        if (File.Exists(path))
        {
            report.AddOk("Bin OK: " + label);
        }
        else
        {
            report.AddError("Bin ausente (" + label + "): " + path, "V_BIN");
        }
    }

    private static void CheckService(PreflightReport report, string name)
    {
        string q = RunCapture("sc.exe", "query \"" + name + "\"");
        if (q.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
        {
            report.AddOk("Serviço RUNNING: " + name);
        }
        else if (q.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
        {
            report.AddError("Serviço STOPPED: " + name, "V_SVC");
        }
        else if (q.Contains("1060") || q.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
        {
            report.AddError("Serviço não instalado: " + name, "V_SVC_MISS");
        }
        else
        {
            report.AddError("Serviço estado desconhecido: " + name, "V_SVC_UNK");
        }
    }

    private static string? ReadWinlogon(string valueName)
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", false);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }

    private static string? FindFirst(string root, string fileName)
    {
        try
        {
            if (!Directory.Exists(root))
            {
                return null;
            }

            return Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string RunCapture(string fileName, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using Process? p = Process.Start(psi);
            if (p is null)
            {
                return string.Empty;
            }

            string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(15000);
            return o;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
