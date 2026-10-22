using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Cthangover.Core.Bootstrap;
using Cthangover.Core.Mods;
using Cthangover.Core.Mods.Resolvers;
using Cthangover.Core.Scenes;

namespace Cthangover.Tools.Services
{
    /// <summary>
    /// Scene files (<c>scenes/**.json</c>) of the loaded mods for the scene editor: reading
    /// them per mod in load order (the order the game merges them in), writing and deleting
    /// one. After a write the caller runs <see cref="SceneManager.ReloadContent"/>, so a new
    /// scene can be entered without a mod reload.
    /// </summary>
    public static class SceneFileService
    {
        private const string ScenesDir = "scenes";
        private static readonly Regex NamePattern = new("^[a-z][a-z0-9_]*$");

        /// <summary>Every scene file of the loaded mods and of the studio layer, in load order.</summary>
        public static List<SceneFileEntry> Scan()
        {
            var result = new List<SceneFileEntry>();
            var manager = ModManager.Instance;
            if (manager?.Mods == null)
                return result;

            foreach (var modId in ModRegistry.Instance.GetOrderedModIds())
            {
                if (manager.Mods.TryGetValue(modId, out var mod) && mod?.FileProvider != null)
                    Collect(mod.FileProvider, modId, ScenesDir, result);
            }
            foreach (var overlay in ModOverlays.All)
                Collect(overlay.Value, overlay.Key, ScenesDir, result);
            return result;
        }

        /// <summary>The scene name typed by the author, trimmed and lowercased; null when it is not an id (latin letters, digits, _).</summary>
        public static string ValidName(string name)
        {
            name = (name ?? string.Empty).Trim().ToLowerInvariant();
            return NamePattern.IsMatch(name) ? name : null;
        }

        /// <summary>Writes the entry to its mod; false when the mod is not a folder mod.</summary>
        public static bool Write(SceneFileEntry entry)
        {
            return ModFileService.WriteText(entry.ModId, entry.RelativePath, entry.ToJson());
        }

        /// <summary>Deletes the entry's file; false when the mod is not a folder mod or the file is gone.</summary>
        public static bool Delete(SceneFileEntry entry)
        {
            var path = ModFileService.GetWritablePath(entry.ModId, entry.RelativePath);
            if (path == null || !File.Exists(path))
                return false;
            File.Delete(path);
            return true;
        }

        private static void Collect(IModFileProvider provider, string modId, string directory, List<SceneFileEntry> result)
        {
            foreach (var entry in provider.ListFiles(directory))
            {
                if (entry.EndsWith("/"))
                {
                    Collect(provider, modId, entry.TrimEnd('/'), result);
                    continue;
                }
                if (!entry.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                var scene = SceneFileEntry.Parse(modId, entry, provider.ReadFileText(entry));
                if (scene != null)
                    result.Add(scene);
            }
        }
    }
}
