using TurboRama.Core.Logging;
using TurboRama.Core.Results;
using TurboRama.Installation;
using Xunit;

namespace TurboRama.Tests;

public class OperationResultTests
{
    [Fact]
    public void Ok_Sets_Success()
    {
        OperationResult r = OperationResult.Ok("feito", "Test");
        Assert.True(r.Success);
        Assert.Equal("feito", r.Message);
        Assert.Equal("Test", r.OperationName);
    }

    [Fact]
    public void Fail_Sets_ErrorCode()
    {
        OperationResult r = OperationResult.Fail("x", "E1", "Op");
        Assert.False(r.Success);
        Assert.Equal("E1", r.ErrorCode);
    }

    [Fact]
    public void RedactSecrets_Hides_Password()
    {
        string raw = "user=a password=segredo123 pin=999999";
        string safe = FileTurboRamaLogger.RedactSecrets(raw);
        Assert.DoesNotContain("segredo123", safe);
        Assert.Contains("***", safe);
    }

    [Fact]
    public void InstallationState_CreateNew_Has_NotStarted()
    {
        InstallationState state = InstallationStateStore.CreateNew(Guid.NewGuid(), "KioskBasic", "2.0.0-alpha");
        Assert.Equal(Core.Steps.InstallationStage.NotStarted, state.CurrentStage);
        Assert.Empty(state.CompletedStages);
    }
}
