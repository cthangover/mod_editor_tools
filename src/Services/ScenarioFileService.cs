using System;
using Cthangover.Mods.Dialog.Scenarios;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cthangover.Core.Scenarios;
using Cthangover.Core.Factories.Impls;
using Cthangover.Core.Utils;
using Cthangover.Core.Mods;

namespace Cthangover.Tools.Services
{
    /// <summary>Parsed metadata from a scenario file header (before the <c>---</c> separator).</summary>
    public sealed class ScenarioMetadata
    {
        /// <summary>Target scene name for the scenario.</summary>
        public string Scene { get; set; } = "-";
        /// <summary>Priority level of the scenario.</summary>
        public string Priority { get; set; } = "-";
        /// <summary>Condition string for the scenario to activate.</summary>
        public string Condition { get; set; } = "-";
    }

    /// <summary>Collected references extracted from scenario command lines for the info panel.</summary>
    public sealed class ScenarioReferences
    {
        /// <summary>Scene switch targets referenced in commands.</summary>
        public List<string> SwitchTargets { get; set; } = new();
        /// <summary>Localisation keys found in named parameters.</summary>
        public List<string> LocaleKeys { get; set; } = new();
        /// <summary>Quest IDs referenced in named <c>quest_id</c> parameters.</summary>
        public List<string> QuestRefs { get; set; } = new();
        /// <summary>Background IDs referenced in positional parameters of background-related commands.</summary>
        public List<string> BackgroundRefs { get; set; } = new();
    }

    /// <summary>
    /// Describes a single scenario file found during scanning. The file is
    /// addressed by its owning mod and mod-relative path, so both folder
    /// mods and zip mods are covered; <see cref="Key"/> is the stable
    /// identity used by the editor UI.
    /// </summary>
    public sealed class ScenarioFileInfo
    {
        /// <summary>Identifier of the mod that provides the file.</summary>
        public string ModId { get; set; }
        /// <summary>Mod-relative path, e.g. <c>scenarios/start.scenario</c>.</summary>
        public string RelativePath { get; set; }
        /// <summary>Filename without extension.</summary>
        public string Name { get; set; }
        /// <summary>Directory path relative to the mod's <c>scenarios/</c> folder.</summary>
        public string RelativeDir { get; set; }
        /// <summary>Stable UI identity: <c>modId:relativePath</c>.</summary>
        public string Key => ScenarioFileService.MakeKey(ModId, RelativePath);
    }

    /// <summary>A group of scenario files within the same subdirectory under <c>scenarios/</c>.</summary>
    public sealed class ScenarioGroup
    {
        /// <summary>Relative directory path (empty for root-level files).</summary>
        public string DirectoryPath { get; set; }
        /// <summary>Files in this group, sorted by name.</summary>
        public List<ScenarioFileInfo> Files { get; set; } = new();
    }

    /// <summary>Represents all scenario files for one mod, grouped by subdirectory.</summary>
    public sealed class ModScenarioTree
    {
        /// <summary>The mod's identifier.</summary>
        public string ModId { get; set; }
        /// <summary>Groups of scenario files within this mod.</summary>
        public List<ScenarioGroup> Groups { get; set; } = new();
    }

    /// <summary>
    /// Static service for discovering, reading, writing, and analysing scenario files.
    /// Scans every loaded mod's <c>scenarios/</c> folder through the mod file
    /// provider abstraction (<see cref="IModFileProvider"/>), so folder and zip
    /// mods are discovered identically. Writing is only possible for folder mods:
    /// zip mods are read-only, and <see cref="WriteAllText"/> reports that instead
    /// of touching the filesystem. Used by <see cref="ScenarioEditorWindow"/> for
    /// the file tree and info panel.
    /// </summary>
    public static class ScenarioFileService
    {
        private const string ScenariosDir = "scenarios";
        private const string ScenarioExtension = ".scenario";
        private const string KeySeparator = ":";

        /// <summary>
        /// Recursively scans every loaded mod's <c>scenarios/</c> folder for
        /// <c>.scenario</c> files via <see cref="IModFileProvider"/>. Groups
        /// results by mod ID and subdirectory. Returns an empty list when no
        /// mods are loaded or none provide scenarios.
        /// </summary>
        public static List<ModScenarioTree> ScanScenarioFiles()
        {
            var result = new List<ModScenarioTree>();
            var manager = ModManager.Instance;

            foreach (var mod in manager.Mods.Values.OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase))
            {
                var files = new List<ScenarioFileInfo>();
                CollectScenarioFiles(manager, mod.Id, ScenariosDir, files);
                if (files.Count == 0)
                    continue;

                var groups = files
                    .GroupBy(f => f.RelativeDir)
                    .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new ScenarioGroup
                    {
                        DirectoryPath = g.Key,
                        Files = g.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList()
                    })
                    .ToList();

                result.Add(new ModScenarioTree { ModId = mod.Id, Groups = groups });
            }

            return result;
        }

        private static void CollectScenarioFiles(ModManager manager, string modId, string directory, List<ScenarioFileInfo> result)
        {
            foreach (var entry in manager.ListFiles(modId, directory))
            {
                if (entry.EndsWith("/"))
                {
                    CollectScenarioFiles(manager, modId, entry.TrimEnd('/'), result);
                    continue;
                }

                if (!entry.EndsWith(ScenarioExtension, StringComparison.OrdinalIgnoreCase))
                    continue;

                var path = entry.Replace('\\', '/');
                var lastSlash = path.LastIndexOf('/');
                var fileName = lastSlash >= 0 ? path.Substring(lastSlash + 1) : path;
                var dirPath = lastSlash >= 0 ? path.Substring(0, lastSlash) : "";

                var relativeDir = "";
                if (dirPath.StartsWith(ScenariosDir + "/", StringComparison.OrdinalIgnoreCase))
                    relativeDir = dirPath.Substring(ScenariosDir.Length + 1);
                else if (!dirPath.Equals(ScenariosDir, StringComparison.OrdinalIgnoreCase))
                    relativeDir = dirPath;

                result.Add(new ScenarioFileInfo
                {
                    ModId = modId,
                    RelativePath = path,
                    Name = Path.GetFileNameWithoutExtension(fileName),
                    RelativeDir = relativeDir
                });
            }
        }

        /// <summary>Builds the stable UI identity for a scenario file.</summary>
        public static string MakeKey(string modId, string relativePath) => modId + KeySeparator + relativePath;

        /// <summary>Splits a key produced by <see cref="MakeKey"/> back into mod ID and mod-relative path.</summary>
        public static bool TryParseKey(string key, out string modId, out string relativePath)
        {
            modId = null;
            relativePath = null;
            if (string.IsNullOrEmpty(key))
                return false;

            var separator = key.IndexOf(KeySeparator, StringComparison.Ordinal);
            if (separator <= 0 || separator >= key.Length - 1)
                return false;

            modId = key.Substring(0, separator);
            relativePath = key.Substring(separator + 1);
            return true;
        }

        /// <summary>
        /// Parses the header block (before first <c>---</c> line) in YAML-like
        /// <c>key: value</c> format to extract scene, priority, and condition.
        /// </summary>
        public static ScenarioMetadata ParseMetadata(string text)
        {
            var meta = new ScenarioMetadata();
            var metaEnd = text.IndexOf("\n---");
            var metaText = metaEnd >= 0 ? text.Substring(0, metaEnd) : "";

            foreach (var line in metaText.Split('\n'))
            {
                var t = line.Trim();
                if (t.Length == 0 || t.StartsWith("#"))
                    continue;

                var ci = t.IndexOf(':');
                if (ci <= 0)
                    continue;

                var key = t.Substring(0, ci).Trim().ToLowerInvariant();
                var val = t.Substring(ci + 1).Trim();

                switch (key)
                {
                    case "scene": meta.Scene = val; break;
                    case "priority": meta.Priority = val; break;
                    case "condition": meta.Condition = val; break;
                }
            }

            return meta;
        }

        /// <summary>
        /// Walks the body of the scenario (after <c>---</c>) and extracts references:
        /// background IDs, scene switch targets, locale keys, and quest IDs.
        /// Uses <see cref="ScenarioSyntaxService.ExtractCommandParams"/> to tokenise
        /// each line and <see cref="ScenarioCommandStrategyFactory"/> metadata to
        /// classify positional parameters.
        /// </summary>
        /// <param name="text">Full scenario file text including header.</param>
        public static ScenarioReferences ExtractReferences(string text)
        {
            var refs = new ScenarioReferences();
            var metaEnd = text.IndexOf("\n---");
            var content = metaEnd >= 0 ? text.Substring(metaEnd + 4) : text;

            foreach (var rawLine in content.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(":"))
                    continue;

                ScenarioSyntaxService.ExtractCommandParams(line, out var cmd, out var positional, out var named);

                if (string.IsNullOrEmpty(cmd))
                    continue;

                var meta = ScenarioCommandStrategyFactory.GetReferenceMetadata(cmd);
                if (meta != null && positional.Count > 0)
                {
                    switch (meta.Positional0Kind)
                    {
                        case PositionalReferenceKind.Background:
                            refs.BackgroundRefs.Add(positional[0]);
                            break;
                        case PositionalReferenceKind.Scene:
                            refs.SwitchTargets.Add(positional[0]);
                            break;
                    }
                }

                if (named.TryGetValue("key", out var lk))
                    refs.LocaleKeys.Add(lk);
                if (named.TryGetValue("quest_id", out var qid))
                    refs.QuestRefs.Add(qid);
            }

            return refs;
        }

        /// <summary>Reads a scenario file through its mod provider. Returns <c>null</c> when not found.</summary>
        public static string ReadAllText(string modId, string relativePath)
        {
            return ModManager.Instance.ReadFileText(modId, relativePath);
        }

        /// <summary>
        /// Writes a scenario file back to disk. Returns <c>false</c> when the
        /// owning mod is not filesystem-backed (zip mods are read-only).
        /// </summary>
        public static bool WriteAllText(string modId, string relativePath, string text)
        {
            var absolutePath = ModManager.Instance.GetFileSystemPath(modId, relativePath);
            if (string.IsNullOrEmpty(absolutePath))
                return false;

            File.WriteAllText(absolutePath, text);
            return true;
        }

        /// <summary>Absolute filesystem path of a scenario file, or <c>null</c> for zip-backed mods.</summary>
        public static string GetAbsolutePath(string modId, string relativePath)
        {
            return ModManager.Instance.GetFileSystemPath(modId, relativePath);
        }

        /// <summary>Project-style display path (<c>mods/&lt;modId&gt;/&lt;relativePath&gt;</c>).</summary>
        public static string GetDisplayPath(string modId, string relativePath)
        {
            return "mods/" + modId + "/" + relativePath;
        }

        /// <summary>Scans every mod's scenarios and builds the cross-scene graph (nodes per scene, edges per switch target).</summary>
        public static SceneGraphData BuildSceneGraph()
        {
            var data = new SceneGraphData();
            var sceneNodes = new Dictionary<string, SceneGraphNode>(StringComparer.OrdinalIgnoreCase);
            var edgeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var mod in ScanScenarioFiles())
            {
                foreach (var group in mod.Groups)
                {
                    foreach (var file in group.Files)
                    {
                        var text = ReadAllText(file.ModId, file.RelativePath);
                        if (text == null)
                            continue;

                        var metadata = ParseMetadata(text);
                        if (string.IsNullOrEmpty(metadata.Scene) || metadata.Scene == "-")
                            continue;

                        var sceneKey = metadata.Scene.ToLowerInvariant();

                        if (!sceneNodes.TryGetValue(sceneKey, out var node))
                        {
                            node = new SceneGraphNode
                            {
                                SceneName = metadata.Scene,
                                ModId = file.ModId,
                                IsStartScene = string.Equals(metadata.Scene, "start_scene", StringComparison.OrdinalIgnoreCase)
                            };
                            sceneNodes[sceneKey] = node;
                        }

                        node.ScenarioCount++;
                        node.Scenarios.Add(new ScenarioGraphLink
                        {
                            Name = file.Name,
                            Priority = int.TryParse(metadata.Priority, out var p) ? p : 0,
                            Key = file.Key
                        });

                        var refs = ExtractReferences(text);
                        foreach (var target in refs.SwitchTargets)
                        {
                            var srcKey = metadata.Scene.ToLowerInvariant();
                            var dstKey = target.ToLowerInvariant();
                            var edgeKey = srcKey + "|" + dstKey;
                            if (!edgeSet.Contains(edgeKey))
                            {
                                edgeSet.Add(edgeKey);
                                data.Edges.Add(new SceneGraphEdge
                                {
                                    FromScene = metadata.Scene,
                                    ToScene = target
                                });
                            }
                        }
                    }
                }
            }

            foreach (var node in sceneNodes.Values)
                data.Nodes.Add(node);

            return data;
        }
    }
}
