using System.Collections.Generic;
using System.IO;
using Cthangover.Core.Bootstrap;
using Cthangover.Core.Games;
using Cthangover.Core.UI.Tool;
using Cthangover.Core.Utils;
using Cthangover.Mods.Interface.Menu;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Tools.Hub;
using Cthangover.Mods.Interface.View;
using Godot;

namespace Cthangover.Tools.Tests.Windows
{
    /// <summary>
    /// Opens every registered tool window one by one through the same path the
    /// toolbar button uses (<see cref="ToolFactory"/> → <see cref="ToolWindow.ShowWindow"/>),
    /// lets it lay out for a couple of ticks, checks that it entered the tree
    /// visible and inside the game viewport, optionally saves a screenshot and
    /// closes it. A window that throws while constructing or laying out fails
    /// only its own check, the others still run. Ends with the runner contract
    /// <c>[TEST_RESULT] name=tools_windows passed=N failed=M</c> and the exit code.
    /// <para/>
    /// Launched as the <c>novel_editor</c> game (<c>--game=novel_editor --new</c>)
    /// it first waits for the collection's entry scene, the
    /// <see cref="NovelEditorHub"/>, and checks it lists a launcher button for
    /// every registered tool before opening the windows; after the windows it
    /// plays a scenario from the editor and checks that both the scenario's
    /// end and Stop return to the hub (<see cref="ScenarioPlayReturnCheck"/>).
    /// </summary>
    public partial class ToolWindowsTestDriverNode : Node
    {
        private const string ResultName = "tools_windows";
        private const float TickSeconds = 0.5f;
        private const float TimeoutSeconds = 90f;
        private const int SettleTicks = 4;
        private const int ExerciseTick = 2;

        private readonly string _shotsDir;
        private readonly List<IToolProvider> _tools = new();
        private int _passed;
        private int _failed;
        private int _toolIndex = -1;
        private int _settle;
        private double _elapsed;
        private bool _finished;
        private Window _window;
        private bool _inHub;
        private ScenarioPlayReturnCheck _playCheck;
        private readonly ToolWindowsErrorLogger _errors = new();

        /// <summary>Creates the driver; <paramref name="shotsDir"/> null or empty disables screenshots.</summary>
        public ToolWindowsTestDriverNode(string shotsDir)
        {
            _shotsDir = shotsDir;
            OS.AddLogger(_errors);
            var timer = new Timer { WaitTime = TickSeconds, Autostart = true };
            AddChild(timer);
            timer.Timeout += Tick;
        }

        private void Tick()
        {
            if (_finished)
                return;

            _elapsed += TickSeconds;
            if (_elapsed >= TimeoutSeconds)
            {
                Fail($"timeout ({TimeoutSeconds:0}s) at tool #{_toolIndex}");
                Finish();
                return;
            }

            if (_toolIndex < 0)
            {
                var tree = Godot.Engine.GetMainLoop() as SceneTree;
                if (!Boot.IsReady || tree?.CurrentScene == null)
                    return;

                _inHub = ActiveGame.Id == NovelEditorHub.SceneName;
                if (_inHub && tree.CurrentScene is not NovelEditorHub)
                    return;

                _tools.AddRange(ToolFactory.Instance.GetAll());
                Check(_tools.Count > 0, $"tools registered ({_tools.Count})");
                if (_inHub)
                    CheckHub(tree.CurrentScene);
                OpenNext();
                return;
            }

            if (_playCheck != null)
            {
                if (_playCheck.Tick(GetTree()))
                    Finish();
                return;
            }

            if (_window == null)
                return;

            if (--_settle == ExerciseTick)
            {
                try
                {
                    ToolWindowsExercises.Run(_tools[_toolIndex].Id, _window, Check);
                }
                catch (System.Exception ex)
                {
                    Fail($"{_tools[_toolIndex].Id}: interaction threw {ex.GetType().Name}: {ex.Message}");
                }
            }

            if (_settle > 0)
                return;

            InspectAndClose();
            OpenNext();
        }

        private void OpenNext()
        {
            _toolIndex++;
            if (_toolIndex >= _tools.Count)
            {
                if (_inHub)
                    _playCheck = new ScenarioPlayReturnCheck(Check);
                else
                    Finish();
                return;
            }

            var tool = _tools[_toolIndex];
            _errors.Drain();
            try
            {
                _window = ToolWindow.ShowWindow(tool.CreateWindow());
                _settle = SettleTicks;
            }
            catch (System.Exception ex)
            {
                Fail($"{tool.Id}: open threw {ex.GetType().Name}: {ex.Message}");
                _window = null;
                OpenNext();
            }
        }

        /// <summary>Every registered tool has its launcher button on the hub screen.</summary>
        private void CheckHub(Node hub)
        {
            var keys = new HashSet<string>();
            CollectButtonKeys(hub, keys);
            foreach (var tool in _tools)
                Check(keys.Contains(tool.LocaleKey), $"hub: launcher button for {tool.Id}");
            SaveShot("novel_editor_hub");
        }

        private static void CollectButtonKeys(Node node, HashSet<string> keys)
        {
            if (node is Button button && button.HasMeta(MenuBuilder.LocaleMetaKey))
                keys.Add(button.GetMeta(MenuBuilder.LocaleMetaKey).AsString());
            foreach (var child in node.GetChildren())
                CollectButtonKeys(child, keys);
        }

        private void InspectAndClose()
        {
            var id = _tools[_toolIndex].Id;
            var window = _window;
            _window = null;

            var errors = _errors.Drain();
            Check(errors.Count == 0, $"{id}: no engine errors while opening" + (errors.Count > 0 ? ": " + string.Join(" | ", errors) : ""));
            Check(GodotObject.IsInstanceValid(window) && window.IsInsideTree() && window.Visible, $"{id}: window visible in tree");
            if (!GodotObject.IsInstanceValid(window))
                return;

            var viewport = GetTree().Root.GetVisibleRect().Size;
            Check(window.Size.X <= viewport.X + 1 && window.Size.Y <= viewport.Y + 1,
                $"{id}: window {window.Size} fits viewport {viewport}");

            SaveShot(id);
            window.QueueFree();
        }

        private void SaveShot(string id)
        {
            if (string.IsNullOrEmpty(_shotsDir) || DisplayServer.GetName() == "headless")
                return;

            var dir = ProjectSettings.GlobalizePath(_shotsDir);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, id + ".png");
            ViewportCapture.SavePng(GetViewport(), path);
            Log($"screenshot {path}");
        }

        private void Check(bool ok, string name)
        {
            if (ok)
            {
                _passed++;
                Log($"[PASS] {name}");
            }
            else
            {
                Fail(name);
            }
        }

        private void Fail(string message)
        {
            _failed++;
            Log($"[FAIL] {message}");
        }

        private void Finish()
        {
            if (_finished)
                return;
            _finished = true;
            OS.RemoveLogger(_errors);
            GameLogger.Log("TEST", $"[TEST_RESULT] name={ResultName} passed={_passed} failed={_failed}");
            GetTree().Quit(_failed == 0 ? 0 : 1);
        }

        private static void Log(string message) => GameLogger.Log("TEST", message);
    }
}
