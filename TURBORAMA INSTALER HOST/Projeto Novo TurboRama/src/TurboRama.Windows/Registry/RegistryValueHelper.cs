using System.Globalization;
using Microsoft.Win32;
using TurboRama.Core.Baseline;
using TurboRama.Core.Results;

namespace TurboRama.Windows.Registry;

/// <summary>
/// Captura e restaura valores de Registro com existência, tipo e vista 32/64.
/// Se não existia, rollback remove o valor (não grava string vazia).
/// </summary>
public static class RegistryValueHelper
{
    public static RegistryValueSnapshot Capture(
        RegistryHive hive,
        string subKeyPath,
        string valueName,
        RegistryView view)
    {
        var snap = new RegistryValueSnapshot
        {
            Path = FormatPath(hive, subKeyPath),
            Name = valueName,
            RegistryView = view.ToString(),
            Existed = false
        };

        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
            using RegistryKey? key = baseKey.OpenSubKey(subKeyPath, false);
            if (key is null)
            {
                return snap;
            }

            object? raw = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (raw is null && key.GetValueNames().All(n => !string.Equals(n, valueName, StringComparison.OrdinalIgnoreCase)))
            {
                // valor realmente inexistente
                return snap;
            }

            // GetValue pode retornar null para valor existente (vazio) — checar GetValueKind
            try
            {
                RegistryValueKind kind = key.GetValueKind(valueName);
                snap.Existed = true;
                snap.Kind = kind.ToString();
                snap.Value = SerializeValue(raw, kind);
            }
            catch (IOException)
            {
                snap.Existed = false;
            }
        }
        catch
        {
            // mantém Existed=false
        }

        return snap;
    }

    public static OperationResult Restore(RegistryValueSnapshot snap)
    {
        try
        {
            if (!TryParsePath(snap.Path, out RegistryHive hive, out string subKey))
            {
                return OperationResult.Fail("Caminho de registro inválido: " + snap.Path, "REG_PATH", "RegistryValueHelper.Restore");
            }

            if (!Enum.TryParse(snap.RegistryView, true, out RegistryView view))
            {
                view = RegistryView.Registry64;
            }

            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);

            if (!snap.Existed)
            {
                using RegistryKey? openKey = baseKey.OpenSubKey(subKey, true);
                if (openKey is not null)
                {
                    try
                    {
                        openKey.DeleteValue(snap.Name, throwOnMissingValue: false);
                    }
                    catch
                    {
                    }
                }

                return OperationResult.Ok(
                    "Valor removido (não existia no baseline): " + snap.Path + "\\" + snap.Name,
                    "RegistryValueHelper.Restore",
                    previousState: snap.Value,
                    currentState: "(deleted)");
            }

            using RegistryKey writeKey = baseKey.CreateSubKey(subKey, true)
                ?? throw new InvalidOperationException("Não foi possível abrir/criar " + subKey);

            if (!Enum.TryParse(snap.Kind, true, out RegistryValueKind kind))
            {
                kind = RegistryValueKind.String;
            }

            object value = DeserializeValue(snap.Value, kind);
            writeKey.SetValue(snap.Name, value, kind);

            return OperationResult.Ok(
                "Valor restaurado: " + snap.Path + "\\" + snap.Name,
                "RegistryValueHelper.Restore",
                currentState: snap.Value);
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(
                "Falha ao restaurar " + snap.Path + "\\" + snap.Name + ": " + ex.Message,
                "REG_RESTORE",
                "RegistryValueHelper.Restore",
                exception: ex);
        }
    }

    public static OperationResult SetValue(
        RegistryHive hive,
        string subKeyPath,
        string valueName,
        object value,
        RegistryValueKind kind,
        RegistryView view = RegistryView.Registry64)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
            using RegistryKey key = baseKey.CreateSubKey(subKeyPath, true)
                ?? throw new InvalidOperationException("CreateSubKey failed");
            key.SetValue(valueName, value, kind);
            return OperationResult.Ok("Registro gravado: " + FormatPath(hive, subKeyPath) + "\\" + valueName, "RegistryValueHelper.SetValue");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("Falha ao gravar registro: " + ex.Message, "REG_SET", "RegistryValueHelper.SetValue", exception: ex);
        }
    }

    public static OperationResult DeleteValue(
        RegistryHive hive,
        string subKeyPath,
        string valueName,
        RegistryView view = RegistryView.Registry64)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
            using RegistryKey? key = baseKey.OpenSubKey(subKeyPath, true);
            key?.DeleteValue(valueName, throwOnMissingValue: false);
            return OperationResult.Ok("Valor removido: " + valueName, "RegistryValueHelper.DeleteValue");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail("Falha ao remover valor: " + ex.Message, "REG_DEL", "RegistryValueHelper.DeleteValue", exception: ex);
        }
    }

    public static string FormatPath(RegistryHive hive, string subKeyPath) =>
        hive switch
        {
            RegistryHive.LocalMachine => @"HKLM\" + subKeyPath,
            RegistryHive.CurrentUser => @"HKCU\" + subKeyPath,
            RegistryHive.Users => @"HKU\" + subKeyPath,
            RegistryHive.ClassesRoot => @"HKCR\" + subKeyPath,
            _ => hive + @"\" + subKeyPath
        };

    public static bool TryParsePath(string path, out RegistryHive hive, out string subKey)
    {
        hive = RegistryHive.LocalMachine;
        subKey = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string p = path.Replace('/', '\\').Trim();
        int slash = p.IndexOf('\\');
        string root = slash >= 0 ? p[..slash] : p;
        subKey = slash >= 0 ? p[(slash + 1)..] : string.Empty;

        if (root.Equals("HKLM", StringComparison.OrdinalIgnoreCase) ||
            root.Equals("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase))
        {
            hive = RegistryHive.LocalMachine;
            return true;
        }

        if (root.Equals("HKCU", StringComparison.OrdinalIgnoreCase) ||
            root.Equals("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase))
        {
            hive = RegistryHive.CurrentUser;
            return true;
        }

        if (root.Equals("HKU", StringComparison.OrdinalIgnoreCase) ||
            root.Equals("HKEY_USERS", StringComparison.OrdinalIgnoreCase))
        {
            hive = RegistryHive.Users;
            return true;
        }

        return false;
    }

    private static string? SerializeValue(object? raw, RegistryValueKind kind)
    {
        if (raw is null)
        {
            return null;
        }

        return kind switch
        {
            RegistryValueKind.DWord => Convert.ToInt32(raw, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            RegistryValueKind.QWord => Convert.ToInt64(raw, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            RegistryValueKind.Binary => Convert.ToHexString((byte[])raw),
            RegistryValueKind.MultiString => string.Join("\n", (string[])raw),
            RegistryValueKind.ExpandString => raw.ToString(),
            _ => raw.ToString()
        };
    }

    private static object DeserializeValue(string? text, RegistryValueKind kind)
    {
        text ??= string.Empty;
        return kind switch
        {
            RegistryValueKind.DWord => int.Parse(text, CultureInfo.InvariantCulture),
            RegistryValueKind.QWord => long.Parse(text, CultureInfo.InvariantCulture),
            RegistryValueKind.Binary => Convert.FromHexString(text),
            RegistryValueKind.MultiString => text.Split('\n'),
            RegistryValueKind.ExpandString => text,
            _ => text
        };
    }
}
