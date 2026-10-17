using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Cthangover.Core.Mods;
using Cthangover.Mods.Map;
using Cthangover.Core.Utils;

namespace Cthangover.Tools.Services
{
    /// <summary>
    /// (De)serialisation of world map zone files (<c>mods/&lt;mod&gt;/map/*.json</c>)
    /// for the map editor. File access itself goes through
    /// <see cref="ModFileService"/>.
    /// </summary>
    public static class MapFileService
    {
        /// <summary>Folder of map zone files inside a mod.</summary>
        public const string MapDir = "map";

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
    }
}
