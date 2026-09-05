using System;
using System.Text.RegularExpressions;

namespace InstallerHost
{
	internal static class DownloadDisplayMask
	{
		private const string CdnLabel = "LZ GAMES CDN";

		public static string Apply(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return text;
			}

			string masked = text;

			masked = Regex.Replace(
				masked,
				@"https?://(?:www\.)?retrobat\.ovh(/[^\s""']*)?",
				match => CdnLabel + (match.Groups[1].Success ? " |" + match.Groups[1].Value : string.Empty),
				RegexOptions.IgnoreCase);

			masked = Regex.Replace(
				masked,
				@"https?://github\.com/RetroBat-Official/([^\s/""']+)(/[^\s""']*)?",
				match => CdnLabel + " | " + match.Groups[1].Value + (match.Groups[2].Success ? match.Groups[2].Value : string.Empty),
				RegexOptions.IgnoreCase);

			masked = Regex.Replace(
				masked,
				@"https?://github\.com/[^/\s""']*RetroBat[^\s""']*",
				CdnLabel,
				RegexOptions.IgnoreCase);

			masked = Regex.Replace(masked, @"RetroBat-Official", "LZ GAMES", RegexOptions.IgnoreCase);
			masked = Regex.Replace(masked, @"RetroBat", "LZ GAMES", RegexOptions.IgnoreCase);
			masked = Regex.Replace(masked, @"retrobat\.ovh", "cdn.lzgames", RegexOptions.IgnoreCase);
			masked = Regex.Replace(masked, @"\bretrobat\b", "LZ GAMES", RegexOptions.IgnoreCase);

			return masked;
		}
	}
}