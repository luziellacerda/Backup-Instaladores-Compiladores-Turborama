namespace TurboRama.Core.Steps;

/// <summary>Estágios da máquina de estados do instalador.</summary>
public enum InstallationStage
{
    NotStarted = 0,
    PreflightValidated = 1,
    BaselineCaptured = 2,
    FilesInstalled = 3,
    KioskUserCreated = 4,
    ShellConfigured = 5,
    SecurityApplied = 6,
    WatchdogInstalled = 7,
    FinalValidation = 8,
    Installed = 9,
    Failed = 100,
    RollingBack = 101,
    RolledBack = 102
}
