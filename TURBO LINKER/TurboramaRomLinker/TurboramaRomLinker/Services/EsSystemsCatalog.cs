using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace TurboramaRomLinker.Services
{
    public sealed class EsSystemInfo
    {
        public string Name { get; set; }
        /// <summary>Nome amigável do es_systems (&lt;fullname&gt;), se existir.</summary>
        public string FullName { get; set; }
        public SortedSet<string> Extensions { get; private set; }

        public EsSystemInfo()
        {
            Extensions = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static class EsSystemsCatalog
    {
        public static Dictionary<string, EsSystemInfo> LoadSystems(string cfgPath)
        {
            if (string.IsNullOrWhiteSpace(cfgPath)) throw new ArgumentNullException("cfgPath");
            if (!File.Exists(cfgPath)) throw new FileNotFoundException("Arquivo es_systems.cfg não encontrado.", cfgPath);

            XDocument document = XDocument.Load(cfgPath);
            Dictionary<string, EsSystemInfo> systems = new Dictionary<string, EsSystemInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (XElement systemElement in document.Descendants("system"))
            {
                string rawPath = (systemElement.Element("path") != null ? systemElement.Element("path").Value : string.Empty).Trim();
                string systemName = ExtractSystemName(rawPath);
                string cfgName = (systemElement.Element("name") != null ? systemElement.Element("name").Value : string.Empty).Trim();
                string fullName = (systemElement.Element("fullname") != null ? systemElement.Element("fullname").Value : string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(systemName))
                    systemName = SafeSystemName(cfgName);

                if (string.IsNullOrWhiteSpace(systemName)) continue;

                EsSystemInfo info;
                if (!systems.TryGetValue(systemName, out info))
                {
                    info = new EsSystemInfo();
                    info.Name = systemName;
                    systems.Add(systemName, info);
                }

                if (!string.IsNullOrWhiteSpace(fullName)
                    && (string.IsNullOrWhiteSpace(info.FullName) || info.FullName.Length < fullName.Length))
                    info.FullName = fullName;

                string extensions = (systemElement.Element("extension") != null ? systemElement.Element("extension").Value : string.Empty).Trim();
                foreach (string extension in ParseExtensions(extensions))
                {
                    info.Extensions.Add(extension);
                }
            }

            // Compatibilidade com arquivos onde a busca anterior era só por <path>.
            foreach (XElement pathElement in document.Descendants("path"))
            {
                string rawPath = (pathElement.Value ?? string.Empty).Trim();
                string systemName = ExtractSystemName(rawPath);
                if (string.IsNullOrWhiteSpace(systemName)) continue;

                if (!systems.ContainsKey(systemName))
                {
                    EsSystemInfo info = new EsSystemInfo();
                    info.Name = systemName;
                    systems.Add(systemName, info);
                }
            }

            return systems;
        }

        public static SortedSet<string> LoadRomSystemNames(string cfgPath)
        {
            Dictionary<string, EsSystemInfo> systems = LoadSystems(cfgPath);
            return new SortedSet<string>(systems.Keys, StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> ParseExtensions(string rawExtensions)
        {
            if (string.IsNullOrWhiteSpace(rawExtensions)) yield break;

            string normalized = rawExtensions.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            string[] parts = normalized.Split(new[] { ' ', ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string clean = part.Trim().Trim('"', '\'');
                if (string.IsNullOrWhiteSpace(clean)) continue;
                if (clean == ".*" || clean == "*")
                {
                    yield return "*";
                    continue;
                }
                if (clean.StartsWith("*.", StringComparison.OrdinalIgnoreCase)) clean = clean.Substring(1);
                if (!clean.StartsWith(".", StringComparison.OrdinalIgnoreCase)) clean = "." + clean;
                yield return clean.ToLowerInvariant();
            }
        }

        private static string ExtractSystemName(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath)) return null;

            string normalized = rawPath.Replace('/', '\\').Trim();
            string marker = "roms\\";
            int index = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return null;

            string afterRoms = normalized.Substring(index + marker.Length).Trim('\\', '/', ' ', '\t', '\r', '\n');
            if (afterRoms.Length == 0) return null;

            string firstPart = afterRoms.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return SafeSystemName(firstPart);
        }

        private static string SafeSystemName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return value.Trim();
        }
    }
}
