using Cthangover.Core.UI.Tool;
using Godot;

using Cthangover.Mods.Interface.Tool;
namespace Cthangover.Tools.MapEditor
{
    /// <summary>
    /// Registers the map editor as a discoverable tool window. Discovery is
    /// automatic: <c>ToolFactory</c> scans mod assemblies for
    /// <see cref="IToolProvider"/> implementations, so no manifest entry is
    /// needed. The provider only knows how to build the window; all editing
    /// logic lives in <see cref="MapEditorWindow"/>.
    /// </summary>
    public class MapEditorToolProvider : IToolProvider
    {
        /// <summary>Unique tool identifier used by the toolbox button.</summary>
        public string Id => "map_editor";

        /// <summary>Translation key for the tool's display name.</summary>
        public string LocaleKey => "tools/map_editor/title";

        /// <summary>Creates the editor window instance.</summary>
        public Window CreateWindow() => new MapEditorWindow();

        /// <summary>Adds the map editor's launcher button to the main menu tool block.</summary>
        public void AddMenuButton(Container container) => ToolMenuButton.Attach(container, this);
    }
}
