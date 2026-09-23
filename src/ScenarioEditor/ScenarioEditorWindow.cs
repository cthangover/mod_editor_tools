using System;
using Cthangover.Core.Scenes;
using Cthangover.Core.Utils;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Core.UI.Tool.ScenarioEditor
{
    public partial class ScenarioEditorWindow : ToolWindow
    {
        private Control _editLayout;
        private Control _playLayout;

        private ScenarioFileTreePanel _fileTreePanel;
        private ScenarioEditTabs _editTabs;
        private ScenarioInfoPanel _infoPanel;
        private ScenarioThumbnailPanel _thumbPanel;

        private Button _saveBtn;
        private Button _openFileBtn;
        private Button _playBtn;
        private Label _filePathLabel;
        private Label _statusLabel;

        private bool _playActive;
        private CanvasLayer _stopOverlay;

        public ScenarioEditorWindow() : base("tools/scenario_editor/title")
        {
            BuildUI();
        }

        public override void _Ready()
        {
            _editTabs.SetViewToGraph();
        }

        protected override void Cleanup()
        {
        }

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
                AnchorBottom = 1f,
                Visible = true
            };
            AddChild(_editLayout);

            var outerVBox = CreateFillContainer();
            _editLayout.AddChild(outerVBox);

            var toolbar = CreateToolbar();
            outerVBox.AddChild(toolbar);

            _saveBtn = new Button
            {
                Text = TranslationServer.Translate("tools/scenario_editor/save"),
                Disabled = true
            };
            _saveBtn.Pressed += OnSavePressed;
            toolbar.AddChild(_saveBtn);

            _openFileBtn = new Button
            {
                Text = TranslationServer.Translate("tools/scenario_editor/open_file"),
                Disabled = true
            };
            _openFileBtn.Pressed += OnOpenFilePressed;
            toolbar.AddChild(_openFileBtn);

            _playBtn = new Button
            {
                Text = TranslationServer.Translate("tools/scenario_editor/play"),
                TooltipText = TranslationServer.Translate("tools/scenario_editor/play_tooltip"),
                Disabled = true
            };
            _playBtn.Pressed += OnPlayPressed;
            toolbar.AddChild(_playBtn);

            _filePathLabel = new Label { Text = "" };
            _filePathLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            toolbar.AddChild(_filePathLabel);

            var mainSplit = new HSplitContainer();
            mainSplit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            mainSplit.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            outerVBox.AddChild(mainSplit);

            var leftPanel = new PanelContainer
            {
                CustomMinimumSize = new Vector2(340, 0),
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            mainSplit.AddChild(leftPanel);

            var leftVBox = new VBoxContainer();
            leftPanel.AddChild(leftVBox);

            var tabsContainer = new TabContainer();
            tabsContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            tabsContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            leftVBox.AddChild(tabsContainer);

            _fileTreePanel = (ScenarioFileTreePanel)TscnScenes.LoadAndBuild("scenes/scenario_file_tree_panel.tscn");
            _fileTreePanel.FileSelected += OnFileSelected;
            tabsContainer.AddChild(_fileTreePanel);
            tabsContainer.SetTabTitle(0, TranslationServer.Translate("tools/scenario_editor/tab_files"));

            _infoPanel = (ScenarioInfoPanel)TscnScenes.LoadAndBuild("scenes/scenario_info_panel.tscn");
            tabsContainer.AddChild(_infoPanel);
            tabsContainer.SetTabTitle(1, TranslationServer.Translate("tools/scenario_editor/tab_info"));

            leftVBox.AddChild(new Label { Text = TranslationServer.Translate("tools/scenario_editor/bg_previews") + ":" });

            _thumbPanel = (ScenarioThumbnailPanel)TscnScenes.LoadAndBuild("scenes/scenario_thumbnail_panel.tscn");
            leftVBox.AddChild(_thumbPanel);

            var rightPanel = new PanelContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            mainSplit.AddChild(rightPanel);

            _editTabs = new ScenarioEditTabs(new ScenarioGraphView(), GetMonospaceFont());
            _editTabs.TextTabSelected += OnTextTabSelected;
            _editTabs.GraphTabSelected += OnGraphTabSelected;
            _editTabs.DirtyChanged += OnDirtyChanged;
            _editTabs.GraphFileRequested += OnGraphFileRequested;
            rightPanel.AddChild(_editTabs);

            var statusBar = new HBoxContainer();
            outerVBox.AddChild(statusBar);

            _statusLabel = new Label { Text = "" };
            statusBar.AddChild(_statusLabel);

            _playLayout = new Control
            {
                AnchorRight = 1f,
                AnchorBottom = 1f,
                Visible = false
            };
            AddChild(_playLayout);

            var stopContainer = new HBoxContainer();
            stopContainer.Alignment = BoxContainer.AlignmentMode.Center;
            stopContainer.AnchorRight = 0.5f;
            stopContainer.AnchorLeft = 0.5f;
            stopContainer.OffsetTop = 8;
            _playLayout.AddChild(stopContainer);

            var stopBtn = new Button
            {
                Text = TranslationServer.Translate("tools/scenario_editor/stop"),
                CustomMinimumSize = new Vector2(120, 40)
            };
            stopBtn.Pressed += OnStopPressed;
            stopContainer.AddChild(stopBtn);
        }

        private void OnFileSelected(string filePath)
        {
            _editTabs.OpenFile(filePath);
        }

        private void OnGraphFileRequested(string filePath)
        {
            _fileTreePanel.SelectFile(filePath);
        }

        private void OnTextTabSelected(string text)
        {
            _filePathLabel.Text = _editTabs.CurrentKey != null
                ? ScenarioFileService.GetDisplayPath(_editTabs.CurrentModId, _editTabs.CurrentRelativePath) : "";
            _saveBtn.Disabled = false;
            _openFileBtn.Disabled = false;
            _playBtn.Disabled = false;
            _statusLabel.Text = _editTabs.IsCurrentDirty
                ? TranslationServer.Translate("tools/scenario_editor/status_modified") : "";
            _infoPanel.Update(text);
            _thumbPanel.Load(text);
        }

        private void OnGraphTabSelected()
        {
            _filePathLabel.Text = "";
            _saveBtn.Disabled = true;
            _openFileBtn.Disabled = true;
            _playBtn.Disabled = true;
            _statusLabel.Text = "";
            _infoPanel.Clear();
            _thumbPanel.Clear();
            _editTabs.RefreshGraph();
        }

        private void OnDirtyChanged(bool anyDirty)
        {
            if (anyDirty)
            {
                SetDirty();
                _statusLabel.Text = TranslationServer.Translate("tools/scenario_editor/status_modified");
            }
            else
            {
                MarkClean();
            }
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
                    _statusLabel.Text = TranslationServer.Translate("tools/scenario_editor/readonly_zip");
                    return;
                }

                _editTabs.SaveCurrentFile();
                _statusLabel.Text = TranslationServer.Translate("tools/scenario_editor/status_saved");
                _infoPanel.Update(text);
                _thumbPanel.Load(text);
                _editTabs.RefreshGraph();
                GameLogger.Log("SCENARIO_EDITOR", $"Saved: {modId}/{relativePath}");
            }
            catch (Exception ex)
            {
                GameLogger.Log("SCENARIO_EDITOR", $"Save failed: {ex.Message}", LogLevel.Error);
                _statusLabel.Text = string.Format(
                    TranslationServer.Translate("tools/scenario_editor/save_error"), ex.Message);
            }
        }

        private void OnOpenFilePressed()
        {
            if (_editTabs.CurrentKey == null)
                return;

            var path = ScenarioFileService.GetAbsolutePath(_editTabs.CurrentModId, _editTabs.CurrentRelativePath);
            if (string.IsNullOrEmpty(path))
            {
                _statusLabel.Text = TranslationServer.Translate("tools/scenario_editor/readonly_zip");
                return;
            }

            GD.Print($"[ScenarioEditor] Opening: {path}");
            var err = OS.ShellOpen(path);
            if (err != Error.Ok)
                GameLogger.Log("SCENARIO_EDITOR", $"ShellOpen error: {err}", LogLevel.Error);
        }

        private void OnPlayPressed()
        {
            if (!_editTabs.HasActiveFile)
                return;

            var text = _editTabs.CurrentText;
            var metadata = ScenarioFileService.ParseMetadata(text);

            if (metadata.Scene == "-")
            {
                _statusLabel.Text = TranslationServer.Translate("tools/scenario_editor/no_scene_error");
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
                    _statusLabel.Text = TranslationServer.Translate("tools/scenario_editor/readonly_zip");
                    GameLogger.Log("SCENARIO_EDITOR",
                        $"Test play: '{modId}/{relativePath}' is read-only, playing from the editor buffer",
                        LogLevel.Warning);
                }
            }
            catch (Exception ex)
            {
                _statusLabel.Text = string.Format(
                    TranslationServer.Translate("tools/scenario_editor/save_error"), ex.Message);
                return;
            }

            _editLayout.Visible = false;
            _playLayout.Visible = true;

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
            sceneService?.LoadScene("res://scenes/ui/base_scene.tscn");

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
            _playLayout.Visible = false;
            _editLayout.Visible = true;
            Visible = true;

            if (_stopOverlay != null)
            {
                _stopOverlay.QueueFree();
                _stopOverlay = null;
            }

            var sceneService = GetNodeOrNull<GodotSceneService>("/root/GodotSceneService");
            sceneService?.SwitchToMenu();
        }

        private CanvasLayer CreateStopOverlay()
        {
            var layer = new CanvasLayer { Layer = 128 };

            var btn = new Button
            {
                Text = TranslationServer.Translate("tools/scenario_editor/stop"),
                CustomMinimumSize = new Vector2(120, 40)
            };
            btn.Pressed += OnStopPressed;

            var margin = new MarginContainer();
            margin.AddThemeConstantOverride("margin_top", 8);
            margin.AddThemeConstantOverride("margin_left", 8);
            margin.AddChild(btn);

            layer.AddChild(margin);
            return layer;
        }
    }
}
