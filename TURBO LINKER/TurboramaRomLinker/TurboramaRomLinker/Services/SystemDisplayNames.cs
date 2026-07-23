using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TurboramaRomLinker.Services
{
    /// <summary>
    /// Nomes profissionais de sistemas para a UI (pasta técnica continua sendo o id).
    /// Prioridade: fullname do es_systems.cfg → mapa embutido → formatação legível.
    /// </summary>
    public static class SystemDisplayNames
    {
        private static readonly Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Nintendo
            { "nes", "Nintendo Entertainment System" },
            { "famicom", "Nintendo Family Computer" },
            { "snes", "Super Nintendo" },
            { "sfc", "Super Famicom" },
            { "n64", "Nintendo 64" },
            { "gamecube", "Nintendo GameCube" },
            { "gc", "Nintendo GameCube" },
            { "wii", "Nintendo Wii" },
            { "wiiu", "Nintendo Wii U" },
            { "switch", "Nintendo Switch" },
            { "gb", "Game Boy" },
            { "gbc", "Game Boy Color" },
            { "gba", "Game Boy Advance" },
            { "nds", "Nintendo DS" },
            { "3ds", "Nintendo 3DS" },
            { "virtualboy", "Virtual Boy" },
            { "pokemini", "Pokémon Mini" },
            { "sufami", "Sufami Turbo" },
            { "satellaview", "Satellaview" },

            // Sony
            { "psx", "PlayStation" },
            { "ps1", "PlayStation" },
            { "ps2", "PlayStation 2" },
            { "ps3", "PlayStation 3" },
            { "ps4", "PlayStation 4" },
            { "ps5", "PlayStation 5" },
            { "psp", "PlayStation Portable" },
            { "psvita", "PlayStation Vita" },
            { "ps2_cd", "PlayStation 2 (CD)" },
            { "ps2_dvd", "PlayStation 2 (DVD)" },

            // Sega
            { "mastersystem", "Sega Master System" },
            { "sms", "Sega Master System" },
            { "megadrive", "Sega Mega Drive" },
            { "genesis", "Sega Genesis" },
            { "sega32x", "Sega 32X" },
            { "segacd", "Sega CD / Mega-CD" },
            { "megacd", "Sega Mega-CD" },
            { "saturn", "Sega Saturn" },
            { "dreamcast", "Sega Dreamcast" },
            { "gamegear", "Sega Game Gear" },
            { "sg1000", "Sega SG-1000" },
            { "naomi", "Sega NAOMI" },
            { "naomi2", "Sega NAOMI 2" },
            { "atomiswave", "Sammy Atomiswave" },
            { "model2", "Sega Model 2" },
            { "model3", "Sega Model 3" },
            { "hikaru", "Sega Hikaru" },

            // Microsoft
            { "xbox", "Xbox" },
            { "xbox360", "Xbox 360" },
            { "xboxone", "Xbox One" },
            { "xboxseries", "Xbox Series X|S" },

            // Atari / outros
            { "atari2600", "Atari 2600" },
            { "atari5200", "Atari 5200" },
            { "atari7800", "Atari 7800" },
            { "atarilynx", "Atari Lynx" },
            { "lynx", "Atari Lynx" },
            { "jaguar", "Atari Jaguar" },
            { "atarijaguar", "Atari Jaguar" },
            { "atarist", "Atari ST" },
            { "neogeo", "Neo Geo AES / MVS" },
            { "neogeocd", "Neo Geo CD" },
            { "ngp", "Neo Geo Pocket" },
            { "ngpc", "Neo Geo Pocket Color" },
            { "pcengine", "PC Engine" },
            { "tg16", "TurboGrafx-16" },
            { "supergrafx", "PC Engine SuperGrafx" },
            { "pcfx", "PC-FX" },
            { "wonderswan", "WonderSwan" },
            { "wonderswancolor", "WonderSwan Color" },
            { "3do", "3DO Interactive Multiplayer" },
            { "amigacd32", "Amiga CD32" },
            { "amiga", "Commodore Amiga" },
            { "c64", "Commodore 64" },
            { "zxspectrum", "ZX Spectrum" },
            { "msx", "MSX" },
            { "msx1", "MSX" },
            { "msx2", "MSX2" },
            { "pc98", "NEC PC-98" },
            { "x68000", "Sharp X68000" },
            { "dos", "MS-DOS" },
            { "windows", "Windows" },
            { "pc", "PC" },
            { "scummvm", "ScummVM" },
            { "steam", "Steam" },
            { "arcade", "Arcade" },
            { "mame", "MAME Arcade" },
            { "fbneo", "FinalBurn Neo" },
            { "fba", "FinalBurn Alpha" },
            { "cps1", "Capcom CPS-1" },
            { "cps2", "Capcom CPS-2" },
            { "cps3", "Capcom CPS-3" },
            { "cave", "CAVE Arcade" },
            { "daphne", "LaserDisc / Daphne" },
            { "doom", "Doom" },
            { "ports", "Game Ports" },
            { "openbor", "OpenBOR" },
            { "easyrpg", "EasyRPG" },
            { "pico8", "PICO-8" },
            { "tic80", "TIC-80" },
            { "solarus", "Solarus" },
            { "lutro", "Lutro" },
            { "imageviewer", "Image Viewer" },
            { "moonlight", "Moonlight" },
            { "android", "Android" },
            { "n3ds", "Nintendo 3DS" },
            { "gw", "Game & Watch" },
            { "vectrex", "Vectrex" },
            { "colecovision", "ColecoVision" },
            { "intellivision", "Intellivision" },
            { "odyssey2", "Magnavox Odyssey²" },
            { "channelf", "Fairchild Channel F" },
            { "supervision", "Watara Supervision" },
            { "palm", "Palm OS" },
            { "zx81", "Sinclair ZX81" },
            { "amstradcpc", "Amstrad CPC" },
            { "thomson", "Thomson MO/TO" },
            { "samcoupe", "SAM Coupé" },
            { "apple2", "Apple II" },
            { "macintosh", "Apple Macintosh" },
            { "fmtowns", "FM Towns" },
            { "pcenginecd", "PC Engine CD" },
            { "tg-cd", "TurboGrafx-CD" },
            { "cdi", "Philips CD-i" },
            { "vsmile", "VTech V.Smile" },
            { "gameandwatch", "Game & Watch" },
            { "prboom", "Doom (PrBoom)" },
            { "gzdoom", "GZDoom" },
        };

        /// <summary>
        /// Nome amigável para UI. systemName = id da pasta (psx, snes...).
        /// fullNameFromCatalog = &lt;fullname&gt; do es_systems, se existir.
        /// </summary>
        public static string Get(string systemName, string fullNameFromCatalog = null)
        {
            string id = (systemName ?? string.Empty).Trim();
            if (id.Length == 0) return "Sistema desconhecido";

            string fromCfg = CleanCatalogFullName(fullNameFromCatalog, id);
            if (!string.IsNullOrEmpty(fromCfg))
                return fromCfg;

            string mapped;
            if (Map.TryGetValue(id, out mapped) && !string.IsNullOrWhiteSpace(mapped))
                return mapped;

            // tenta sem underscores/hífens
            string key = id.Replace("_", "").Replace("-", "");
            if (Map.TryGetValue(key, out mapped) && !string.IsNullOrWhiteSpace(mapped))
                return mapped;

            return HumanizeFolderName(id);
        }

        private static string CleanCatalogFullName(string fullName, string id)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return null;
            string t = fullName.Trim();
            // fullname igual ao id técnico ou genérico demais → usa mapa
            if (string.Equals(t, id, StringComparison.OrdinalIgnoreCase))
                return null;
            if (t.Length <= 2) return null;
            // se o cfg só capitalizou (Snes) e temos nome melhor no mapa
            string mapped;
            if (Map.TryGetValue(id, out mapped) && mapped.Length > t.Length)
                return mapped;
            return t;
        }

        /// <summary>psx_cd → Psx Cd | game-boy → Game Boy</summary>
        private static string HumanizeFolderName(string id)
        {
            string s = id.Replace('_', ' ').Replace('-', ' ').Trim();
            if (s.Length == 0) return id;

            StringBuilder sb = new StringBuilder(s.Length + 8);
            bool newWord = true;
            foreach (char c in s)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] != ' ')
                        sb.Append(' ');
                    newWord = true;
                    continue;
                }
                if (newWord)
                {
                    sb.Append(char.ToUpper(c, CultureInfo.InvariantCulture));
                    newWord = false;
                }
                else
                    sb.Append(char.ToLower(c, CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }
}
