using System;
using System.Text.RegularExpressions;

namespace RetroBuild
{
	internal static class DownloadDisplayMask
	{
		private const string CdnLabel = "LZTURBO CDN";

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

			masked = Regex.Replace(masked, @"RetroBat-Official", "LZTURBO", RegexOptions.IgnoreCase);
			masked = Regex.Replace(masked, @"RetroBat", "LZTURBO", RegexOptions.IgnoreCase);
			masked = Regex.Replace(masked, @"retrobat\.ovh", "cdn.lzturbo", RegexOptions.IgnoreCase);
			masked = Regex.Replace(masked, @"\bretrobat\b", "lzturbo", RegexOptions.IgnoreCase);

			return masked;
		}
	}
}