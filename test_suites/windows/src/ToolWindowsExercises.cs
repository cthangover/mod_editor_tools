using System;
using System.Linq;
using Cthangover.Core.UI.Tool.LightEditor;
using Cthangover.Core.UI.Tool.ScenarioEditor;
using Cthangover.Tools.InteractiveEditor;
using Cthangover.Tools.MapEditor;
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
