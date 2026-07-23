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
        /// <summary>Id técnico da pasta (psx, snes...) — usado em links e paths.</summary>
        public string SystemName { get; set; }
        /// <summary>Nome profissional para UI (ex.: PlayStation, Super Nintendo).</summary>
        public string DisplayName { get; set; }
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
