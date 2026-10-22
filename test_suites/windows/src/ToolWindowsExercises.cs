using System;
using System.IO;
using System.Linq;
using Cthangover.Core.Scenes;
using Cthangover.Core.UI.Tool.LightEditor;
using Cthangover.Core.UI.Tool.ScenarioEditor;
using Cthangover.Tools.InteractiveEditor;
using Cthangover.Tools.Common;
using Cthangover.Tools.MapEditor;
using Cthangover.Tools.SceneEditor;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Tools.Tests.Windows
{
    /// <summary>
    /// Per-tool interaction checks run on an open window, through the same
    /// public surface the UI uses: canvas input is delivered by emitting the
    /// canvas' <c>gui_input</c> signal (the editors subscribe to it), data is
    /// read back from the canvas' public controller. Nothing is saved — the
    /// window is closed without writing afterwards.
    /// </summary>
    public static class ToolWindowsExercises
    {
        /// <summary>Runs the checks for <paramref name="toolId"/>; <paramref name="check"/> records each result.</summary>
        public static void Run(string toolId, Window window, Action<bool, string> check)
        {
            switch (toolId)
            {
                case "map_editor":
                    MapDrawing(window, check);
                    break;
                case "interactive_editor":
                    InteractivePolygon(window, check);
                    break;
                case "light_editor":
                    LightAdd(window, check);
                    break;
                case "scene_editor":
                    SceneCreate(window, check);
                    break;
                case "scenario_editor":
                    var tree = Find<ScenarioFileTreePanel>(window)?.FindChildren("*", nameof(Tree), true, false).OfType<Tree>().FirstOrDefault();
                    check(tree?.GetRoot()?.GetChildCount() > 0, "scenario_editor: file tree lists mods with scenarios");
                    break;
            }
        }

        /// <summary>Draws a triangle; the finishing double-click must not add a fourth vertex.</summary>
        private static void MapDrawing(Window window, Action<bool, string> check)
        {
            var canvas = Find<MapEditorCanvas>(window);
            if (canvas?.Controller == null)
            {
                check(false, "map_editor: canvas found");
                return;
            }

            var zone = canvas.Controller.StartRegion();
            canvas.SetMode(MapEditorCanvas.ModeDraw);
            var rect = canvas.GetBackgroundRect();
            Click(canvas, rect.Position + rect.Size * new Vector2(0.2f, 0.2f), false);
            Click(canvas, rect.Position + rect.Size * new Vector2(0.4f, 0.2f), false);
            Click(canvas, rect.Position + rect.Size * new Vector2(0.3f, 0.4f), false);
            Click(canvas, rect.Position + rect.Size * new Vector2(0.3f, 0.4f), true);

            check(zone.Points.Count == 3, $"map_editor: drawn region has 3 vertices (got {zone.Points.Count})");
            check(canvas.Mode == MapEditorCanvas.ModeSelect, "map_editor: double-click finishes drawing");
            check(Mathf.Abs(zone.Points[0].X - 0.2f) < 0.01f && Mathf.Abs(zone.Points[0].Y - 0.2f) < 0.01f,
                $"map_editor: vertex lands at the clicked background point (got {zone.Points[0]})");
        }

        /// <summary>A new object switched to polygon gets 4 vertices; a click on empty background appends one.</summary>
        private static void InteractivePolygon(Window window, Action<bool, string> check)
        {
            var canvas = Find<InteractiveEditorCanvas>(window);
            if (canvas?.Controller == null)
            {
                check(false, "interactive_editor: canvas found");
                return;
            }

            var def = canvas.Controller.Add();
            InteractiveEditorController.SetHitType(def, "polygon");
            canvas.RebuildHandles();
            check(def.HitArea.Vertices?.Length == 4, "interactive_editor: rect converts to a 4-vertex polygon");

            var rect = canvas.GetBackgroundRect();
            Click(canvas, rect.Position + rect.Size * new Vector2(0.97f, 0.97f), false);
            check(def.HitArea.Vertices?.Length == 5, $"interactive_editor: click on empty background adds a vertex (got {def.HitArea.Vertices?.Length})");
        }

        /// <summary>A double-click on the background adds a light at that point.</summary>
        private static void LightAdd(Window window, Action<bool, string> check)
        {
            var canvas = Find<LightEditorCanvas>(window);
            if (canvas?.Controller == null)
            {
                check(false, "light_editor: canvas found");
                return;
            }

            var before = canvas.Controller.Lights.Count;
            if (before >= LightEditorController.MaxLights)
                return;

            var rect = canvas.GetBackgroundRect();
            Click(canvas, rect.Position + rect.Size * new Vector2(0.75f, 0.25f), true);
            var light = canvas.Controller.Selected;
            check(canvas.Controller.Lights.Count == before + 1 && light != null
                  && Mathf.Abs(light.X - 0.75f) < 0.01f && Mathf.Abs(light.Y - 0.25f) < 0.01f,
                "light_editor: double-click adds a light at the clicked point");
        }

        /// <summary>
        /// Creates a scene the way an author does — New scene, a name, the test fixture mod,
        /// a double-clicked background, Save — and checks that the running game knows it and
        /// that no scenario was made for it (a scene may hold only interactives); then removes
        /// the files again. The scene goes
        /// into editor_tools itself: it is always loaded with the window, while a test fixture
        /// mod is not (--all-mods skips fixtures). Skipped when the mod is not a folder mod.
        /// </summary>
        private static void SceneCreate(Window window, Action<bool, string> check)
        {
            const string mod = "editor_tools";
            const string name = "zz_tools_test_scene";
            var lists = window.FindChildren("*", "", true, false).OfType<FilteredList>().ToList();
            check(lists.Count == 2 && lists[0].Count > 0, $"scene_editor: scene list shows the game's scenes ({(lists.Count > 0 ? lists[0].Count : 0)})");
            var modSelect = window.FindChildren("*", "", true, false).OfType<OptionButton>()
                .FirstOrDefault(o => Enumerable.Range(0, o.ItemCount).Any(i => o.GetItemText(i) == mod));
            if (modSelect == null || lists.Count < 2 || lists[1].Count == 0)
                return;

            var scenePath = ModFileService.GetWritablePath(mod, SceneFileEntry.PathFor(name));
            var scenarioPath = ModFileService.GetWritablePath(mod, "scenarios/" + name + ".scenario");
            try
            {
                DeleteLeftovers(scenePath, scenarioPath);
                PressButton(window, "tools/scene_editor/new_scene");
                var nameEdit = window.FindChildren("*", "", true, false).OfType<LineEdit>().First(e => e.PlaceholderText == "my_scene");
                nameEdit.Text = name;
                nameEdit.EmitSignal(LineEdit.SignalName.TextChanged, name);
                var index = Enumerable.Range(0, modSelect.ItemCount).First(i => modSelect.GetItemText(i) == mod);
                modSelect.Select(index);
                modSelect.EmitSignal(OptionButton.SignalName.ItemSelected, index);
                var picker = lists[1].FindChildren("*", "", true, false).OfType<ItemList>().First();
                picker.EmitSignal(ItemList.SignalName.ItemActivated, 0);
                PressButton(window, "tools/common/save");

                var manager = window.GetTree().Root.GetNodeOrNull<SceneManager>("SceneManager");
                check(File.Exists(scenePath) && File.ReadAllText(scenePath).Contains("\"defaultBackground\""), "scene_editor: Save writes the scene file with its background");
                check(!File.Exists(scenarioPath), "scene_editor: a new scene is saved without a scenario");
                check(manager?.HasScene(name) == true, "scene_editor: the running game knows the new scene without a reload");
            }
            finally
            {
                DeleteLeftovers(scenePath, scenarioPath);
                SceneFileService.RefreshGame();
            }
        }

        /// <summary>Removes the test scene's files and the folders they alone needed (editor_tools has no scenes of its own).</summary>
        private static void DeleteLeftovers(string scenePath, string scenarioPath)
        {
            foreach (var path in new[] { scenePath, scenarioPath })
            {
                if (path != null && File.Exists(path))
                    File.Delete(path);
                DeleteIfEmpty(Path.GetDirectoryName(path));
            }
        }

        /// <summary>Deletes a folder left empty by the test.</summary>
        private static void DeleteIfEmpty(string directory)
        {
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }

        private static void PressButton(Node root, string key)
        {
            var text = TranslationServer.Translate(key).ToString();
            var button = root.FindChildren("*", "", true, false).OfType<Button>().First(b => b.Text == text);
            button.EmitSignal(BaseButton.SignalName.Pressed);
        }

        private static void Click(Control target, Vector2 local, bool doubleClick)
        {
            var ev = new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                DoubleClick = doubleClick,
                Position = local
            };
            target.EmitSignal(Control.SignalName.GuiInput, ev);
        }

        private static T Find<T>(Node root) where T : Node
        {
            foreach (var child in root.GetChildren())
            {
                if (child is T match)
                    return match;
                var nested = Find<T>(child);
                if (nested != null)
                    return nested;
            }
            return null;
        }
    }
}
