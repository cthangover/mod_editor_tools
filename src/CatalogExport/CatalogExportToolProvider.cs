using Cthangover.Core.UI.Tool;
using Godot;

namespace Cthangover.Core.UI.Tool.CatalogExport
{
    /// <summary>
    /// Registers the catalog export window as a developer tool. Moved here from
    /// the old mods menu when mod management UI was delegated to the
    /// <c>mod_manager</c> mod — exporting a catalog entry is an authoring task,
    /// not part of day-to-day mod management.
    /// </summary>
    public class CatalogExportToolProvider : IToolProvider
    {
        /// <summary>Unique tool identifier.</summary>
        public string Id => "catalog_export";

        /// <summary>Translation key for the tool's display name.</summary>
        public string LocaleKey => "mods/catalog_title";

        /// <summary>Creates the export window instance.</summary>
        public Window CreateWindow() => new CatalogExportWindow();
    }
}
