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
        /// the <c>"backgrounds"</c> file collection across all installed mods.
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
                    if (!cleanId.EndsWith("_albedo") && !cleanId.EndsWith("_depth"))
                        ids.Add(cleanId);
                }
            }
            
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
