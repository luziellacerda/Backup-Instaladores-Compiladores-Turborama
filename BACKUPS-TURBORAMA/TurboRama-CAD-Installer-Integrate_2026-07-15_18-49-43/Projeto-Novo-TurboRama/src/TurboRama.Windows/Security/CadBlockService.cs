using System.Management;
using TurboRama.Core.Results;
using TurboRama.Windows.Exec;
using TurboRama.Windows.Optional;
using WinReg = Microsoft.Win32;

namespace TurboRama.Windows.Security;

/// <summary>
/// Windows 10 IoT Enterprise LTSC: desativa Ctrl+Alt+Del de verdade
/// (Keyboard Filter / MsKeyboardFilter / WEKF) e esvazia o CAD se ainda abrir.
/// Menu TurboRama = Ctrl+End (Allowed no filtro; capturado pelo SecurityAgent).
/// </summary>
public static class CadBlockService
{
    public static OperationResult ApplySystemWide()
    {
        var notes = new List<string>();
        int ok = 0;

        notes.Add("Target=Windows 10 IoT Enterprise (Keyboard Filter)");

        // 0) Serviço MsKeyboardFilter = Automatic (sem sc start pré-reboot)
        OperationResult kfEnable = KeyboardFilterModuleService.Enable();
        notes.Add("KbFilter.Enable: " + kfEnable.Message);
        if (kfEnable.Success)
        {
            ok++;
        }

        // Só tenta start se já estiver Automatic e ainda Stopped (pós-reboot pode falhar 1x)
        EnsureKeyboardFilterAutoOnly(notes);

        // 1) Esvaziar CAD (políticas) — reforço se o filtro falhar a meio
        if (ApplyEmptyCad(WinReg.Registry.LocalMachine, notes))
        {
            ok++;
        }

        try
        {
            if (ApplyEmptyCad(WinReg.Registry.CurrentUser, notes))
            {
                ok++;
            }
        }
        catch (Exception ex)
        {
            notes.Add("HKCU: " + ex.Message);
        }

        // 2) Keyboard Filter registry (IoT — nomes oficiais do filtro)
        if (ApplyKeyboardFilterRegistry(notes))
        {
            ok++;
        }

        // 3) WEKF WMI (embedded) — bloqueia Ctrl+Alt+Del no filtro
        if (TryWekfBlockCtrlAltDel(notes))
        {
            ok++;
        }

        string msg = "CadBlock IoT ok=" + ok + " | " + string.Join("; ", notes);
        return ok > 0
            ? OperationResult.Ok(msg, "CadBlockService.ApplySystemWide")
            : OperationResult.Fail(msg, "CAD_BLOCK", "CadBlockService.ApplySystemWide");
    }

    private static bool ApplyEmptyCad(WinReg.RegistryKey root, List<string> notes)
    {
        try
        {
            using WinReg.RegistryKey? sys = root.CreateSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", true)
                ?? root.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Policies\System", true);
            if (sys == null)
            {
                return false;
            }

            sys.SetValue("DisableTaskMgr", 1, WinReg.RegistryValueKind.DWord);
            sys.SetValue("DisableChangePassword", 1, WinReg.RegistryValueKind.DWord);
            sys.SetValue("DisableLockWorkstation", 1, WinReg.RegistryValueKind.DWord);
            sys.SetValue("HideFastUserSwitching", 1, WinReg.RegistryValueKind.DWord);

            using WinReg.RegistryKey? exp = root.CreateSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", true)
                ?? root.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", true);
            exp?.SetValue("NoLogoff", 1, WinReg.RegistryValueKind.DWord);

            notes.Add("CAD esvaziado " + root.Name);
            return true;
        }
        catch (Exception ex)
        {
            notes.Add("EmptyCAD: " + ex.Message);
            return false;
        }
    }

    private static bool ApplyKeyboardFilterRegistry(List<string> notes)
    {
        try
        {
            using WinReg.RegistryKey? kf = WinReg.Registry.LocalMachine.CreateSubKey(
                @"SOFTWARE\Microsoft\Windows Embedded\KeyboardFilter", true);
            if (kf == null)
            {
                notes.Add("KeyboardFilter registry indisponível");
                return false;
            }

            // SAS bloqueado (nome exacto no IoT LTSC)
            kf.SetValue("Ctrl+Alt+Del", "Blocked", WinReg.RegistryValueKind.String);
            // Menu TurboRama (substituto) — custom allowed
            kf.SetValue("Ctrl+End", "Allowed", WinReg.RegistryValueKind.String);
            // Filtro activo também para administradores no kiosk de fábrica
            kf.SetValue("DisableKeyboardFilterForAdministrators", 0, WinReg.RegistryValueKind.DWord);

            // Nomes oficiais do Keyboard Filter IoT (ver regedit Windows Embedded)
            TrySet(kf, "Windows", "Blocked");       // tecla Win
            TrySet(kf, "Win+L", "Blocked");
            TrySet(kf, "Alt+F4", "Blocked");
            TrySet(kf, "Alt+Tab", "Blocked");
            TrySet(kf, "Ctrl+Esc", "Blocked");
            TrySet(kf, "Shift+Ctrl+Esc", "Blocked"); // Task Manager (nome IoT)

            notes.Add("KeyboardFilter registry: CAD=Blocked Ctrl+End=Allowed");
            return true;
        }
        catch (Exception ex)
        {
            notes.Add("KeyboardFilter reg: " + ex.Message);
            return false;
        }
    }

    private static void TrySet(WinReg.RegistryKey kf, string name, string value)
    {
        try
        {
            kf.SetValue(name, value, WinReg.RegistryValueKind.String);
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>
    /// WEKF_PredefinedKey (root\standardcimv2\embedded) — IoT Keyboard Filter WMI.
    /// Enabled=true significa tecla BLOQUEADA no filtro.
    /// </summary>
    private static bool TryWekfBlockCtrlAltDel(List<string> notes)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\standardcimv2\embedded",
                "SELECT * FROM WEKF_PredefinedKey");
            using ManagementObjectCollection results = searcher.Get();
            int hit = 0;
            foreach (ManagementBaseObject raw in results)
            {
                using var obj = (ManagementObject)raw;
                string id = (obj["Id"] as string) ?? "";
                bool isCad =
                    id.Contains("Ctrl+Alt+Del", StringComparison.OrdinalIgnoreCase) ||
                    id.Contains("Ctrl+Alt+Delete", StringComparison.OrdinalIgnoreCase) ||
                    id.Equals("CAD", StringComparison.OrdinalIgnoreCase) ||
                    (id.Contains("Alt+Del", StringComparison.OrdinalIgnoreCase) &&
                     id.Contains("Ctrl", StringComparison.OrdinalIgnoreCase));

                bool isCtrlEnd =
                    id.Contains("Ctrl+End", StringComparison.OrdinalIgnoreCase);

                if (isCad)
                {
                    obj["Enabled"] = true; // bloqueado
                    obj.Put();
                    hit++;
                    notes.Add("WEKF block: " + id);
                }
                else if (isCtrlEnd)
                {
                    obj["Enabled"] = false; // permitido
                    obj.Put();
                    notes.Add("WEKF allow: " + id);
                }
            }

            if (hit == 0)
            {
                notes.Add("WEKF: nenhum Id CAD (reg+reboot aplicam o bloqueio)");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            notes.Add("WEKF: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Garante Start=Automatic no registo. Não chama sc start antes do reboot
    /// (em IoT o SCM reverte AUTO→DEMAND se o serviço arranca sem o driver).
    /// </summary>
    private static void EnsureKeyboardFilterAutoOnly(List<string> notes)
    {
        try
        {
            using WinReg.RegistryKey? svc = WinReg.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\MsKeyboardFilter", true);
            svc?.SetValue("Start", 2, WinReg.RegistryValueKind.DWord); // AUTO
            ProcessRunner.Run("sc.exe", "config MsKeyboardFilter start= auto", operationName: "cad-kf-auto");
            notes.Add("MsKeyboardFilter=Automatic (sem start forçado; reboot activa filtro CAD)");
        }
        catch (Exception ex)
        {
            notes.Add("MsKeyboardFilter auto: " + ex.Message);
        }
    }
}
