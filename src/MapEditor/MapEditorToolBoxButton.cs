using Cthangover.Core.UI.Tool;

namespace Cthangover.Tools.MapEditor
{
    /// <summary>
    /// Toolbox button that opens the map editor from the in-game toolbar.
    /// Matches the tool id registered by <see cref="MapEditorToolProvider"/>;
    /// always visible because map authoring is part of the normal dev workflow.
    /// </summary>
    public class MapEditorToolBoxButton : IToolBoxButton
    {
        /// <summary>Matches <see cref="MapEditorToolProvider.Id"/>.</summary>
        public string ToolId => "map_editor";

        /// <summary>No dedicated icon — the button uses its text label.</summary>
        public string IconPath => "";

        /// <summary>Translation key for the button label.</summary>
        public string LocaleKey => "tools/map_editor/title";

        /// <summary>Always visible in the toolbox.</summary>
        public bool IsVisible() => true;
    }
}
