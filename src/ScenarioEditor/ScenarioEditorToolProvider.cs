using Cthangover.Core.UI.Tool;
using Godot;

using Cthangover.Mods.Interface.Tool;
namespace Cthangover.Core.UI.Tool.ScenarioEditor
{
    /// <summary>Registers the scenario editor as a tool window. Creates <see cref="ScenarioEditorWindow"/> instances.</summary>
    public class ScenarioEditorToolProvider : IToolProvider
    {
        /// <summary>Unique tool identifier.</summary>
        public string Id => "scenario_editor";
        /// <summary>Translation key for the tool's display name.</summary>
        public string LocaleKey => "tools/scenario_editor/title";
        /// <summary>Creates the editor window instance.</summary>
        public Window CreateWindow() => new ScenarioEditorWindow();
        /// <summary>Adds the scenario editor's launcher button to the main menu tool block.</summary>
        public void AddMenuButton(Container container) => ToolMenuButton.Attach(container, this);
    }
}
