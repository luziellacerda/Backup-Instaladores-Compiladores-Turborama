using System;
using System.Collections.Generic;

namespace TurboRama.Next
{
    public enum CheckState { Good, Warning, Missing, Unknown }

    public sealed class ReadinessCheck
    {
        public string Name { get; set; }
        public string Detail { get; set; }
        public string Action { get; set; }
        public CheckState State { get; set; }
    }

    public sealed class ReadinessSnapshot
    {
        public DateTime CapturedAtUtc { get; set; }
        public List<ReadinessCheck> Checks { get; private set; }
        public ReadinessSnapshot() { Checks = new List<ReadinessCheck>(); }
    }
}
