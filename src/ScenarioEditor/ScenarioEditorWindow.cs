using System;
using Cthangover.Core.Scenes;
using Cthangover.Core.UI.Theming;
using Cthangover.Core.Utils;
using Cthangover.Mods.Interface.Theming;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Tools.Common;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Core.UI.Tool.ScenarioEditor
{
    /// <summary>
    /// Tool window hosting the scenario editor: a searchable tree of every
    /// mod's .scenario files, tabbed code/graph editors, a metadata panel that
    /// follows the text as it is typed, background thumbnails, plus a
    /// play-test mode that runs the current scenario in-game.
    /// <para/>
    /// Shortcuts: Ctrl+S saves the active file, F5 play-tests it.
    /// </summary>
    public partial class ScenarioEditorWindow : ToolWindow
    {
        private const double InfoRefreshDelay = 0.4;

        private Control _editLayout;

        private ScenarioFileTreePanel _fileTreePanel;
        private ScenarioEditTabs _editTabs;
        private ScenarioInfoPanel _infoPanel;
        private ScenarioThumbnailPanel _thumbPanel;
        private Timer _infoTimer;

        private Button _saveBtn;
        private Button _openFileBtn;
        private Button _playBtn;
        private Label _filePathLabel;

        private bool _playActive;
        private CanvasLayer _stopOverlay;

        public ScenarioEditorWindow() : base("tools/scenario_editor/title")
        {
            CthangoverTheme.Apply(this);
            BuildUI();
            SetHint(ToolUi.Tr("tools/scenario_editor/hint"));
        }

        /// <summary>Opens with the graph view so the editor starts on the cross-scene map.</summary>
        public override void _Ready()
        {
            _editTabs.SetViewToGraph();
        }

        /// <summary>Returns from play-test mode as soon as the in-game test play ends.</summary>
        public override void _Process(double delta)
        {
            if (_playActive && !SceneManager.IsTestPlayActive)
                ReturnFromPlay();
        }

        private void BuildUI()
        {
            _editLayout = new Control
            {
                AnchorRight = 1f,
                AnchorBottom = 1f
            };
            AddChild(_editLayout);

            var outerVBox = CreateFillContainer();
            _editLayout.AddChild(outerVBox);

            var toolbar = CreateToolbar();
            outerVBox.AddChild(toolbar);

            _saveBtn = ToolUi.Button("tools/common/save", OnSavePressed, ButtonKind.Primary, "tools/common/save_tip");
            toolbar.AddChild(_saveBtn);
            _playBtn = ToolUi.Button("tools/scenario_editor/play", OnPlayPressed, ButtonKind.Secondary, "tools/scenario_editor/play_tooltip");
            toolbar.AddChild(_playBtn);
            _openFileBtn = ToolUi.Button("tools/scenario_editor/open_file", OnOpenFilePressed, ButtonKind.Secondary, "tools/scenario_editor/open_file_tip");
            toolbar.AddChild(_openFileBtn);
            toolbar.AddChild(ToolUi.Divider());
            toolbar.AddChild(ToolUi.Button("tools/scenario_editor/rescan", OnRescanPressed, ButtonKind.Secondary, "tools/scenario_editor/rescan_tip"));

            _filePathLabel = new Label
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
            };
            _filePathLabel.AddThemeFontOverride("font", ToolUi.Mono());
            _filePathLabel.AddThemeColorOverride("font_color", UiTokens.Muted);
            _filePathLabel.AddThemeFontSizeOverride("font_size", UiTokens.FontBodySmall);
            toolbar.AddChild(_filePathLabel);

            var mainSplit = new HSplitContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            outerVBox.AddChild(mainSplit);

            var leftVBox = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
            var leftPanel = CreateSidebar(leftVBox, 320);
            mainSplit.AddChild(leftPanel);

            var tabsContainer = new TabContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 420)
            };
            leftVBox.AddChild(tabsContainer);

            _fileTreePanel = (ScenarioFileTreePanel)TscnScenes.LoadAndBuild("scenes/scenario_file_tree_panel.tscn");
            _fileTreePanel.FileSelected += OnFileSelected;
            tabsContainer.AddChild(_fileTreePanel);
            tabsContainer.SetTabTitle(0, ToolUi.Tr("tools/scenario_editor/tab_files"));

            _infoPanel = (ScenarioInfoPanel)TscnScenes.LoadAndBuild("scenes/scenario_info_panel.tscn");
            tabsContainer.AddChild(_infoPanel);
            tabsContainer.SetTabTitle(1, ToolUi.Tr("tools/scenario_editor/tab_info"));

            leftVBox.AddChild(ToolUi.Section("tools/scenario_editor/bg_previews"));
            _thumbPanel = (ScenarioThumbnailPanel)TscnScenes.LoadAndBuild("scenes/scenario_thumbnail_panel.tscn");
            leftVBox.AddChild(_thumbPanel);

            _editTabs = new ScenarioEditTabs(new ScenarioGraphView());
            _editTabs.TextTabSelected += OnTextTabSelected;
            _editTabs.GraphTabSelected += OnGraphTabSelected;
            _editTabs.DirtyChanged += OnDirtyChanged;
            _editTabs.GraphFileRequested += OnGraphFileRequested;
            _editTabs.BufferChanged += () => _infoTimer.Start();
            mainSplit.AddChild(_editTabs);

            _infoTimer = new Timer { OneShot = true, WaitTime = InfoRefreshDelay };
            _infoTimer.Timeout += () => _infoPanel.Update(_editTabs.CurrentText);
            AddChild(_infoTimer);
        }

        private void OnFileSelected(string filePath)
        {
            _editTabs.OpenFile(filePath);
        }

        private void OnGraphFileRequested(string filePath)
        {
            _fileTreePanel.SelectFile(filePath);
        }

        private void OnRescanPressed()
        {
            _fileTreePanel.Reload();
            _editTabs.RefreshGraph();
            SetStatus(ToolUi.Tr("tools/scenario_editor/rescanned"));
        }

        private void OnTextTabSelected(string text)
        {
            var hasFile = _editTabs.CurrentKey != null;
            _filePathLabel.Text = hasFile
                ? ScenarioFileService.GetDisplayPath(_editTabs.CurrentModId, _editTabs.CurrentRelativePath) : "";
            SetFileButtonsEnabled(hasFile);
            _infoPanel.Update(text);
            _thumbPanel.Load(text);
        }

        private void OnGraphTabSelected()
        {
            _filePathLabel.Text = "";
            SetFileButtonsEnabled(false);
            _infoPanel.Clear();
            _thumbPanel.Clear();
            _editTabs.RefreshGraph();
        }

        private void SetFileButtonsEnabled(bool enabled)
        {
            _saveBtn.Disabled = !enabled;
            _openFileBtn.Disabled = !enabled;
            _playBtn.Disabled = !enabled;
        }

        private void OnDirtyChanged(bool anyDirty)
        {
            if (anyDirty)
                SetDirty();
            else
                MarkClean();
        }

        /// <summary>Ctrl+S.</summary>
        protected override void OnSaveShortcut() => OnSavePressed();

        /// <summary>F5 play-tests the active scenario.</summary>
        protected override bool HandleKey(InputEventKey key)
        {
            if (key.Keycode != Key.F5 || !_editTabs.HasActiveFile)
                return false;

            OnPlayPressed();
            return true;
        }

        private void OnSavePressed()
        {
            if (!_editTabs.HasActiveFile)
                return;

            var modId = _editTabs.CurrentModId;
            var relativePath = _editTabs.CurrentRelativePath;
            var text = _editTabs.CurrentText;
            try
            {
                if (!ScenarioFileService.WriteAllText(modId, relativePath, text))
                {
                    SetStatus(ToolUi.Tr("tools/scenario_editor/readonly_zip"), ToolStatusKind.Error);
                    return;
                }

                _editTabs.SaveCurrentFile();
                SetStatus(string.Format(ToolUi.Tr("tools/common/saved"), ScenarioFileService.GetDisplayPath(modId, relativePath)), ToolStatusKind.Ok);
                _infoPanel.Update(text);
                _thumbPanel.Load(text);
                GameLogger.Log("SCENARIO_EDITOR", $"Saved: {modId}/{relativePath}");
            }
            catch (Exception ex)
            {
                GameLogger.Log("SCENARIO_EDITOR", $"Save failed: {ex.Message}", LogLevel.Error);
                SetStatus(string.Format(ToolUi.Tr("tools/scenario_editor/save_error"), ex.Message), ToolStatusKind.Error);
            }
        }

        private void OnOpenFilePressed()
        {
            if (_editTabs.CurrentKey == null)
                return;

            var path = ScenarioFileService.GetAbsolutePath(_editTabs.CurrentModId, _editTabs.CurrentRelativePath);
            if (string.IsNullOrEmpty(path))
            {
                SetStatus(ToolUi.Tr("tools/scenario_editor/readonly_zip"), ToolStatusKind.Error);
                return;
            }

            var err = OS.ShellOpen(path);
            if (err != Error.Ok)
            {
                GameLogger.Log("SCENARIO_EDITOR", $"ShellOpen '{path}' failed: {err}", LogLevel.Error);
                SetStatus($"{ToolUi.Tr("tools/scenario_editor/open_file_error")}: {err}", ToolStatusKind.Error);
            }
        }

        private void OnPlayPressed()
        {
            if (!_editTabs.HasActiveFile)
                return;

            var text = _editTabs.CurrentText;
            var metadata = ScenarioFileService.ParseMetadata(text);

            if (metadata.Scene == "-")
            {
                SetStatus(ToolUi.Tr("tools/scenario_editor/no_scene_error"), ToolStatusKind.Error);
                return;
            }

            var modId = _editTabs.CurrentModId;
            var relativePath = _editTabs.CurrentRelativePath;
            try
            {
                if (ScenarioFileService.WriteAllText(modId, relativePath, text))
                {
                    _editTabs.SaveCurrentFile();
                }
                else
                {
                    GameLogger.Log("SCENARIO_EDITOR",
                        $"Test play: '{modId}/{relativePath}' is read-only, playing from the editor buffer",
                        LogLevel.Warning);
                }
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(ToolUi.Tr("tools/scenario_editor/save_error"), ex.Message), ToolStatusKind.Error);
                return;
            }

            SceneManager.IsTestPlayActive = true;
            SceneManager.TestScenarioText = text;

            GameLogger.Log("SCENARIO_EDITOR", $"Test play START: scene='{metadata.Scene}', queue={text.Split('\n').Length} lines");

            Visible = false;

            _stopOverlay = CreateStopOverlay();
            var tree = Godot.Engine.GetMainLoop() as SceneTree;
            tree?.Root.AddChild(_stopOverlay);

            var sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
            if (sceneManager != null)
            {
                sceneManager.Initialize();
                sceneManager.PendingSceneName = metadata.Scene;
            }

            var sceneService = GetNodeOrNull<GodotSceneService>("/root/GodotSceneService");
            sceneService?.LoadShell();

            _playActive = true;
        }

        private void OnStopPressed()
        {
            SceneManager.IsTestPlayActive = false;
            ReturnFromPlay();
        }

        private void ReturnFromPlay()
        {
            _playActive = false;
            Visible = true;

            if (_stopOverlay != null)
            {
                _stopOverlay.QueueFree();
                _stopOverlay = null;
            }

            var sceneService = GetNodeOrNull<GodotSceneService>("/root/GodotSceneService");
            sceneService?.SwitchToMenu();
            SetStatus(ToolUi.Tr("tools/scenario_editor/play_stopped"));
        }

        /// <summary>Stop button shown over the running game while the scenario is play-tested.</summary>
        private CanvasLayer CreateStopOverlay()
        {
            var layer = new CanvasLayer { Layer = 128 };

            var btn = ToolUi.Button("tools/scenario_editor/stop", OnStopPressed, ButtonKind.Danger);
            btn.Theme = ToolTheme.Get();
            btn.CustomMinimumSize = new Vector2(120, 36);

            var margin = new MarginContainer();
            margin.AddThemeConstantOverride("margin_top", UiTokens.Space3);
            margin.AddThemeConstantOverride("margin_left", UiTokens.Space3);
            margin.AddChild(btn);

            layer.AddChild(margin);
            return layer;
        }
    }
}
