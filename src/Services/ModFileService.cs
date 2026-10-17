using System;
using System.Collections.Generic;
using System.IO;
using Cthangover.Core.Mods;
using Cthangover.Core.Mods.Providers;

namespace Cthangover.Tools.Services
{
    /// <summary>
    /// File access shared by the editors: listing a mod's files by folder and
    /// extension, reading them through the mod provider and writing them back.
    /// Reading works for every mod (folder, zip, test-suite subdir), writing only
    /// for folder-backed mods — the only ones with a real directory behind them.
    /// New files are created under the provider's <c>RootPath</c> because
    /// <c>GetFileSystemPath</c> reports <c>null</c> for files that do not exist
    /// yet, and an editor must be able to author a brand-new file.
    /// </summary>
    public static class ModFileService
    {
        /// <summary>IDs of every loaded folder-backed (writable) mod, sorted.</summary>
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

        /// <summary>True when the mod is folder-backed, i.e. the editors can save into it.</summary>
        public static bool IsWritable(string modId)
        {
            return !string.IsNullOrEmpty(modId)
                && ModManager.Instance.GetMod(modId)?.FileProvider is FolderModFileProvider;
        }

        /// <summary>
        /// Mod-relative paths of the files under <paramref name="directory"/>
        /// (recursively) ending with <paramref name="extension"/>, sorted.
        /// </summary>
        public static List<string> ListFiles(string modId, string directory, string extension)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(modId))
                return result;

            Collect(modId, directory, extension, result);
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        /// <summary>Reads a file through the mod provider; <c>null</c> when missing.</summary>
        public static string ReadText(string modId, string relativePath)
        {
            return ModManager.Instance.ReadFileText(modId, relativePath);
        }

        /// <summary>
        /// Writes a file into a folder-backed mod, creating missing directories.
        /// Returns <c>false</c> for zip/virtual mods, unknown mods and empty paths.
        /// </summary>
        public static bool WriteText(string modId, string relativePath, string text)
        {
            var absolute = GetWritablePath(modId, relativePath);
            if (absolute == null)
                return false;

            var directory = Path.GetDirectoryName(absolute);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(absolute, text);
            return true;
        }

        /// <summary>
        /// Absolute path a file of a folder-backed mod has (or would have once
        /// written); <c>null</c> for read-only mods and empty paths.
        /// </summary>
        public static string GetWritablePath(string modId, string relativePath)
        {
            if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(relativePath))
                return null;

            if (ModManager.Instance.GetMod(modId)?.FileProvider is not FolderModFileProvider provider
                || string.IsNullOrEmpty(provider.RootPath))
                return null;

            return Path.GetFullPath(Path.Combine(provider.RootPath, relativePath.Replace('\\', '/')));
        }

        private static void Collect(string modId, string directory, string extension, List<string> result)
        {
            foreach (var entry in ModManager.Instance.ListFiles(modId, directory))
            {
                if (entry.EndsWith("/"))
                {
                    Collect(modId, entry.TrimEnd('/'), extension, result);
                    continue;
                }

                if (entry.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    result.Add(entry.Replace('\\', '/'));
            }
        }
    }
}
