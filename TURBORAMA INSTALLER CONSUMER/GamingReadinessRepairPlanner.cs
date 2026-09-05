using System;
using System.Collections.Generic;
using System.Linq;

namespace InstallerHost
{
	internal sealed class GamingReadinessRepairPlan
	{
		public GamingReadinessRepairPlan(
			GamingRuntimeInstallSelection selection,
			IEnumerable<RuntimeComponentStatus> repairable,
			int manualActionCount)
		{
			Selection = selection ?? new GamingRuntimeInstallSelection();
			RepairableComponents = (repairable ?? Enumerable.Empty<RuntimeComponentStatus>()).ToArray();
			ManualActionCount = Math.Max(0, manualActionCount);
		}

		public GamingRuntimeInstallSelection Selection { get; private set; }
		public RuntimeComponentStatus[] RepairableComponents { get; private set; }
		public int RepairableComponentCount { get { return RepairableComponents.Length; } }
		public int ManualActionCount { get; private set; }
		public bool CanRepair { get { return RepairableComponentCount > 0; } }
	}

	internal static class GamingReadinessRepairPlanner
	{
		public static GamingReadinessRepairPlan Create(GamingReadinessProfile profile)
		{
			GamingRuntimeInstallSelection selection = new GamingRuntimeInstallSelection();
			List<RuntimeComponentStatus> repairable = new List<RuntimeComponentStatus>();
			int manualActions = 0;

			if (profile == null)
				return new GamingReadinessRepairPlan(selection, repairable, manualActions);

			foreach (RuntimeComponentStatus status in profile.RuntimeStatuses)
			{
				if (status == null || status.Component == null || !status.NeedsAction)
					continue;

				GamingRuntimeComponent component = status.Component;
				bool supportedTier = component.Tier == GamingRuntimeTier.Required ||
					component.Tier == GamingRuntimeTier.Recommended;
				bool supportedBundle = supportedTier && component.CanInstallOffline && status.BundleAvailable;
				if (!supportedBundle)
				{
					manualActions++;
					continue;
				}

				if (string.Equals(component.Id, "directx-june-2010", StringComparison.OrdinalIgnoreCase))
				{
					selection.InstallDirectXLegacy = true;
					repairable.Add(status);
					continue;
				}

				if (component.Category == GamingRuntimeCategory.MicrosoftRuntime || component.IsLegacy)
				{
					selection.InstallMicrosoftRuntimeStack = true;
					repairable.Add(status);
					continue;
				}

				manualActions++;
			}

			selection.AllowedComponentIds = repairable
				.Select(status => status.Component.Id)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray();
			manualActions += profile.Findings.Count(finding => finding != null &&
				(finding.State == GamingReadinessState.Attention || finding.State == GamingReadinessState.Blocked));
			return new GamingReadinessRepairPlan(selection, repairable, manualActions);
		}
	}
}
