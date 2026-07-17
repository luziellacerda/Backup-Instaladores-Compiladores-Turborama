using TurboRama.Configuration;
using Xunit;

namespace TurboRama.Tests;

public class FactoryDefaultsTests
{
    [Fact]
    public void ResolveKioskPassword_Uses_Factory_Default()
    {
        var cfg = new ProductConfiguration { KioskPassword = null };
        Assert.Equal("Lz2026@$", FactoryDefaults.ResolveKioskPassword(cfg));
        Assert.Equal("Lz2026@$", FactoryDefaults.KioskPassword);
    }

    [Fact]
    public void ResolveKioskPassword_Uses_Config_Override()
    {
        var cfg = new ProductConfiguration { KioskPassword = "OutraSenha9!" };
        Assert.Equal("OutraSenha9!", FactoryDefaults.ResolveKioskPassword(cfg));
    }

    [Fact]
    public void ResolveKioskPassword_Ignores_Short_Override()
    {
        var cfg = new ProductConfiguration { KioskPassword = "abc" };
        Assert.Equal(FactoryDefaults.KioskPassword, FactoryDefaults.ResolveKioskPassword(cfg));
    }
}
