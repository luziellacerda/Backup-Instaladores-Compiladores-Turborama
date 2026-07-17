using TurboRama.Core.Paths;

namespace TurboRama.Core.State;

/// <summary>
/// Comunicação ES scripts → Launcher sobre o que o jogador pediu no menu.
/// Ficheiro: C:\TurboRama\State\power-request.txt
/// </summary>
public enum PowerRequestKind
{
    /// <summary>Sem ficheiro — crash, fecho inesperado, ou saída sem script.</summary>
    None = 0,
    /// <summary>Menu Desligar — splash + power off Windows.</summary>
    Shutdown = 1,
    /// <summary>Menu Reiniciar — splash + reboot Windows.</summary>
    Reboot = 2,
    /// <summary>Menu Sair — só fecha o frontend; Launcher relança o jogo.</summary>
    Quit = 3,
}

public static class PowerRequestStore
{
    public static string FilePath => ProductPaths.PowerRequestFile;

    /// <summary>Escreve o pedido (chamado pelos scripts .bat do EmulationStation).</summary>
    public static void Write(PowerRequestKind kind)
    {
        if (kind == PowerRequestKind.None)
        {
            Clear();
            return;
        }

        try
        {
            Directory.CreateDirectory(ProductPaths.State);
            string token = kind switch
            {
                PowerRequestKind.Shutdown => "shutdown",
                PowerRequestKind.Reboot => "reboot",
                PowerRequestKind.Quit => "quit",
                _ => "quit"
            };
            File.WriteAllText(FilePath, token + Environment.NewLine);
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>Lê e apaga o pedido (uma vez por sessão do frontend).</summary>
    public static PowerRequestKind Consume()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return PowerRequestKind.None;
            }

            string raw = File.ReadAllText(FilePath).Trim();
            try
            {
                File.Delete(FilePath);
            }
            catch
            {
                // ignore
            }

            return Parse(raw);
        }
        catch
        {
            return PowerRequestKind.None;
        }
    }

    public static PowerRequestKind Peek()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return PowerRequestKind.None;
            }

            return Parse(File.ReadAllText(FilePath).Trim());
        }
        catch
        {
            return PowerRequestKind.None;
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static PowerRequestKind Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return PowerRequestKind.None;
        }

        string t = raw.Split(new[] { '\r', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries)[0]
            .Trim()
            .ToLowerInvariant();

        return t switch
        {
            "shutdown" or "poweroff" or "desligar" or "off" => PowerRequestKind.Shutdown,
            "reboot" or "restart" or "reiniciar" => PowerRequestKind.Reboot,
            "quit" or "exit" or "sair" => PowerRequestKind.Quit,
            _ => PowerRequestKind.None
        };
    }
}
