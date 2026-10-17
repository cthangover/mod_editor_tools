using System.Linq;
using Cthangover.Tools.Services;
using Cthangover.Core.UI.Theming;
using Cthangover.Mods.Interface.Tool;
using Godot;

namespace Cthangover.Core.UI.Tool.ScenarioEditor
{
    /// <summary>Read-only metadata panel for the scenario in the active text tab: scene, priority, condition, switch targets, locale keys and quest references.</summary>
    public partial class ScenarioInfoPanel : VBoxContainer
    {
        private Label _infoScene;
        private Label _infoPriority;
        private Label _infoCondition;
        private Label _infoSwitchTargets;
        private Label _infoLocaleKeys;
        private Label _infoQuestRefs;

        /// <summary>Binds the rows once the panel enters the tree (see <see cref="Bind"/>).</summary>
        public override void _Ready()
        {
            Bind();
        }

        /// <summary>
        /// Looks the rows up lazily: the tscn builder runs the constructor before
        /// the child nodes exist, so lookups there would fail.
        /// </summary>
        private void Bind()
        {
            if (_infoScene != null)
                return;

            SetHint("HintScene", "tools/scenario_editor/info_scene");
            SetHint("HintPriority", "tools/scenario_editor/info_priority");
            SetHint("HintCondition", "tools/scenario_editor/info_condition");
            SetHint("HintSwitchTargets", "tools/scenario_editor/info_switch_targets");
            SetHint("HintLocaleKeys", "tools/scenario_editor/info_locale_keys");
            SetHint("HintQuestRefs", "tools/scenario_editor/info_quest_refs");

            _infoScene = Value("InfoScene");
            _infoPriority = Value("InfoPriority");
            _infoCondition = Value("InfoCondition");
            _infoSwitchTargets = Value("InfoSwitchTargets");
            _infoLocaleKeys = Value("InfoLocaleKeys");
            _infoQuestRefs = Value("InfoQuestRefs");
        }

        private void SetHint(string node, string key)
        {
            var label = GetNode<Label>("Scroll/VBox/" + node);
            label.Text = TranslationServer.Translate(key).ToString().ToUpperInvariant();
            label.AddThemeColorOverride("font_color", UiTokens.Muted);
            label.AddThemeFontSizeOverride("font_size", UiTokens.FontSmall);
        }

        private Label Value(string node)
        {
            var label = GetNode<Label>("Scroll/VBox/" + node);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            label.CustomMinimumSize = new Vector2(120, 0);
            label.AddThemeFontOverride("font", ToolWindow.GetMonospaceFont());
            label.AddThemeFontSizeOverride("font_size", UiTokens.FontBodySmall);
            return label;
        }

        /// <summary>Parses the given scenario text and refreshes every metadata row.</summary>
        public void Update(string text)
        {
            Bind();
            if (string.IsNullOrEmpty(text))
            {
                Clear();
                return;
            }

            var metadata = ScenarioFileService.ParseMetadata(text);
            var refs = ScenarioFileService.ExtractReferences(text);

            _infoScene.Text = metadata.Scene;
            _infoPriority.Text = metadata.Priority;
            _infoCondition.Text = metadata.Condition;
            _infoSwitchTargets.Text = refs.SwitchTargets.Count > 0 ? string.Join(", ", refs.SwitchTargets.Distinct()) : "-";
            _infoLocaleKeys.Text = refs.LocaleKeys.Count > 0 ? string.Join(", ", refs.LocaleKeys.Distinct()) : "-";
            _infoQuestRefs.Text = refs.QuestRefs.Count > 0 ? string.Join(", ", refs.QuestRefs.Distinct()) : "-";
        }

        /// <summary>Resets every metadata row to the "-" placeholder.</summary>
        public void Clear()
        {
            Bind();
            _infoScene.Text = "-";
            _infoPriority.Text = "-";
            _infoCondition.Text = "-";
            _infoSwitchTargets.Text = "-";
            _infoLocaleKeys.Text = "-";
            _infoQuestRefs.Text = "-";
        }
    }
}
