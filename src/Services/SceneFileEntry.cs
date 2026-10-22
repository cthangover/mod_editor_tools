using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Cthangover.Core.Scenes;
using Cthangover.Core.Utils;

namespace Cthangover.Tools.Services
{
    /// <summary>
    /// One mod's file of a logical scene (<c>scenes/&lt;name&gt;.json</c>). The game merges
    /// the files of all mods into one <see cref="SceneDefinition"/> (backgrounds add up,
    /// a later non-empty ambient or default scenario wins); the scene editor works on
    /// the files themselves, because only one of them — the chosen mod's — is written.
    /// </summary>
    public sealed class SceneFileEntry
    {
        /// <summary>Mod that owns the file.</summary>
        public string ModId { get; set; }

        /// <summary>Mod-relative path of the file.</summary>
        public string RelativePath { get; set; }

        /// <summary>Scene name (the logical scene id).</summary>
        public string Name { get; set; }

        /// <summary>Backgrounds this file adds; the game shows one of the merged list at random.</summary>
        public List<string> Backgrounds { get; set; } = new();

        /// <summary>Ambient sound id (group <c>sounds</c>), empty for none.</summary>
        public string Ambient { get; set; } = string.Empty;

        /// <summary>Mod-relative path of the scenario played when nothing else starts, empty for none.</summary>
        public string DefaultScenario { get; set; } = string.Empty;

        /// <summary>Default path of a scene file: <c>scenes/&lt;name&gt;.json</c>.</summary>
        public static string PathFor(string name) => "scenes/" + name + ".json";

        /// <summary>Reads a scene file with the game's own JSON options; null when it is not a scene.</summary>
        public static SceneFileEntry Parse(string modId, string relativePath, string json)
        {
            SceneDefinition definition;
            try
            {
                definition = JsonSerializer.Deserialize<SceneDefinition>(json ?? string.Empty, JsonOptionsProvider.ReadOptions);
            }
            catch (JsonException)
            {
                return null;
            }

            if (string.IsNullOrEmpty(definition?.Name))
                return null;

            return new SceneFileEntry
            {
                ModId = modId,
                RelativePath = relativePath,
                Name = definition.Name,
                Backgrounds = definition.DefaultBackground ?? new List<string>(),
                Ambient = definition.DefaultAmbient ?? string.Empty,
                DefaultScenario = definition.DefaultScenario ?? string.Empty,
            };
        }

        /// <summary>
        /// The file text in the style of the hand-written scene files (two-space indent, LF,
        /// camelCase keys). Empty fields are left out, so a file of a mod that only adds a
        /// background does not reset another mod's ambient or default scenario.
        /// </summary>
        public string ToJson()
        {
            var json = new StringBuilder("{\n  \"name\": ").Append(Quote(Name));
            if (Backgrounds.Count > 0)
            {
                json.Append(",\n  \"defaultBackground\": [");
                for (var i = 0; i < Backgrounds.Count; i++)
                    json.Append(i == 0 ? string.Empty : ", ").Append(Quote(Backgrounds[i]));
                json.Append(']');
            }
            if (!string.IsNullOrEmpty(Ambient))
                json.Append(",\n  \"defaultAmbient\": ").Append(Quote(Ambient));
            if (!string.IsNullOrEmpty(DefaultScenario))
                json.Append(",\n  \"defaultScenario\": ").Append(Quote(DefaultScenario));
            return json.Append("\n}\n").ToString();
        }

        private static string Quote(string value) => JsonSerializer.Serialize(value ?? string.Empty);
    }
}
