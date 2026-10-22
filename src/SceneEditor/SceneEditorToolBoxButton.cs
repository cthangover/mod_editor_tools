using Cthangover.Core.UI.Tool;

namespace Cthangover.Tools.SceneEditor
{
    /// <summary>Toolbox button that opens the scene editor. Always visible.</summary>
    public class SceneEditorToolBoxButton : IToolBoxButton
    {
        /// <summary>Matches <see cref="SceneEditorToolProvider.Id"/>.</summary>
        public string ToolId => "scene_editor";
        /// <summary>No custom icon — uses text label.</summary>
        public string IconPath => "";
        /// <summary>Translation key for the button label.</summary>
        public string LocaleKey => "tools/scene_editor/title";
        /// <summary>Always visible in the toolbox.</summary>
        public bool IsVisible() => true;
    }
}
