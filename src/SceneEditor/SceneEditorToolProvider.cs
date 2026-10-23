using Cthangover.Core.UI.Tool;
using Cthangover.Mods.Interface.Tool;
using Godot;

namespace Cthangover.Tools.SceneEditor
{
    /// <summary>Registers the scene editor as a tool window (main menu tools, in-game toolbox).</summary>
    public class SceneEditorToolProvider : IToolProvider
    {
        /// <summary>Unique tool identifier.</summary>
        public string Id => "scene_editor";
        /// <summary>Translation key for the tool's display name.</summary>
        public string LocaleKey => "tools/scene_editor/title";
        /// <summary>Creates the editor window instance.</summary>
        public Window CreateWindow() => new SceneEditorWindow();
        /// <summary>Adds the launcher button to the main menu tool block.</summary>
        public void AddMenuButton(Container container) => ToolMenuButton.Attach(container, this);
    }
}
