using System.Collections.Generic;
using System.IO;
using Cthangover.Core.Bootstrap;
using Cthangover.Core.UI.Tool;
using Cthangover.Core.Utils;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Mods.Interface.View;
using Godot;

namespace Cthangover.Tools.Tests.Windows
{
    /// <summary>
    /// Opens every registered tool window one by one through the same path the
    /// toolbar button uses (<see cref="ToolFactory"/> → <see cref="ToolWindow.ShowWindow"/>),
    /// waits for visible layout and two real frames (visual settles with screenshots), checks that it entered the tree
    /// visible and inside the game viewport, optionally saves a screenshot and
    /// closes it. A window that throws while constructing or laying out fails
    /// only its own check, the others still run. Ends with the runner contract
    /// <c>[TEST_RESULT] name=tools_windows passed=N failed=M</c> and the exit code.
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
        private bool _exercised;
        private ulong _waitStarted;
        private ulong _readyFrame;
        private readonly double _tickSeconds;
        private Window _window;
        private readonly ToolWindowsErrorLogger _errors = new();

        /// <summary>Creates the driver; <paramref name="shotsDir"/> null or empty disables screenshots.</summary>
        public ToolWindowsTestDriverNode(string shotsDir)
        {
            _shotsDir = shotsDir;
            _tickSeconds = string.IsNullOrEmpty(shotsDir) ? 0.05 : TickSeconds;
            OS.AddLogger(_errors);
            var timer = new Timer { WaitTime = _tickSeconds, Autostart = true };
            AddChild(timer);
            timer.Timeout += Tick;
        }

        private void Tick()
        {
            if (_finished)
                return;

            _elapsed += _tickSeconds;
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

                _tools.AddRange(ToolFactory.Instance.GetAll());
                Check(_tools.Count > 0, $"tools registered ({_tools.Count})");
                var toolbarTools = new HashSet<string>();
                foreach (var button in ToolBoxButtonFactory.Instance.GetVisible())
                    toolbarTools.Add(button.ToolId);
                foreach (var tool in _tools)
                    Check(toolbarTools.Contains(tool.Id), $"{tool.Id}: reachable from the in-game toolbar");
                OpenNext();
                return;
            }

            if (_window == null)
                return;

            if (string.IsNullOrEmpty(_shotsDir))
            {
                AdvanceWhenReady();
                return;
            }

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

        private void AdvanceWhenReady()
        {
            var id = _tools[_toolIndex].Id;
            var ready = GodotObject.IsInstanceValid(_window) && _window.IsInsideTree()
                && _window.Visible && _window.Size.X > 0 && _window.Size.Y > 0;
            // This exercise intentionally schedules a metadata debounce while detached.
            if (ready && _exercised && id == "scenario_editor")
                ready = MetadataIsReady();
            if (Time.GetTicksMsec() - _waitStarted > 10000)
            {
                Fail($"{id}: timed out waiting for {(_exercised ? "exercise completion" : "visible layout")}");
                Finish();
                return;
            }
            if (!ready)
            {
                _readyFrame = 0;
                return;
            }
            if (_readyFrame == 0)
                _readyFrame = Engine.GetProcessFrames();
            if (Engine.GetProcessFrames() - _readyFrame < 2)
                return;
            if (!_exercised)
            {
                try { ToolWindowsExercises.Run(id, _window, Check); }
                catch (System.Exception ex)
                {
                    Fail($"{id}: interaction threw {ex.GetType().Name}: {ex.Message}");
                }
                _exercised = true;
                _waitStarted = Time.GetTicksMsec();
                _readyFrame = 0;
                return;
            }
            InspectAndClose();
            OpenNext();
        }

        private bool MetadataIsReady()
        {
            foreach (var child in _window.FindChildren("InfoScene", "", true, false))
                if (child is Label label && label.Text == "timer_lifecycle_probe")
                    return true;
            return false;
        }

        private void OpenNext()
        {
            _toolIndex++;
            if (_toolIndex >= _tools.Count)
            {
                Finish();
                return;
            }

            var tool = _tools[_toolIndex];
            _errors.Drain();
            try
            {
                _window = ToolWindow.ShowWindow(tool.CreateWindow());
                _settle = SettleTicks;
                _exercised = false;
                _waitStarted = Time.GetTicksMsec();
                _readyFrame = 0;
            }
            catch (System.Exception ex)
            {
                Fail($"{tool.Id}: open threw {ex.GetType().Name}: {ex.Message}");
                _window = null;
                OpenNext();
            }
        }

        private void InspectAndClose()
        {
            var id = _tools[_toolIndex].Id;
            var window = _window;
            _window = null;

            ToolWindowsExercises.Verify(id, window, Check);
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
            Cthangover.Core.Bootstrap.ApplicationExit.Quit(GetTree(), _failed == 0 ? 0 : 1);
        }

        private static void Log(string message) => GameLogger.Log("TEST", message);
    }
}
