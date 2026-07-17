using System.Collections.Generic;

namespace TurboramaRomLinker.Models
{
    public sealed class DriveScanResult
    {
        public DriveScanResult()
        {
            ValidSystems = new SortedSet<string>(System.StringComparer.OrdinalIgnoreCase);
            Items = new List<RomLinkPlanItem>();
            Messages = new List<string>();
        }

        public string MasterRoot { get; set; }
        public string MasterConfigPath { get; set; }
        public string MasterRomsPath { get; set; }
        public SortedSet<string> ValidSystems { get; private set; }
        public List<RomLinkPlanItem> Items { get; private set; }
        public List<string> Messages { get; private set; }

        public int CreateCount
        {
            get
            {
                int count = 0;
                foreach (RomLinkPlanItem item in Items)
                {
                    if (item.CanCreate) count++;
                }
                return count;
            }
        }
    }
}
