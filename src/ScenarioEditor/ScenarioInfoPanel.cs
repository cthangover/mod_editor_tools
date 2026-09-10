using System.Linq;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Core.UI.Tool.ScenarioEditor
{
    public partial class ScenarioInfoPanel : VBoxContainer
    {
        private Label _infoScene;
        private Label _infoPriority;
        private Label _infoCondition;
        private Label _infoSwitchTargets;
        private Label _infoLocaleKeys;
        private Label _infoQuestRefs;

        public ScenarioInfoPanel()
        {
            GetNode<Label>("Scroll/VBox/HintScene").Text = TranslationServer.Translate("tools/scenario_editor/info_scene") + ":";
            GetNode<Label>("Scroll/VBox/HintPriority").Text = TranslationServer.Translate("tools/scenario_editor/info_priority") + ":";
            GetNode<Label>("Scroll/VBox/HintCondition").Text = TranslationServer.Translate("tools/scenario_editor/info_condition") + ":";
            GetNode<Label>("Scroll/VBox/HintSwitchTargets").Text = TranslationServer.Translate("tools/scenario_editor/info_switch_targets") + ":";
            GetNode<Label>("Scroll/VBox/HintLocaleKeys").Text = TranslationServer.Translate("tools/scenario_editor/info_locale_keys") + ":";
            GetNode<Label>("Scroll/VBox/HintQuestRefs").Text = TranslationServer.Translate("tools/scenario_editor/info_quest_refs") + ":";

            _infoScene = GetNode<Label>("Scroll/VBox/InfoScene");
            _infoPriority = GetNode<Label>("Scroll/VBox/InfoPriority");
            _infoCondition = GetNode<Label>("Scroll/VBox/InfoCondition");
            _infoSwitchTargets = GetNode<Label>("Scroll/VBox/InfoSwitchTargets");
            _infoLocaleKeys = GetNode<Label>("Scroll/VBox/InfoLocaleKeys");
            _infoQuestRefs = GetNode<Label>("Scroll/VBox/InfoQuestRefs");
        }

        public void Update(string text)
        {
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

        public void Clear()
        {
            _infoScene.Text = "-";
            _infoPriority.Text = "-";
            _infoCondition.Text = "-";
            _infoSwitchTargets.Text = "-";
            _infoLocaleKeys.Text = "-";
            _infoQuestRefs.Text = "-";
        }
    }
}
