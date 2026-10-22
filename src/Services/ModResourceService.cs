using System;
using System.Collections.Generic;
using System.IO;
using Cthangover.Core.Factories.Impls;
using Cthangover.Core.Mods;
using Cthangover.Core.Mods.Resolvers;
using Godot;

namespace Cthangover.Tools.Services
{
    /// <summary>
    /// Static service for discovering and loading mod background resources.
    /// Queries <see cref="Files.Collect"/> for files under
    /// <c>"backgrounds"</c>, strips extensions to produce IDs, and loads textures
    /// via <see cref="BackgroundFactory"/>. Used by the light editor, interactive
    /// editor, and scenario editor to populate background selection lists and
    /// render previews.
    /// </summary>
    public static class ModResourceService
    {
        /// <summary>
        /// Returns all unique background IDs (filenames without extension) from
        /// the <c>"backgrounds"</c> file collection across all installed mods,
        /// sorted, without the <c>_albedo</c>/<c>_depth</c> lighting masks.
        /// </summary>
        public static List<string> GetBackgroundIds()
        {
            var ids = new List<string>();
            var files = Files.Collect("backgrounds");
            var exts = GetExt();
            
            foreach (var kvp in files)
            {
                var id = kvp.Key;
                var ext = Path.GetExtension(id);
                if (!string.IsNullOrWhiteSpace(id) && exts.Contains(ext.ToLower()))
                {
                    var cleanId = id.Substring(0, id.LastIndexOf('.'));
                    var bare = cleanId.TrimEnd('_');
                    if (!bare.EndsWith("_albedo") && !bare.EndsWith("_depth"))
                        ids.Add(cleanId);
                }
            }

            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids;
        }

        /// <summary>Ids of every sound (group <c>sounds</c>, path without extension): the choices for a scene's ambient.</summary>
        public static List<string> GetSoundIds()
        {
            var ids = new List<string>();
            var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ext in ModConfig.Instance.AudioExtensions)
                exts.Add(ext);

            foreach (var id in Files.Collect("sounds").Keys)
            {
                var ext = Path.GetExtension(id);
                if (exts.Contains(ext))
                    ids.Add(id.Substring(0, id.Length - ext.Length));
            }

            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids;
        }

        private static HashSet<string> GetExt()
        {
            var set = new HashSet<string>();
            foreach (var ext in ModConfig.Instance.TextureExtensions)
            {
                if(string.IsNullOrWhiteSpace(ext))
                    continue;
                set.Add(ext.ToLower());
            }
            return set;
        }

        /// <summary>
        /// Returns every texture logical name (filename without extension) from
        /// all configured texture groups, sorted for stable UI lists. Used by
        /// the map editor's marker icon picker: these are exactly the IDs that
        /// <c>MapZone.Image</c> accepts, because resolution also keys textures
        /// by basename.
        /// </summary>
        public static List<string> GetTextureIds()
        {
            var ids = new List<string>();
            var textures = Textures.Collect();
            if (textures == null)
                return ids;

            foreach (var kvp in textures)
                ids.Add(kvp.Key);

            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids;
        }
        
        /// <summary>
        /// Texture IDs that make sense as small icons (map markers): every
        /// texture except backgrounds and their <c>_depth</c>/<c>_albedo</c>
        /// masks. Textures are keyed by file name, so backgrounds are excluded
        /// by the file-name part of their IDs.
        /// </summary>
        public static List<string> GetIconIds()
        {
            var backgrounds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in GetBackgroundIds())
                backgrounds.Add(id.Substring(id.LastIndexOf('/') + 1));

            var result = new List<string>();
            foreach (var id in GetTextureIds())
            {
                if (backgrounds.Contains(id) || id.EndsWith("_depth", StringComparison.OrdinalIgnoreCase)
                    || id.EndsWith("_albedo", StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(id);
            }
            return result;
        }

        /// <summary>
        /// Loads a background texture by ID via <see cref="BackgroundFactory.Instance.Get"/>.
        /// Returns <c>null</c> on any error (missing resource, load failure).
        /// </summary>
        public static Texture2D LoadBackgroundTexture(string id)
        {
            try
            {
                return BackgroundFactory.Instance.Get(id);
            }
            catch
            {
                return null;
            }
        }
    }
}
