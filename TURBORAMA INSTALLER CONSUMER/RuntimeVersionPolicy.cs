using System;

namespace InstallerHost
{
	internal enum RuntimeVersionComparison
	{
		NotManaged,
		Current,
		Outdated,
		Unknown
	}

	/// <summary>
	/// Compara somente runtimes cujo payload incorporado representa uma versão
	/// mínima. O requisito vem do catálogo de integridade que acompanha o EXE;
	/// nenhuma versão de produto é duplicada no detector.
	/// </summary>
	internal static class RuntimeVersionPolicy
	{
		public static bool RequiresMinimumVersion(GamingRuntimeComponent component)
		{
			if (component == null)
			{
				return false;
			}

			return IsVisualCppKey(component.DetectionKey) ||
				IsDotNetDesktopKey(component.DetectionKey) ||
				IsDokanyKey(component.DetectionKey) ||
				IsWinFspKey(component.DetectionKey);
		}

		public static RuntimeVersionComparison Evaluate(
			GamingRuntimeComponent component,
			string detectedVersion,
			out string requiredProductVersion)
		{
			requiredProductVersion = string.Empty;
			if (!RequiresMinimumVersion(component))
			{
				return RuntimeVersionComparison.NotManaged;
			}
			if (string.IsNullOrWhiteSpace(component.BundleFileName))
			{
				return RuntimeVersionComparison.Unknown;
			}

			PrerequisitePayloadLock payload = PrerequisiteIntegrityCatalog.GetRequiredPayload(component.BundleFileName);
			requiredProductVersion = payload.productVersion ?? string.Empty;
			return Evaluate(component.DetectionKey, detectedVersion, requiredProductVersion);
		}

		internal static RuntimeVersionComparison Evaluate(
			string detectionKey,
			string detectedVersion,
			string requiredProductVersion)
		{
			if (IsVisualCppKey(detectionKey))
			{
				return CompareVisualCpp(detectedVersion, requiredProductVersion);
			}
			if (IsDotNetDesktopKey(detectionKey))
			{
				return CompareDotNetDesktop(detectedVersion, requiredProductVersion);
			}
			if (IsDokanyKey(detectionKey))
			{
				return CompareFourPart(detectedVersion, requiredProductVersion);
			}
			if (IsWinFspKey(detectionKey))
			{
				return CompareThreePart(detectedVersion, requiredProductVersion);
			}

			return RuntimeVersionComparison.NotManaged;
		}

		internal static bool HaveSameVersionFields(string first, string second, int fieldCount)
		{
			if (fieldCount != 3 && fieldCount != 4)
			{
				throw new ArgumentOutOfRangeException("fieldCount");
			}
			Version left = null;
			Version right = null;
			bool parsed = fieldCount == 4
				? TryParseFourPartVersion(first, out left) && TryParseFourPartVersion(second, out right)
				: TryParseAtLeastThreePartVersion(first, out left) && TryParseAtLeastThreePartVersion(second, out right);
			if (!parsed)
			{
				return false;
			}

			return left.Major == right.Major && left.Minor == right.Minor && left.Build == right.Build &&
				(fieldCount == 3 || left.Revision == right.Revision);
		}

		private static RuntimeVersionComparison CompareVisualCpp(string detectedVersion, string requiredProductVersion)
		{
			Version detected;
			Version required;
			if (!TryParseVisualCppVersion(detectedVersion, out detected) ||
				!TryParseFourPartVersion(requiredProductVersion, out required))
			{
				return RuntimeVersionComparison.Unknown;
			}

			return detected.CompareTo(required) >= 0
				? RuntimeVersionComparison.Current
				: RuntimeVersionComparison.Outdated;
		}

		private static RuntimeVersionComparison CompareDotNetDesktop(string detectedVersion, string requiredProductVersion)
		{
			Version detected;
			Version required;
			if (!TryParseAtLeastThreePartVersion(detectedVersion, out detected) ||
				!TryParseAtLeastThreePartVersion(requiredProductVersion, out required))
			{
				return RuntimeVersionComparison.Unknown;
			}

			int comparison = detected.Major.CompareTo(required.Major);
			if (comparison == 0)
			{
				comparison = detected.Minor.CompareTo(required.Minor);
			}
			if (comparison == 0)
			{
				comparison = detected.Build.CompareTo(required.Build);
			}

			return comparison >= 0
				? RuntimeVersionComparison.Current
				: RuntimeVersionComparison.Outdated;
		}

		private static RuntimeVersionComparison CompareFourPart(string detectedVersion, string requiredProductVersion)
		{
			Version detected;
			Version required;
			if (!TryParseFourPartVersion(detectedVersion, out detected) ||
				!TryParseFourPartVersion(requiredProductVersion, out required))
			{
				return RuntimeVersionComparison.Unknown;
			}

			return detected.CompareTo(required) >= 0
				? RuntimeVersionComparison.Current
				: RuntimeVersionComparison.Outdated;
		}

		private static RuntimeVersionComparison CompareThreePart(string detectedVersion, string requiredProductVersion)
		{
			Version detected;
			Version required;
			if (!TryParseAtLeastThreePartVersion(detectedVersion, out detected) ||
				!TryParseAtLeastThreePartVersion(requiredProductVersion, out required))
			{
				return RuntimeVersionComparison.Unknown;
			}

			int comparison = detected.Major.CompareTo(required.Major);
			if (comparison == 0)
			{
				comparison = detected.Minor.CompareTo(required.Minor);
			}
			if (comparison == 0)
			{
				comparison = detected.Build.CompareTo(required.Build);
			}

			return comparison >= 0
				? RuntimeVersionComparison.Current
				: RuntimeVersionComparison.Outdated;
		}

		private static bool TryParseVisualCppVersion(string value, out Version version)
		{
			version = null;
			string cleaned = (value ?? string.Empty).Trim();
			if (cleaned.StartsWith("v", StringComparison.OrdinalIgnoreCase))
			{
				cleaned = cleaned.Substring(1);
			}

			return TryParseFourPartVersion(cleaned, out version);
		}

		private static bool TryParseFourPartVersion(string value, out Version version)
		{
			version = null;
			Version parsed;
			if (!Version.TryParse((value ?? string.Empty).Trim(), out parsed) ||
				parsed.Build < 0 || parsed.Revision < 0)
			{
				return false;
			}

			version = parsed;
			return true;
		}

		private static bool TryParseAtLeastThreePartVersion(string value, out Version version)
		{
			version = null;
			Version parsed;
			if (!Version.TryParse((value ?? string.Empty).Trim(), out parsed) || parsed.Build < 0)
			{
				return false;
			}

			version = parsed;
			return true;
		}

		private static bool IsVisualCppKey(string detectionKey)
		{
			return string.Equals(detectionKey, "vc-modern-x64", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(detectionKey, "vc-modern-x86", StringComparison.OrdinalIgnoreCase);
		}

		private static bool IsDotNetDesktopKey(string detectionKey)
		{
			return string.Equals(detectionKey, "dotnet-desktop-8-x64", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(detectionKey, "dotnet-desktop-8-x86", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(detectionKey, "dotnet-desktop-10-x64", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(detectionKey, "dotnet-desktop-10-x86", StringComparison.OrdinalIgnoreCase);
		}

		private static bool IsDokanyKey(string detectionKey)
		{
			return string.Equals(detectionKey, "dokany", StringComparison.OrdinalIgnoreCase);
		}

		private static bool IsWinFspKey(string detectionKey)
		{
			return string.Equals(detectionKey, "winfsp", StringComparison.OrdinalIgnoreCase);
		}
	}
}
