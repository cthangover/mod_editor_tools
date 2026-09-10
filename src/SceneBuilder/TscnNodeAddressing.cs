using System.Collections.Generic;

namespace Cthangover.Core.UI.Tool.SceneBuilder
{
    /// <summary>
    /// Single source of truth for mapping a <c>[node]</c> section's attributes to
    /// the address path used across the scene builder tool (inspector selection,
    /// tscn text surgery, file property snapshots). Both <see cref="TscnTextSurgery"/>
    /// and <see cref="TscnFileProps"/> must resolve sections identically, so the
    /// rule lives here and nowhere else: no parent attribute = scene root (""),
    /// parent="." = direct child of root, otherwise parent path + "/" + name.
    /// </summary>
    internal static class TscnNodeAddressing
    {
        /// <summary>Computes the node address from section attributes; null when attributes lack a usable name.</summary>
        public static string ComputePath(Dictionary<string, string> attrs)
        {
            if (attrs == null || !attrs.TryGetValue("name", out var name) || string.IsNullOrEmpty(name))
                return null;
            if (!attrs.TryGetValue("parent", out var parent))
                return "";
            return parent == "." ? name : parent + "/" + name;
        }
    }
}
