using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;
using TurboRama.Configuration;
using TurboRama.Core.Paths;
using TurboRama.Core.Results;

namespace TurboRama.Diagnostics;

/// <summary>
/// Pré-validação obrigatória antes de modificar o Windows (estudo §5) — expandida.
/// </summary>
public sealed class PreflightService
{
    public PreflightReport Run(ProductConfiguration config)
    {
        var report = new PreflightReport();

        // Admin
        if (!IsAdministrator())
        {
            report.AddError("Execute como Administrador.", "PF_ADMIN");
        }
        else
        {
            report.AddOk("Sessão Administrador.");
        }

        // Não kiosk session
        string current = Environment.UserName ?? string.Empty;
        if (current.Equals(config.KioskUser, StringComparison.OrdinalIgnoreCase))
        {
            report.AddError(
                "Logado como conta kiosk (" + config.KioskUser + "). Use conta técnica Admin.",
                "PF_KIOSK_SESSION");
        }
        else
        {
            report.AddOk("Sessão atual não é a conta kiosk (" + current + ").");
        }

        // Nome kiosk
        if (string.IsNullOrWhiteSpace(config.KioskUser) ||
            config.KioskUser.Equals("Administrator", StringComparison.OrdinalIgnoreCase) ||
            config.KioskUser.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            report.AddError("Nome de conta kiosk inválido: " + config.KioskUser, "PF_KIOSK_NAME");
        }
        else
        {
            report.AddOk("Nome kiosk válido: " + config.KioskUser);
        }

        // OS / arch
        try
        {
            string os = Environment.OSVersion.VersionString;
            bool x64 = Environment.Is64BitOperatingSystem;
            report.AddOk("Windows: " + os + " | OS64=" + x64 + " | Proc64=" + Environment.Is64BitProcess);
            if (!x64)
            {
                report.AddWarning("SO não é x64 — suporte principal é win-x64.", "PF_ARCH");
            }
        }
        catch (Exception ex)
        {
            report.AddWarning("Não leu versão OS: " + ex.Message, "PF_OS");
        }

        // Disco
        try
        {
            DriveInfo c = new("C");
            long freeMb = c.AvailableFreeSpace / (1024 * 1024);
            if (freeMb < 500)
            {
                report.AddError("Espaço livre em C: insuficiente (" + freeMb + " MB).", "PF_DISK");
            }
            else if (freeMb < 2048)
            {
                report.AddWarning("Pouco espaço em C: (" + freeMb + " MB). Recomendado > 2 GB.", "PF_DISK_LOW");
            }
            else
            {
                report.AddOk("Espaço livre em C: " + freeMb + " MB.");
            }
        }
        catch (Exception ex)
        {
            report.AddError("Falha ao checar disco: " + ex.Message, "PF_DISK");
        }

        // Outra conta Admin (recovery)
        if (!HasOtherAdmin(config.KioskUser))
        {
            report.AddError(
                "É obrigatória outra conta Administrador além da kiosk (recuperação).",
                "PF_NO_ADMIN");
        }
        else
        {
            report.AddOk("Conta administrativa de recuperação detectada.");
        }

        // BitLocker (aviso)
        string bl = RunCapture("manage-bde.exe", "-status C:");
        if (bl.Contains("Protection On", StringComparison.OrdinalIgnoreCase) ||
            bl.Contains("Proteção Ativada", StringComparison.OrdinalIgnoreCase))
        {
            report.AddWarning("BitLocker ativo em C: — planeje chaves de recuperação antes de mudanças profundas.", "PF_BITLOCKER");
        }
        else if (bl.Length > 10)
        {
            report.AddOk("BitLocker: status obtido (sem proteção on óbvia ou não aplicável).");
        }
        else
        {
            report.AddOk("BitLocker: manage-bde não retornou status (normal se indisponível).");
        }

        // UWF presence
        string uwf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "uwfmgr.exe");
        if (File.Exists(uwf))
        {
            report.AddWarning("uwfmgr.exe presente — UWF disponível (default do TurboRama = OFF).", "PF_UWF");
        }
        else
        {
            report.AddOk("UWF não presente nesta edição (normal Home/Pro).");
        }

        // Device Lockdown / Keyboard filter feature probe via dism (rápido, timeout)
        string dism = RunCapture("dism.exe", "/Online /Get-FeatureInfo /FeatureName:Client-DeviceLockdown");
        if (dism.Contains("State : Enabled", StringComparison.OrdinalIgnoreCase) ||
            dism.Contains("Estado : Habilitado", StringComparison.OrdinalIgnoreCase))
        {
            report.AddOk("Client-DeviceLockdown habilitado (Keyboard Filter possível).");
        }
        else if (dism.Contains("State : Disabled", StringComparison.OrdinalIgnoreCase) ||
                 dism.Contains("Disabled", StringComparison.OrdinalIgnoreCase))
        {
            report.AddOk("Client-DeviceLockdown presente/desabilitado (Filter opcional, default OFF).");
        }
        else
        {
            report.AddOk("Client-DeviceLockdown não confirmado (edição sem Embedded — Filter N/A).");
        }

        // Frontend
        if (!string.IsNullOrWhiteSpace(config.FrontendExecutable) && !File.Exists(config.FrontendExecutable))
        {
            string alt = Path.Combine(ProductPaths.Frontend, "Frontend.exe");
            string alt2 = Path.Combine(ProductPaths.Frontend, "TurboRama.exe");
            if (File.Exists(alt) || File.Exists(alt2) || File.Exists(@"D:\Turborama\TurboRama.exe"))
            {
                report.AddWarning("Frontend config ausente, mas candidato local existe.", "PF_FRONTEND_ALT");
            }
            else
            {
                report.AddWarning("Frontend não encontrado: " + config.FrontendExecutable, "PF_FRONTEND");
            }
        }
        else if (File.Exists(config.FrontendExecutable))
        {
            report.AddOk("Frontend encontrado: " + config.FrontendExecutable);
        }

        // .NET runtime hint
        string? dotnet = FindDotnetHost();
        if (dotnet is not null)
        {
            report.AddOk("dotnet host: " + dotnet);
        }
        else
        {
            report.AddWarning(
                "dotnet host não encontrado no PATH típico — runtime .NET 8 Desktop pode ser necessário no alvo.",
                "PF_DOTNET");
        }

        // Shell / autologon atuais
        try
        {
            using RegistryKey? wl = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", false);
            string shell = wl?.GetValue("Shell") as string ?? "?";
            string auto = wl?.GetValue("AutoAdminLogon") as string ?? "0";
            string defUser = wl?.GetValue("DefaultUserName") as string ?? "";
            report.AddOk("Winlogon atual: Shell=" + shell + " AutoAdminLogon=" + auto + " DefaultUser=" + defUser);
            if (!string.IsNullOrEmpty(wl?.GetValue("DefaultPassword") as string))
            {
                report.AddWarning("DefaultPassword presente em texto no Winlogon (risco).", "PF_PLAIN_PWD");
            }
        }
        catch (Exception ex)
        {
            report.AddWarning("Não leu Winlogon: " + ex.Message, "PF_WINLOGON");
        }

        // Serviços TurboRama já existentes
        foreach (string svc in new[] { "TurboRamaWatchdog", "TurboRamaMaintenance" })
        {
            string q = RunCapture("sc.exe", "query \"" + svc + "\"");
            if (q.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
            {
                report.AddWarning("Serviço já RUNNING: " + svc + " (reinstalação deve parar antes de copiar).", "PF_SVC_RUN");
            }
            else if (q.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
            {
                report.AddOk("Serviço já instalado (STOPPED): " + svc);
            }
            else if (q.Contains("1060") || q.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
            {
                report.AddOk("Serviço ainda não instalado: " + svc);
            }
            else
            {
                report.AddOk("Serviço " + svc + ": estado consultado.");
            }
        }

        // Instalação anterior / state
        if (File.Exists(ProductPaths.InstallationStateFile))
        {
            report.AddWarning("installation-state.json existe — instalação anterior/retomável.", "PF_PREV_STATE");
        }
        else
        {
            report.AddOk("Sem installation-state prévio.");
        }

        if (Directory.Exists(ProductPaths.Backup) &&
            Directory.EnumerateDirectories(ProductPaths.Backup).Any())
        {
            report.AddOk("Backup/ de instalações anteriores presente.");
        }

        // Destino gravável
        try
        {
            ProductPaths.EnsureLayout();
            string probe = Path.Combine(ProductPaths.State, ".preflight-write");
            File.WriteAllText(probe, DateTimeOffset.Now.ToString("O"));
            File.Delete(probe);
            report.AddOk("Permissão de escrita em C:\\TurboRama\\State OK.");
        }
        catch (Exception ex)
        {
            report.AddError("Sem permissão de escrita em C:\\TurboRama: " + ex.Message, "PF_PERM");
        }

        // Edição Windows
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string? product = key?.GetValue("ProductName") as string;
            string? release = key?.GetValue("DisplayVersion") as string ?? key?.GetValue("ReleaseId") as string;
            string? build = key?.GetValue("CurrentBuild") as string;
            report.AddOk("Edição: " + (product ?? "?") + " " + (release ?? "") + " build=" + (build ?? "?"));
        }
        catch (Exception ex)
        {
            report.AddWarning("Edição Windows: " + ex.Message, "PF_EDITION");
        }

        // BCD atual (rápido)
        string bcd = RunCapture("bcdedit.exe", "/enum {current}");
        if (bcd.Length > 20 && !bcd.Contains("TIMEOUT", StringComparison.OrdinalIgnoreCase))
        {
            report.AddOk("BCD {current} legível (enum ok).");
        }
        else
        {
            report.AddWarning("BCD enum limitado (execute como Admin se necessário).", "PF_BCD");
        }

        // System Restore
        try
        {
            OperationResult sr = TurboRama.Windows.Recovery.SystemRestoreHelper.ProbeAvailability();
            report.AddOk(sr.Message);
        }
        catch
        {
            report.AddOk("System Restore: não sondado.");
        }

        // Integridade do pacote
        try
        {
            OperationResult pack = PackageIntegrityService.VerifyNearBaseDirectory();
            if (pack.Success)
            {
                report.AddOk(pack.Message);
            }
            else
            {
                // Hash mismatch: ERRO; skip: OK
                report.AddError(pack.Message, "PF_PACK");
            }
        }
        catch (Exception ex)
        {
            report.AddWarning("Integridade pack: " + ex.Message, "PF_PACK_EX");
        }

        // Assinatura soft do UI
        try
        {
            string uiExe = Path.Combine(AppContext.BaseDirectory, "TurboRama.UI.exe");
            if (File.Exists(uiExe))
            {
                OperationResult sig = TurboRama.Windows.Security.AuthenticodeHelper.CheckFile(uiExe);
                report.AddOk(sig.Message);
            }
        }
        catch
        {
            /* ignore */
        }

        // MsKeyboardFilter
        string kb = RunCapture("sc.exe", "query MsKeyboardFilter");
        if (kb.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) ||
            kb.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
        {
            report.AddOk("MsKeyboardFilter presente no sistema (Filter opcional default OFF).");
        }
        else
        {
            report.AddOk("MsKeyboardFilter ausente/N/A.");
        }

        // Shell strategy probe
        try
        {
            var strat = TurboRama.Windows.Shell.ShellStrategyService.ProbeAndDescribe();
            report.AddOk("Shell strategy: " + strat.Mode + " — " + strat.Message);
        }
        catch
        {
            report.AddOk("Shell strategy: UserHive (padrão).");
        }

        // Tarefas TurboRama
        string tasks = RunCapture("schtasks.exe", "/Query /FO LIST");
        if (tasks.Contains("TurboRama", StringComparison.OrdinalIgnoreCase))
        {
            report.AddWarning("Há tarefas agendadas com nome TurboRama (ver baseline).", "PF_TASKS");
        }
        else
        {
            report.AddOk("Sem tarefas agendadas TurboRama óbvias.");
        }

        report.Success = report.Errors.Count == 0;
        return report;
    }

    private static bool HasOtherAdmin(string kioskUser)
    {
        string g = RunCapture("net.exe", "localgroup Administrators");
        if (string.IsNullOrWhiteSpace(g))
        {
            return true; // não bloquear se net falhar
        }

        foreach (string line in g.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string t = line.Trim();
            if (t.Length == 0 || t.StartsWith('-') || t.Contains("Alias", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Comment", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Members", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("command completed", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("comando", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (t.Equals(kioskUser, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (t.Equals("Guest", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // qualquer outro membro conta
            if (!t.Contains("----"))
            {
                return true;
            }
        }

        return false;
    }

    private static string? FindDotnetHost()
    {
        string[] c =
        {
            @"D:\tr-dotnet\dotnet.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe"),
        };
        return c.FirstOrDefault(File.Exists);
    }

    private static string RunCapture(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
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

            if (!p.WaitForExit(12_000))
            {
                try { p.Kill(true); } catch { /* ignore */ }
                return "TIMEOUT";
            }

            return (p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd()).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsAdministrator()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}

public sealed class PreflightReport
{
    public bool Success { get; set; }
    public List<PreflightItem> Items { get; } = new();
    public List<PreflightItem> Errors => Items.Where(i => i.Severity == "ERRO").ToList();
    public List<PreflightItem> Warnings => Items.Where(i => i.Severity == "AVISO").ToList();

    public void AddOk(string message) =>
        Items.Add(new PreflightItem { Severity = "OK", Message = message });

    public void AddWarning(string message, string code) =>
        Items.Add(new PreflightItem { Severity = "AVISO", Message = message, Code = code });

    public void AddError(string message, string code) =>
        Items.Add(new PreflightItem { Severity = "ERRO", Message = message, Code = code });

    public OperationResult ToOperationResult() =>
        Success
            ? OperationResult.Ok("Preflight OK (" + Items.Count + " checks).", "Preflight")
            : OperationResult.Fail(
                "Preflight falhou: " + string.Join("; ", Errors.Select(e => e.Message)),
                "PF_FAIL",
                "Preflight");
}

public sealed class PreflightItem
{
    public string Severity { get; set; } = "OK";
    public string Message { get; set; } = string.Empty;
    public string? Code { get; set; }
}
