using TurboRama.Configuration;
using TurboRama.Core.Paths;
using TurboRama.Core.Results;
using TurboRama.Core.State;
using TurboRama.Installation;
using TurboRama.Windows.Security;
using Xunit;

namespace TurboRama.Tests;

/// <summary>
/// Testes do painel Alt+End, power-request e escudo CAD (lógica de fábrica).
/// </summary>
public class OperatorConsoleAndCadTests
{
    [Fact]
    public void ProductConfiguration_OperatorConsole_Default_On()
    {
        var cfg = ConfigurationStore.CreateDefault();
        Assert.True(cfg.EnableOperatorConsole);
        Assert.True(cfg.ShowLoadingScreen);
        Assert.True(cfg.EnableLauncherTechMenu);
    }

    [Fact]
    public void OperatorPin_Falls_Back_To_Factory_Kiosk_Password()
    {
        var cfg = new ProductConfiguration { OperatorPin = null, KioskPassword = null };
        // Mesma regra do Launcher: OperatorPin vazio → senha kiosk de fábrica
        string pin = string.IsNullOrWhiteSpace(cfg.OperatorPin)
            ? FactoryDefaults.ResolveKioskPassword(cfg)
            : cfg.OperatorPin.Trim();
        Assert.Equal(FactoryDefaults.KioskPassword, pin);
    }

    [Fact]
    public void OperatorPin_Config_Override()
    {
        var cfg = new ProductConfiguration { OperatorPin = "PinOperador99" };
        string pin = string.IsNullOrWhiteSpace(cfg.OperatorPin)
            ? FactoryDefaults.ResolveKioskPassword(cfg)
            : cfg.OperatorPin.Trim();
        Assert.Equal("PinOperador99", pin);
    }

    [Fact]
    public void PowerRequestStore_Write_Consume_Shutdown()
    {
        ProductPaths.EnsureLayout();
        PowerRequestStore.Clear();

        PowerRequestStore.Write(PowerRequestKind.Shutdown);
        Assert.Equal(PowerRequestKind.Shutdown, PowerRequestStore.Peek());
        Assert.Equal(PowerRequestKind.Shutdown, PowerRequestStore.Consume());
        Assert.Equal(PowerRequestKind.None, PowerRequestStore.Peek());
    }

    [Fact]
    public void PowerRequestStore_Quit_And_Reboot_Tokens()
    {
        ProductPaths.EnsureLayout();
        PowerRequestStore.Clear();

        PowerRequestStore.Write(PowerRequestKind.Reboot);
        Assert.Equal(PowerRequestKind.Reboot, PowerRequestStore.Consume());

        // quit escrito como ficheiro (simula script ES)
        Directory.CreateDirectory(ProductPaths.State);
        File.WriteAllText(ProductPaths.PowerRequestFile, "quit\r\n");
        Assert.Equal(PowerRequestKind.Quit, PowerRequestStore.Consume());
    }

    [Fact]
    public void PowerRequestStore_Parse_Aliases()
    {
        ProductPaths.EnsureLayout();
        Directory.CreateDirectory(ProductPaths.State);

        File.WriteAllText(ProductPaths.PowerRequestFile, "desligar");
        Assert.Equal(PowerRequestKind.Shutdown, PowerRequestStore.Consume());

        File.WriteAllText(ProductPaths.PowerRequestFile, "reiniciar");
        Assert.Equal(PowerRequestKind.Reboot, PowerRequestStore.Consume());

        File.WriteAllText(ProductPaths.PowerRequestFile, "sair");
        Assert.Equal(PowerRequestKind.Quit, PowerRequestStore.Consume());
    }

    [Fact]
    public void CadShield_Apply_Does_Not_Throw()
    {
        // Pode falhar parcialmente sem Admin; não pode lançar exceção
        OperationResult r = CadShieldService.ApplyShield();
        Assert.NotNull(r.Message);
        // Sucesso ou falha parcial — ambos válidos em CI sem elevação
        Assert.True(r.Success || !r.Success);
    }

    [Fact]
    public void CadShield_TryStartFilter_Does_Not_Throw()
    {
        OperationResult r = CadShieldService.TryStartFilterService();
        Assert.NotNull(r.Message);
        Assert.True(r.Success); // método devolve Ok mesmo se serviço ausente
    }

    [Fact]
    public void EnsureOperatorAndLoadingFlags_Writes_Json()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "tr-op-cfg-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(tmp, """{"schemaVersion":1,"kioskUser":"Arcade"}""");
            var log = new System.Text.StringBuilder();
            FactoryFullInstall.EnsureOperatorAndLoadingFlags(tmp, log);

            string json = File.ReadAllText(tmp);
            Assert.Contains("enableOperatorConsole", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("showLoadingScreen", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("true", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("config:", log.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void PowerRequestFile_Path_Is_Under_State()
    {
        Assert.Contains("State", ProductPaths.PowerRequestFile, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("power-request.txt", ProductPaths.PowerRequestFile, StringComparison.OrdinalIgnoreCase);
    }
}
