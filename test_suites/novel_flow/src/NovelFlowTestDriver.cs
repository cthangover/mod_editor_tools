using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cthangover.Core.Bootstrap;
using Cthangover.Core.Scenes;
using Cthangover.Core.UI.Dialog;
using Cthangover.Core.UI.Tool.ScenarioEditor;
using Cthangover.Core.Utils;
using Cthangover.Mods.Dialog;
using Cthangover.Tools.Common;
using Cthangover.Tools.SceneEditor;
using Cthangover.Tools.Services;
using Cthangover.Mods.Interface.View;
using Godot;

namespace Cthangover.Tools.Tests.NovelFlow
{
    /// <summary>
    /// An author's session in the running novel template, through the editors' UI only:
    /// <list type="number">
    /// <item>the toolbar's tools menu opens the scene editor;</item>
    /// <item>New scene, a name, the game's mod, a background, Save;</item>
    /// <item>New scenario of that scene opens the scenario editor; the scenario (a way back to
    /// the town) is typed in and saved;</item>
    /// <item>the town is picked in the scene editor, its scenario opened and a way to the new
    /// scene typed in and saved;</item>
    /// <item>Go to scene puts the game into the town, and the player walks town → new scene →
    /// town by clicking the dialog's answers.</item>
    /// </list>
    /// Buttons, list rows, answers and text edits get the signals a mouse or keyboard would
    /// raise. The game's files are restored at the end (town scenario text, new files deleted).
    /// Result line: <c>[TEST_RESULT] name=novel_flow passed=N failed=M</c>, 150 s watchdog.
    /// </summary>
    public partial class NovelFlowTestDriver : Node
    {
        private const float TickSeconds = 0.3f;
        private const float TimeoutSeconds = 150f;
        private const string Mod = "my_novel_game";
        private const string Town = "my_novel_town";
        private const string Park = "my_novel_park";
        private const string TownScenario = "scenarios/my_novel_town.scenario";
        private const string ParkScenario = "scenarios/my_novel_park.scenario";
        private const string ToPark = "Go to the park";
        private const string ToTown = "Back to the town";
        // The town scenario before the test edits it; survives a run that is killed midway
        // (the next run restores it first, so an interrupted run never leaves the park behind).
        private const string Backup = "user://novel_flow_town.scenario.bak";

        private const string ParkText =
            "scene: " + Park + "\npriority: 10\n---\n" +
            "text \"A quiet park behind the town. Wet leaves, an empty bench.\"\n" +
            "select \"What next?\"\n" +
            "option \"" + ToTown + "\" -> :town\n\n" +
            ":town\nswitch_scene " + Town + "\nend\n";

        private readonly string shotsDir;
        private readonly List<string> failures = new();
        private int passed;
        private int step;
        private int wait;
        private double elapsed;
        private string townOriginal;
        private SceneEditorWindow sceneEditor;

        /// <summary>Creates the driver; <paramref name="shotsDir"/> null disables screenshots.</summary>
        public NovelFlowTestDriver(string shotsDir)
        {
            this.shotsDir = shotsDir;
            Name = "NovelFlowTestDriver";
            var timer = new Timer { WaitTime = TickSeconds, Autostart = true };
            AddChild(timer);
            timer.Timeout += Tick;
        }

        private SceneManager Scenes => GetTree().Root.GetNodeOrNull<SceneManager>("SceneManager");

        private void Tick()
        {
            if (step < 0)
                return;
            elapsed += TickSeconds;
            if (elapsed > TimeoutSeconds)
            {
                Fail($"timeout at step {step}");
                Finish();
                return;
            }

            try
            {
                RunStep();
            }
            catch (System.Exception ex)
            {
                Fail($"step {step}: {ex.GetType().Name}: {ex.Message}");
                Finish();
            }
        }

        private void RunStep()
        {
            switch (step)
            {
                case 0:
                    if (!Boot.IsReady || Scenes?.CurrentSceneName != Town || GodotSceneService.IsTransitioning)
                        return;
                    RestoreFromBackup();
                    townOriginal = ModFileService.ReadText(Mod, TownScenario);
                    File.WriteAllText(ProjectSettings.GlobalizePath(Backup), townOriginal ?? string.Empty);
                    DeleteParkFiles();
                    SceneManager.ReloadContent();
                    Check(townOriginal != null && !Scenes.HasScene(Park), "the novel runs in the town, no park yet");
                    OpenToolFromToolbar("tools/scene_editor/title");
                    Next();
                    return;
                case 1:
                    sceneEditor = Windows<SceneEditorWindow>().FirstOrDefault();
                    if (sceneEditor == null)
                        return;
                    Check(true, "the toolbar's tools menu opens the scene editor");
                    Press(sceneEditor, "tools/scene_editor/new_scene");
                    var name = sceneEditor.FindChildren("*", "", true, false).OfType<LineEdit>().First(e => e.PlaceholderText == "my_scene");
                    name.Text = Park;
                    name.EmitSignal(LineEdit.SignalName.TextChanged, Park);
                    var mods = sceneEditor.FindChildren("*", "", true, false).OfType<OptionButton>()
                        .First(o => Enumerable.Range(0, o.ItemCount).Any(i => o.GetItemText(i) == Mod));
                    var modIndex = Enumerable.Range(0, mods.ItemCount).First(i => mods.GetItemText(i) == Mod);
                    mods.Select(modIndex);
                    mods.EmitSignal(OptionButton.SignalName.ItemSelected, modIndex);
                    var picker = Lists(sceneEditor)[1];
                    var background = Row(picker, "home/vestibule") >= 0 ? Row(picker, "home/vestibule") : 0;
                    picker.EmitSignal(ItemList.SignalName.ItemActivated, background);
                    Next();
                    return;
                case 2:
                    Shot("1_new_scene");
                    Press(sceneEditor, "tools/common/save");
                    Check(Scenes.HasScene(Park), "Save in the scene editor: the game knows the new scene at once");
                    Check(File.Exists(ModFileService.GetWritablePath(Mod, "scenes/" + Park + ".json")), "the scene file is in the game's mod");
                    Press(sceneEditor, "tools/scene_editor/new_scenario");
                    Next();
                    return;
                case 3:
                    var parkEditor = EditorWith("scene: " + Park);
                    if (parkEditor == null)
                        return;
                    Check(true, "New scenario opens the scenario editor on the scene's new scenario");
                    Type(parkEditor, ParkText);
                    Press(parkEditor, "tools/common/save");
                    Check(ModFileService.ReadText(Mod, ParkScenario)?.Contains(ToTown) == true, "the park scenario is saved with the way back");
                    Next();
                    return;
                case 4:
                    // A tick after typing, so the frame shows the new text.
                    Shot("2_park_scenario");
                    PickScene(Town);
                    Next();
                    return;
                case 5:
                    var rows = sceneEditor.FindChildren("*", "", true, false).OfType<ItemList>().Last();
                    var townRow = Row(rows, Mod + ": " + TownScenario);
                    Check(townRow >= 0, "the town's scenario is listed under the town in the scene editor");
                    rows.Select(townRow);
                    rows.EmitSignal(ItemList.SignalName.ItemActivated, townRow);
                    Next();
                    return;
                case 6:
                    var townEditor = EditorWith("scene: " + Town);
                    if (townEditor == null)
                        return;
                    var text = Code(townEditor).Text;
                    text = text.Replace("option \"Go back home\"", "option \"" + ToPark + "\" -> :park\noption \"Go back home\"")
                           + "\n:park\nswitch_scene " + Park + "\nend\n";
                    Type(townEditor, text);
                    Press(townEditor, "tools/common/save");
                    Check(ModFileService.ReadText(Mod, TownScenario)?.Contains("switch_scene " + Park) == true, "the town scenario is saved with the way to the park");
                    Next();
                    return;
                case 7:
                    Shot("3_town_scenario");
                    Press(sceneEditor, "tools/scene_editor/go");
                    Next();
                    return;
                case 8:
                    if (++wait < 3 || GodotSceneService.IsTransitioning)
                        return;
                    foreach (var window in GetTree().Root.GetChildren().OfType<Window>().ToList())
                        window.EmitSignal(Window.SignalName.CloseRequested);
                    Next();
                    return;
                case 9:
                    if (!Answer(ToPark))
                        return;
                    Shot("4_town_choice");
                    Click(ToPark);
                    Next();
                    return;
                case 10:
                    if (Scenes.CurrentSceneName != Park || GodotSceneService.IsTransitioning || !Answer(ToTown))
                        return;
                    Check(true, "choosing the new answer in the town leads into the park");
                    Shot("5_park");
                    Click(ToTown);
                    Next();
                    return;
                case 11:
                    if (Scenes.CurrentSceneName != Town || GodotSceneService.IsTransitioning || !Answer(ToPark))
                        return;
                    Check(true, "the park's answer leads back to the town, which offers the park again");
                    Shot("6_back_in_town");
                    Finish();
                    return;
            }
        }

        // ── the author's hands ─────────────────────────────────────

        /// <summary>The toolbar's tools button opens its menu; the tool is picked by its title.</summary>
        private void OpenToolFromToolbar(string titleKey)
        {
            var menuTitle = TranslationServer.Translate("tools/menu_title").ToString();
            var button = GetTree().Root.FindChildren("*", "", true, false).OfType<Button>().First(b => b.TooltipText == menuTitle);
            button.EmitSignal(BaseButton.SignalName.Pressed);
            var menu = button.GetChildren().OfType<PopupMenu>().First();
            var title = TranslationServer.Translate(titleKey).ToString();
            var index = Enumerable.Range(0, menu.ItemCount).First(i => menu.GetItemText(i) == title);
            menu.EmitSignal(PopupMenu.SignalName.IndexPressed, index);
        }

        private void PickScene(string name)
        {
            var list = Lists(sceneEditor)[0];
            var row = Enumerable.Range(0, list.ItemCount).First(i => list.GetItemTooltip(i) == name);
            list.Select(row);
            list.EmitSignal(ItemList.SignalName.ItemSelected, row);
        }

        private static void Type(ScenarioEditorWindow editor, string text)
        {
            var code = Code(editor);
            code.Text = text;
            code.EmitSignal(TextEdit.SignalName.TextChanged);
        }

        private void Press(Node root, string key)
        {
            var text = TranslationServer.Translate(key).ToString();
            var button = root.FindChildren("*", "", true, false).OfType<Button>().FirstOrDefault(b => b.Text == text && b.IsVisibleInTree());
            Check(button != null && !button.Disabled, $"button '{text}' is there and enabled");
            button?.EmitSignal(BaseButton.SignalName.Pressed);
        }

        /// <summary>True when the dialog shows an answer with this text (advances the text before it).</summary>
        private bool Answer(string text)
        {
            if (AnswerItems().Any(a => a.Model?.Text == text))
                return true;
            if (++wait % 10 == 0)
                GameLogger.Log("TEST", $"waiting for answer '{text}' in '{Scenes?.CurrentSceneName}', shown: [{string.Join(", ", AnswerItems().Select(a => a.Model?.Text))}]");
            var box = SceneContextNode.FindNode<Node>("DialogBox") as IDialogBox;
            if (box != null && box.IsRunning && !box.IsWaitingAnswer)
                box.NextAction();
            return false;
        }

        private void Click(string text)
        {
            var item = AnswerItems().First(a => a.Model?.Text == text);
            item.GetNode<Button>("Button").EmitSignal(BaseButton.SignalName.Pressed);
        }

        private IEnumerable<AnswerItem> AnswerItems() =>
            GetTree().Root.FindChildren("*", "", true, false).OfType<AnswerItem>().Where(a => a.IsVisibleInTree());

        // ── finding things ─────────────────────────────────────────

        private IEnumerable<T> Windows<T>() where T : Window =>
            GetTree().Root.GetChildren().OfType<T>().Where(w => !w.IsQueuedForDeletion());

        private ScenarioEditorWindow EditorWith(string header) =>
            Windows<ScenarioEditorWindow>().FirstOrDefault(w => Code(w)?.Text.StartsWith(header) == true);

        private static CodeEdit Code(Node editor) =>
            editor.FindChildren("*", "", true, false).OfType<CodeEdit>().FirstOrDefault(c => c.IsVisibleInTree());

        private static List<ItemList> Lists(Node root) =>
            root.FindChildren("*", "", true, false).OfType<FilteredList>()
                .Select(f => f.FindChildren("*", "", true, false).OfType<ItemList>().First()).ToList();

        private static int Row(ItemList list, string text) =>
            Enumerable.Range(0, list.ItemCount).FirstOrDefault(i => list.GetItemText(i) == text, -1);

        // ── bookkeeping ────────────────────────────────────────────

        private static void RestoreFromBackup()
        {
            var path = ProjectSettings.GlobalizePath(Backup);
            if (!File.Exists(path))
                return;
            ModFileService.WriteText(Mod, TownScenario, File.ReadAllText(path));
            File.Delete(path);
            GameLogger.Log("TEST", "novel_flow: restored the town scenario left by an interrupted run");
        }

        private void DeleteParkFiles()
        {
            foreach (var file in new[] { "scenes/" + Park + ".json", ParkScenario })
            {
                var path = ModFileService.GetWritablePath(Mod, file);
                if (path != null && File.Exists(path))
                    File.Delete(path);
            }
        }

        private void Next()
        {
            step++;
            wait = 0;
        }

        private void Check(bool condition, string what)
        {
            if (condition)
            {
                passed++;
                GameLogger.Log("TEST", $"[PASS] {what}");
            }
            else
            {
                Fail(what);
            }
        }

        private void Fail(string what)
        {
            failures.Add(what);
            GameLogger.Log("TEST", $"[FAIL] {what}", LogLevel.Error);
        }

        private void Shot(string name)
        {
            if (string.IsNullOrEmpty(shotsDir) || DisplayServer.GetName() == "headless")
                return;
            DirAccess.MakeDirRecursiveAbsolute(shotsDir);
            ViewportCapture.SavePng(GetViewport(), shotsDir.TrimEnd('/') + "/novel_flow_" + name + ".png");
        }

        private void Finish()
        {
            step = -1;
            if (townOriginal != null)
                ModFileService.WriteText(Mod, TownScenario, townOriginal);
            File.Delete(ProjectSettings.GlobalizePath(Backup));
            DeleteParkFiles();
            SceneManager.ReloadContent();
            GameLogger.Log("TEST", $"[TEST_RESULT] name=novel_flow passed={passed} failed={failures.Count}");
            GetTree().Quit(failures.Count == 0 ? 0 : 1);
        }
    }
}
