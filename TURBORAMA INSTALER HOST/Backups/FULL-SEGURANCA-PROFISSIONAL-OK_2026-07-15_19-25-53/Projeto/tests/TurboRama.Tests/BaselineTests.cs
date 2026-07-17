using TurboRama.Core.Baseline;
using TurboRama.Core.Manifest;
using TurboRama.Windows.Registry;
using Microsoft.Win32;
using Xunit;

namespace TurboRama.Tests;

public class BaselineTests
{
    [Fact]
    public void RegistryValueHelper_FormatPath_HKLM()
    {
        string path = RegistryValueHelper.FormatPath(RegistryHive.LocalMachine, @"SOFTWARE\Test");
        Assert.Equal(@"HKLM\SOFTWARE\Test", path);
    }

    [Fact]
    public void RegistryValueHelper_TryParsePath_Works()
    {
        bool ok = RegistryValueHelper.TryParsePath(@"HKLM\SOFTWARE\Microsoft", out RegistryHive hive, out string sub);
        Assert.True(ok);
        Assert.Equal(RegistryHive.LocalMachine, hive);
        Assert.Equal(@"SOFTWARE\Microsoft", sub);
    }

    [Fact]
    public void ChangeManifest_AddChange_Counts()
    {
        var m = new ChangeManifest { InstallationId = Guid.NewGuid() };
        ChangeManifestStore.AddChange(m, "RegistryValue", "HKLM\\X", "a", "b", true, "Step");
        Assert.Single(m.Changes);
        Assert.Equal("Pending", m.Changes[0].RollbackStatus);
    }

    [Fact]
    public void BaselineHash_Sha256Text_Stable()
    {
        string a = BaselineHash.Sha256Text("turbo");
        string b = BaselineHash.Sha256Text("turbo");
        Assert.Equal(a, b);
        Assert.Equal(64, a.Length);
    }
}
