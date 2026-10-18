using System;
using System.Linq;
using Cthangover.Core.Scenes;
using Cthangover.Core.UI.Tool.ScenarioEditor;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Tools.Common;
using Cthangover.Tools.Hub;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Tools.Tests.Windows
{
    /// <summary>
    /// Play-test round trip of the scenario editor opened from the
    /// <see cref="NovelEditorHub"/>: plays this suite's own scenario (so the
    /// editor's save-before-play rewrites a test file, not game content), lets
    /// the play end the way a finished dialog ends it
    /// (<see cref="SceneManager.IsTestPlayActive"/> drops), then plays again and
    /// presses Stop. Both times the game must come back to the hub with the
    /// editor visible — not to the engine's main menu. Advanced one step per
    /// driver tick; <see cref="Tick"/> returns true when done.
    /// </summary>
    public class ScenarioPlayReturnCheck
    {
        private const string SuiteModId = "editor_tools__windows";
        private const string ScenarioPath = "scenarios/playtest_return.scenario";
        private const int ShellSettleTicks = 3;

        private readonly Action<bool, string> _check;
        private ScenarioEditorWindow _window;
        private int _step;
        private int _settle;

        /// <summary>Creates the check; <paramref name="check"/> records one assertion.</summary>
        public ScenarioPlayReturnCheck(Action<bool, string> check)
        {
            _check = check;
        }

        /// <summary>Advances the round trip by one step; true once finished.</summary>
        public bool Tick(SceneTree tree)
        {
            switch (_step)
            {
                case 0:
                    _window = (ScenarioEditorWindow)ToolWindow.ShowWindow(new ScenarioEditorWindow());
                    var tabs = _window.FindChildren("*", "", true, false).OfType<ScenarioEditTabs>().FirstOrDefault();
                    tabs?.OpenFile(ScenarioFileService.MakeKey(SuiteModId, ScenarioPath));
                    _check(tabs?.HasActiveFile == true, "scenario_editor: test scenario opened");
                    break;

                case 1:
                case 4:
                    PressButton(_window, "tools/scenario_editor/play");
                    _settle = ShellSettleTicks;
                    break;

                case 2:
                    if (!InGame(tree))
                        return false;
                    SceneManager.IsTestPlayActive = false;
                    break;

                case 5:
                    if (!InGame(tree))
                        return false;
                    PressButton(tree.Root, "tools/scenario_editor/stop");
                    break;

                case 3:
                case 6:
                    if (tree.CurrentScene is not NovelEditorHub || !_window.Visible)
                        return false;
                    _check(true, _step == 3
                        ? "scenario_editor: the scenario's end returns to the editor hub"
                        : "scenario_editor: Stop returns to the editor hub");
                    if (_step == 6)
                    {
                        _window.QueueFree();
                        return true;
                    }
                    break;
            }

            _step++;
            return false;
        }

        /// <summary>The play-test left the hub for the game shell and the shell settled.</summary>
        private bool InGame(SceneTree tree)
        {
            if (tree.CurrentScene is NovelEditorHub || GodotSceneService.IsTransitioning || --_settle > 0)
                return false;
            _check(!_window.Visible, "scenario_editor: play hides the editor and enters the game");
            return true;
        }

        private static void PressButton(Node root, string textKey)
        {
            var text = ToolUi.Tr(textKey);
            var button = root.FindChildren("*", nameof(Button), true, false).OfType<Button>().FirstOrDefault(b => b.Text == text);
            button?.EmitSignal(BaseButton.SignalName.Pressed);
        }
    }
}
