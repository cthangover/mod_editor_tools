using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Cthangover.Core.Mods;
using Cthangover.Core.Mods.Providers;
using Cthangover.Core.UI.Tool;
using Cthangover.Core.Utils;

namespace Cthangover.Tools.Services
{
    /// <summary>
    /// Static service for discovering, reading and writing world map zone files
    /// (<c>mods/&lt;mod&gt;/map/*.json</c>) from the map editor. Follows
    /// <see cref="ScenarioFileService"/>: reading goes through the mod file
    /// provider abstraction so zip mods are visible, writing is deliberately
    /// limited to folder-backed mods because zip archives are read-only.
    /// <para/>
    /// New files are created through the provider's <c>RootPath</c> rather than
    /// <c>GetFileSystemPath</c>, which reports <c>null</c> for files that do not
    /// exist yet — the editor must be able to author a brand-new map file, not
    /// only overwrite an existing one.
    /// </summary>
    public static class MapFileService
    {
        private const string MapDir = "map";
        private const string JsonExtension = ".json";

        /// <summary>
        /// IDs of every loaded mod that can be written to (folder-backed).
        /// Zip mods are skipped silently: they appear in the game but the editor
        /// cannot save into them.
        /// </summary>
        public static List<string> ListWritableMods()
        {
            var result = new List<string>();
            var manager = ModManager.Instance;
            if (manager?.Mods == null)
                return result;

            foreach (var pair in manager.Mods)
            {
                if (pair.Value?.FileProvider is FolderModFileProvider)
                    result.Add(pair.Key);
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        /// <summary>
        /// Mod-relative paths of all <c>.json</c> files under the mod's
        /// <c>map/</c> directory, including nested subdirectories. Returns an
        /// empty list for unknown mods.
        /// </summary>
        public static List<string> ListMapFiles(string modId)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(modId))
                return result;

            CollectJsonFiles(modId, MapDir, result);
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        /// <summary>Reads a map file through the mod provider; <c>null</c> when missing.</summary>
        public static string ReadFile(string modId, string relativePath)
        {
            return ModManager.Instance.ReadFileText(modId, relativePath);
        }

        /// <summary>
        /// Writes a map file into a folder-backed mod, creating the
        /// <c>map/</c> directory and any missing parents. Returns <c>false</c>
        /// for zip mods, unknown mods and empty paths.
        /// </summary>
        public static bool WriteFile(string modId, string relativePath, string text)
        {
            if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(relativePath))
                return false;

            var mod = ModManager.Instance.GetMod(modId);
            if (mod?.FileProvider is not FolderModFileProvider provider)
                return false;

            var root = provider.RootPath;
            if (string.IsNullOrEmpty(root))
                return false;

            var absolute = Path.GetFullPath(Path.Combine(root, relativePath.Replace('\\', '/')));
            var directory = Path.GetDirectoryName(absolute);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(absolute, text);
            return true;
        }

        /// <summary>
        /// Parses the standard <c>{"Items": [...]}</c> envelope into zones.
        /// Throws <see cref="JsonException"/> on malformed JSON so the editor
        /// can show the error instead of silently opening an empty map.
        /// </summary>
        public static List<MapZone> ParseZones(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<MapZone>();

            var data = JsonSerializer.Deserialize<FileData<MapZone>>(json, JsonOptionsProvider.ReadOptions);
            return data?.Items ?? new List<MapZone>();
        }

        /// <summary>
        /// Serialises zones into the standard envelope with indentation and
        /// without null fields, so hand-editing the result stays pleasant and
        /// optional properties such as <c>label_pos</c> only appear when used.
        /// </summary>
        public static string BuildJson(IEnumerable<MapZone> zones)
        {
            var data = new FileData<MapZone> { Items = zones?.ToList() ?? new List<MapZone>() };
            return JsonSerializer.Serialize(data, JsonOptionsProvider.WriteIndentedIgnoreNullOptions);
        }

        private static void CollectJsonFiles(string modId, string directory, List<string> result)
        {
            foreach (var entry in ModManager.Instance.ListFiles(modId, directory))
            {
                if (entry.EndsWith("/"))
                {
                    CollectJsonFiles(modId, entry.TrimEnd('/'), result);
                    continue;
                }

                if (entry.EndsWith(JsonExtension, StringComparison.OrdinalIgnoreCase))
                    result.Add(entry.Replace('\\', '/'));
            }
        }
    }
}
