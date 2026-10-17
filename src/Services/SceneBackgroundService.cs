using System;
using System.Collections.Generic;
using Cthangover.Core.Mods.Resolvers;

namespace Cthangover.Tools.Services
{
    /// <summary>
    /// Finds which background an editor should show for a piece of content,
    /// so the author does not have to guess it from a list of hundreds:
    /// <list type="bullet">
    /// <item>a scene's background is the first <c>defaultBackground</c> of its
    /// <c>scenes/*.json</c> definition;</item>
    /// <item>an interactive belongs to the scene of the scenario that adds it
    /// (<c>interactive_add &lt;id&gt;</c>).</item>
    /// </list>
    /// Both are best-effort hints: <c>null</c> means "no idea", and the author
    /// can always pick another background by hand.
    /// </summary>
    public static class SceneBackgroundService
    {
        private const string InteractiveAddCommand = "interactive_add";

        /// <summary>Default background ID of a logical scene, or <c>null</c>.</summary>
        public static string ForScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName) || sceneName == "-")
                return null;

            var defs = SceneDefinitions.Collect();
            if (defs == null)
                return null;

            foreach (var pair in defs)
            {
                if (!string.Equals(pair.Key, sceneName, StringComparison.OrdinalIgnoreCase) || pair.Value == null)
                    continue;

                // Several mods may define the scene; the last one wins at runtime.
                for (var i = pair.Value.Count - 1; i >= 0; i--)
                {
                    if (pair.Value[i]?.DefaultBackground is { Count: > 0 } list)
                        return list[0];
                }
            }

            return null;
        }

        /// <summary>
        /// Scans every scenario once and maps interactive IDs to the scene of
        /// the first scenario that adds them.
        /// </summary>
        public static Dictionary<string, string> MapInteractivesToScenes()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in ScenarioFileService.ScanScenarioFiles())
            {
                foreach (var group in mod.Groups)
                {
                    foreach (var file in group.Files)
                    {
                        var text = ScenarioFileService.ReadAllText(file.ModId, file.RelativePath);
                        if (string.IsNullOrEmpty(text) || text.IndexOf(InteractiveAddCommand, StringComparison.Ordinal) < 0)
                            continue;

                        var scene = ScenarioFileService.ParseMetadata(text).Scene;
                        foreach (var raw in text.Split('\n'))
                        {
                            var tokens = ScenarioSyntaxService.Tokenize(raw.Trim());
                            if (tokens.Count >= 2 && tokens[0] == InteractiveAddCommand && !result.ContainsKey(tokens[1]))
                                result[tokens[1]] = scene;
                        }
                    }
                }
            }

            return result;
        }
    }
}
