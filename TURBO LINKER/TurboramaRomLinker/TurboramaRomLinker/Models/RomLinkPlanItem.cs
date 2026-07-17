namespace TurboramaRomLinker.Models
{
    public enum RomLinkAction
    {
        CreateJunction,
        AlreadyExists,
        PreserveRealFolder,
        SkippedDuplicate,
        SkippedInvalidSystem,
        SkippedMasterFolder,
        Error
    }

    public sealed class RomLinkPlanItem
    {
        public string SystemName { get; set; }
        public string SourceDrive { get; set; }
        public string SourcePath { get; set; }
        public string LinkPath { get; set; }
        public RomLinkAction Action { get; set; }
        public string Message { get; set; }
        public bool Success { get; set; }

        public bool CanCreate
        {
            get { return Action == RomLinkAction.CreateJunction; }
        }
    }
}
