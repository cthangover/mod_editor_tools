using System;
using System.Collections.Generic;
using System.Linq;
using Cthangover.Core.UI.Theming;
using Cthangover.Core.Utils;
using Godot;

namespace Cthangover.Core.UI.Tool.SceneBuilder
{
    /// <summary>
    /// Developer tool window for interactively previewing a mod <c>.tscn</c> scene
    /// layered on top of a base game scene. Features three chained selectors
    /// (base scene → mod → scene-in-mod), a live viewport with a selectable node
    /// hierarchy tree, draggable resize/anchor handles over the preview, and one
    /// compact tabbed sidebar holding the property inspector and the tscn source
    /// editor (edits applied live, debounced auto-save), plus an always-visible
    /// output strip for status and errors. All logic lives in
    /// <see cref="SceneBuilderController"/>.
    /// </summary>
    public partial class SceneBuilderWindow : ToolWindow
    {
        private SceneBuilderController _controller;

        private OptionButton _baseDropdown;
        private OptionButton _modDropdown;
        private OptionButton _sceneDropdown;
        private Tree _hierarchyTree;
        private SubViewport _viewport;
        private TabContainer _sideTabs;
        private InspectorPanel _inspector;
        private SelectionOverlay _selectionOverlay;
        private Timer _autosaveTimer;
        private Node _selectedNode;
        private TscnFileProps _fileProps = TscnFileProps.Build(null);
        private TextEdit _tscnEditor;
        private Button _reloadBtn;
        private Button _saveBtn;
        private Button _refreshBtn;
        private OptionButton _newNodeTypeDropdown;
        private Button _addNodeBtn;
        private Button _deleteNodeBtn;
        private RichTextLabel _outputLabel;

        /// <summary>Curated palette for node creation — common Control classes the runtime builder instantiates via ClassDB.</summary>
        private static readonly string[] CreatableTypes =
        {
            "Control", "Label", "Button", "Panel", "ColorRect", "TextureRect",
            "RichTextLabel", "LineEdit", "VBoxContainer", "HBoxContainer",
            "GridContainer", "MarginContainer", "CenterContainer", "ScrollContainer"
        };

        private List<(string Display, string Path)> _baseScenes;
        private List<(string ModId, string Name, string RelativePath)> _modScenes;
        private List<string> _mods;
        private List<(string ModId, string Name, string RelativePath)> _currentModScenes = new();

        /// <summary>Constructs the window, creates the controller, builds UI, and populates selectors.</summary>
        public SceneBuilderWindow() : base("tools/scene_builder/title")
        {
            CthangoverTheme.Apply(this);
            _controller = new SceneBuilderController();
            BuildUI();
            PopulateBaseList();
            PopulateModList();
            RebuildPreview();
        }

        private void BuildUI()
        {
            var outerVBox = CreateFillContainer();
            AddChild(outerVBox);

            var toolbar = CreateToolbar();
            outerVBox.AddChild(toolbar);

            toolbar.AddChild(new Label { Text = TranslationServer.Translate("tools/scene_builder/base_scene") });

            _baseDropdown = new OptionButton();
            _baseDropdown.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _baseDropdown.ItemSelected += OnBaseSelected;
            toolbar.AddChild(_baseDropdown);

            toolbar.AddChild(new Label { Text = TranslationServer.Translate("tools/scene_builder/mod") });

            _modDropdown = new OptionButton();
            _modDropdown.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _modDropdown.ItemSelected += OnModSelected;
            toolbar.AddChild(_modDropdown);

            toolbar.AddChild(new Label { Text = TranslationServer.Translate("tools/scene_builder/scene") });

            _sceneDropdown = new OptionButton();
            _sceneDropdown.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _sceneDropdown.ItemSelected += OnSceneSelected;
            toolbar.AddChild(_sceneDropdown);

            _reloadBtn = new Button { Text = TranslationServer.Translate("tools/scene_builder/reload") };
            _reloadBtn.Pressed += OnReloadPressed;
            toolbar.AddChild(_reloadBtn);

            var mainHBox = new HBoxContainer();
            mainHBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            mainHBox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            outerVBox.AddChild(mainHBox);

            var leftVBox = new VBoxContainer();
            leftVBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            leftVBox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            var leftPanel = CreateSidebar(leftVBox);
            mainHBox.AddChild(leftPanel);

            var hierarchyHeader = new HBoxContainer();
            var hierarchyLabel = new Label { Text = TranslationServer.Translate("tools/scene_builder/hierarchy") };
            hierarchyLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hierarchyHeader.AddChild(hierarchyLabel);

            _newNodeTypeDropdown = new OptionButton();
            _newNodeTypeDropdown.TooltipText = TranslationServer.Translate("tools/scene_builder/add_node_tip");
            foreach (var t in CreatableTypes)
                _newNodeTypeDropdown.AddItem(t);
            _newNodeTypeDropdown.Selected = 0;
            hierarchyHeader.AddChild(_newNodeTypeDropdown);

            _addNodeBtn = new Button { Text = "+" };
            _addNodeBtn.TooltipText = TranslationServer.Translate("tools/scene_builder/add_node_tip");
            _addNodeBtn.Pressed += OnAddNodePressed;
            hierarchyHeader.AddChild(_addNodeBtn);

            _deleteNodeBtn = new Button { Text = "-" };
            _deleteNodeBtn.TooltipText = TranslationServer.Translate("tools/scene_builder/delete_node_tip");
            _deleteNodeBtn.Pressed += OnDeleteNodePressed;
            hierarchyHeader.AddChild(_deleteNodeBtn);

            _refreshBtn = new Button();
            _refreshBtn.Text = TranslationServer.Translate("tools/scene_builder/refresh");
            _refreshBtn.Pressed += OnRefreshPressed;
            hierarchyHeader.AddChild(_refreshBtn);

            leftVBox.AddChild(hierarchyHeader);

            _hierarchyTree = new Tree();
            _hierarchyTree.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            _hierarchyTree.HideRoot = true;
            _hierarchyTree.CellSelected += OnTreeCellSelected;
            leftVBox.AddChild(_hierarchyTree);

            var previewContainer = new PanelContainer();
            previewContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            previewContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            mainHBox.AddChild(previewContainer);

            _viewport = new SubViewport();
            _viewport.Size = new Vector2I(1920, 1080);

            var clickCatcher = new Control();
            clickCatcher.MouseFilter = Control.MouseFilterEnum.Stop;
            clickCatcher.ZIndex = 4095;
            clickCatcher.AnchorRight = 1f;
            clickCatcher.AnchorBottom = 1f;
            clickCatcher.GuiInput += OnViewportClick;
            _viewport.AddChild(clickCatcher);

            var viewportContainer = new SubViewportContainer();
            viewportContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            viewportContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            viewportContainer.Stretch = true;
            viewportContainer.StretchShrink = 1;
            viewportContainer.AddChild(_viewport);
            previewContainer.AddChild(viewportContainer);

            _selectionOverlay = new SelectionOverlay();
            previewContainer.AddChild(_selectionOverlay);
            _selectionOverlay.Setup(_viewport, viewportContainer);
            _selectionOverlay.PickRequested += OnOverlayPick;
            _selectionOverlay.BatchCommitted += OnOverlayBatchCommitted;
            _selectionOverlay.EnsureConstructed();

            _autosaveTimer = new Timer { OneShot = true, WaitTime = 0.4 };
            _autosaveTimer.Timeout += OnAutosaveTimeout;
            AddChild(_autosaveTimer);

            // Compact tabbed sidebar: inspector and tscn source share one column,
            // the output strip stays below the tabs so errors are always visible.
            var rightVBox = new VBoxContainer();
            rightVBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            rightVBox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            var rightPanel = CreateSidebar(rightVBox, 360);
            mainHBox.AddChild(rightPanel);

            _sideTabs = new TabContainer();
            _sideTabs.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _sideTabs.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            rightVBox.AddChild(_sideTabs);

            var inspectorTab = new VBoxContainer
            {
                Name = TranslationServer.Translate("tools/scene_builder/tab_inspector"),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            _sideTabs.AddChild(inspectorTab);

            _inspector = new InspectorPanel();
            inspectorTab.AddChild(_inspector);
            _inspector.PropertyEdited += OnInspectorPropertyEdited;
            _inspector.EnsureConstructed();
            _inspector.ShowMessage(TranslationServer.Translate("tools/scene_builder/inspector_select"));

            var tscnTab = new VBoxContainer
            {
                Name = TranslationServer.Translate("tools/scene_builder/tab_tscn"),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            _sideTabs.AddChild(tscnTab);

            _tscnEditor = new TextEdit();
            _tscnEditor.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _tscnEditor.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            _tscnEditor.WrapMode = TextEdit.LineWrappingMode.None;
            _tscnEditor.AddThemeFontOverride("font", GetMonospaceFont());
            _tscnEditor.TextChanged += () => SetDirty();
            tscnTab.AddChild(_tscnEditor);

            _saveBtn = new Button();
            _saveBtn.Text = TranslationServer.Translate("tools/scene_builder/save");
            _saveBtn.Pressed += OnSavePressed;
            tscnTab.AddChild(_saveBtn);

            var outputHeader = new HBoxContainer();
            var outputLabel = new Label { Text = TranslationServer.Translate("tools/scene_builder/output") };
            outputLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            outputHeader.AddChild(outputLabel);

            var clearOutputBtn = new Button { Text = TranslationServer.Translate("tools/scene_builder/clear") };
            clearOutputBtn.Pressed += () => _outputLabel.Text = "";
            outputHeader.AddChild(clearOutputBtn);

            rightVBox.AddChild(outputHeader);

            _outputLabel = new RichTextLabel();
            _outputLabel.BbcodeEnabled = true;
            _outputLabel.FitContent = false;
            _outputLabel.ScrollActive = true;
            _outputLabel.ScrollFollowing = true;
            _outputLabel.CustomMinimumSize = new Vector2(0, 70);
            rightVBox.AddChild(_outputLabel);
        }

        private void PopulateBaseList()
        {
            _baseDropdown.Clear();
            _baseDropdown.AddItem(TranslationServer.Translate("tools/scene_builder/empty_scene"));
            _baseScenes = _controller.GetBaseSceneList();
            foreach (var s in _baseScenes)
                _baseDropdown.AddItem(s.Display);
            _baseDropdown.Selected = 0;
        }

        private void PopulateModList()
        {
            _modDropdown.Clear();
            _modDropdown.AddItem(TranslationServer.Translate("tools/scene_builder/select_mod"));
            _modScenes = _controller.GetModSceneIndex();
            _mods = _modScenes.Select(s => s.ModId).Distinct().ToList();
            foreach (var modId in _mods)
                _modDropdown.AddItem(modId);
            _modDropdown.Selected = 0;

            _sceneDropdown.Clear();
            _sceneDropdown.AddItem(TranslationServer.Translate("tools/scene_builder/select_scene"));
            _sceneDropdown.Selected = 0;
        }

        private void PopulateSceneListForMod(string modId)
        {
            _currentModScenes = new List<(string, string, string)>();
            if (!string.IsNullOrEmpty(modId))
            {
                foreach (var s in _modScenes)
                    if (s.ModId == modId)
                        _currentModScenes.Add(s);
            }

            _sceneDropdown.Clear();
            _sceneDropdown.AddItem(TranslationServer.Translate("tools/scene_builder/select_scene"));
            foreach (var s in _currentModScenes)
                _sceneDropdown.AddItem(s.Name);
            _sceneDropdown.Selected = 0;
        }

        private bool TryGetSelectedModScene(out (string ModId, string Name, string RelativePath) scene)
        {
            scene = default;
            var idx = (int)_sceneDropdown.Selected - 1;
            if (idx < 0 || idx >= _currentModScenes.Count)
                return false;

            scene = _currentModScenes[idx];
            return true;
        }

        private void RebuildPreview()
        {
            var baseIdx = (int)_baseDropdown.Selected;
            if (baseIdx <= 0 || baseIdx - 1 >= _baseScenes.Count)
                _controller.LoadEmptyScene(_viewport);
            else
                _controller.LoadBaseScene(_baseScenes[baseIdx - 1].Path, _viewport);

            if (TryGetSelectedModScene(out var scene))
                _controller.LoadModScene(scene.RelativePath);

            _controller.BuildHierarchy(_hierarchyTree);
            ShowTscnText();
            RefreshFileSnapshot();
            _selectedNode = null;
            _selectionOverlay.Track(null);
            _inspector.ShowMessage(TranslationServer.Translate("tools/scene_builder/inspector_select"));
        }

        /// <summary>Re-parses the current editor text into the file-truth property snapshot used by the inspector.</summary>
        private void RefreshFileSnapshot()
        {
            _fileProps = TscnFileProps.Build(_tscnEditor.Text);
        }

        /// <summary>Restarts the debounced auto-save; called after every inspector/overlay write-back.</summary>
        private void KickAutosave()
        {
            _autosaveTimer.Stop();
            _autosaveTimer.Start();
        }

        private void OnAutosaveTimeout()
        {
            SaveCurrentText(true);
        }

        private void ShowTscnText()
        {
            _tscnEditor.Text = "";
            if (TryGetSelectedModScene(out var scene))
            {
                var text = _controller.ReadSceneText(scene.ModId, scene.RelativePath);
                _tscnEditor.Text = text ?? "";
            }
        }

        private void OnBaseSelected(long index)
        {
            RebuildPreview();
        }

        private void OnModSelected(long index)
        {
            var modId = index == 0 ? null : _mods[(int)index - 1];
            PopulateSceneListForMod(modId);
            RebuildPreview();
        }

        private void OnSceneSelected(long index)
        {
            RebuildPreview();
        }

        private void OnReloadPressed()
        {
            if (TryGetSelectedModScene(out var scene))
                _controller.InvalidateScene(scene.RelativePath);

            RebuildPreview();
            ShowOutput(TranslationServer.Translate("tools/scene_builder/reloaded"), false);
        }

        private void OnSavePressed()
        {
            SaveCurrentText(false);
        }

        /// <summary>
        /// Writes the editor text to disk. auto=true marks the debounced
        /// inspector-driven save: failures are logged and reported, but the
        /// wording differs from the explicit Save button feedback.
        /// </summary>
        private bool SaveCurrentText(bool auto)
        {
            if (!TryGetSelectedModScene(out var scene))
            {
                if (!auto)
                    ShowOutput(TranslationServer.Translate("tools/scene_builder/error_no_scene"), true);
                return false;
            }

            if (_controller.SaveSceneText(scene.ModId, scene.RelativePath, _tscnEditor.Text))
            {
                MarkClean();
                ShowOutput(TranslationServer.Translate(auto
                    ? "tools/scene_builder/autosaved"
                    : "tools/scene_builder/saved_ok"), false);
                return true;
            }

            ShowOutput(TranslationServer.Translate("tools/scene_builder/error_save"), true);
            GameLogger.Log("SCENE_BUILDER",
                $"save failed: mod='{scene.ModId}' path='{scene.RelativePath}' (zip mods are read-only)",
                LogLevel.Error);
            return false;
        }

        private void OnRefreshPressed()
        {
            _controller.BuildHierarchy(_hierarchyTree);
        }

        private void OnTreeCellSelected()
        {
            var item = _hierarchyTree.GetSelected();
            if (item == null)
                return;

            _controller.SelectNode(item);
            SyncInspector(item);
        }

        private void OnViewportClick(InputEvent evt)
        {
            if (evt is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
            {
                var node = _controller.FindNodeAtPosition(mb.Position);
                if (node == null)
                    return;

                var item = _controller.GetItemForNode(node);
                if (item == null)
                    return;

                var current = _hierarchyTree.GetSelected();
                if (current != null)
                    current.Deselect(0);

                item.Select(0);
                _hierarchyTree.ScrollToItem(item);
                _controller.SelectNode(item);
                SyncInspector(item);
            }
        }

        /// <summary>
        /// Mirrors the hierarchy selection into the inspector and the overlay:
        /// mod-scene nodes get editable rows and manipulation handles, everything
        /// else gets a read-only notice.
        /// </summary>
        private void SyncInspector(TreeItem item)
        {
            var node = _controller.GetNodeForItem(item);
            _selectedNode = node;

            if (node != null && _controller.IsEditableModNode(node))
            {
                var nodePath = _controller.GetModNodePath(node);
                _inspector.ShowNode(node, _fileProps.GetNodeProperties(nodePath));
                _selectionOverlay.Track(node as Control);
                _sideTabs.CurrentTab = 0;
            }
            else
            {
                _inspector.ShowMessage(TranslationServer.Translate("tools/scene_builder/inspector_readonly"));
                _selectionOverlay.Track(null);
            }
        }

        private void OnOverlayPick(Vector2 canvasPosition)
        {
            var node = _controller.FindNodeAtPosition(canvasPosition);
            if (node == null)
                return;

            var item = _controller.GetItemForNode(node);
            if (item == null)
                return;

            SelectTreeItem(item);
        }

        /// <summary>Moves tree selection to the item and mirrors it into highlight/inspector/overlay.</summary>
        private void SelectTreeItem(TreeItem item)
        {
            var current = _hierarchyTree.GetSelected();
            if (current != null)
                current.Deselect(0);

            item.Select(0);
            _hierarchyTree.ScrollToItem(item);
            _controller.SelectNode(item);
            SyncInspector(item);
        }

        private void OnAddNodePressed()
        {
            if (!TryGetSelectedModScene(out var scene))
            {
                ShowOutput(TranslationServer.Translate("tools/scene_builder/error_no_scene"), true);
                return;
            }

            var typeName = CreatableTypes[System.Math.Max(_newNodeTypeDropdown.Selected, 0)];
            var parent = ResolveAddParent();
            if (parent == null)
            {
                ShowOutput(TranslationServer.Translate("tools/scene_builder/error_no_scene"), true);
                return;
            }

            var created = _controller.CreateUniqueChild(parent, typeName);
            if (created == null)
            {
                ShowOutput(TranslationServer.Translate("tools/scene_builder/err_add_failed"), true);
                GameLogger.Log("SCENE_BUILDER", $"ClassDB.Instantiate failed for '{typeName}'", LogLevel.Error);
                return;
            }

            var props = BuildInitialNodeProps(created, parent);
            var parentPath = _controller.GetModNodePath(parent);
            if (!TscnTextSurgery.TryInsertNodeSection(_tscnEditor.Text, parentPath, created.Name, typeName, props, out var updated))
            {
                created.GetParent().RemoveChild(created);
                created.QueueFree();
                ShowOutput(TranslationServer.Translate("tools/scene_builder/err_add_parent_missing"), true);
                GameLogger.Log("SCENE_BUILDER",
                    $"node insert rejected: parent section '{parentPath}' absent from file", LogLevel.Warning);
                return;
            }

            _tscnEditor.Text = updated;
            RefreshFileSnapshot();
            KickAutosave();
            _controller.BuildHierarchy(_hierarchyTree);

            var item = _controller.GetItemForNode(created);
            if (item != null)
                SelectTreeItem(item);
        }

        /// <summary>Selected editable mod node, falling back to the mod scene root.</summary>
        private Node ResolveAddParent()
        {
            if (_selectedNode != null && _controller.IsEditableModNode(_selectedNode))
                return _selectedNode;
            return _controller.GetModRoot();
        }

        /// <summary>
        /// Minimal viable layout written with a fresh node: container children get
        /// container mode; free Controls get an anchors-mode box of visible size so
        /// a new node is immediately grabbable by the overlay handles.
        /// </summary>
        private static List<KeyValuePair<string, string>> BuildInitialNodeProps(Node created, Node parent)
        {
            var props = new List<KeyValuePair<string, string>>();
            if (created is Control)
            {
                if (parent is Container)
                    props.Add(new KeyValuePair<string, string>("layout_mode", "2"));
                else
                {
                    props.Add(new KeyValuePair<string, string>("layout_mode", "1"));
                    props.Add(new KeyValuePair<string, string>("offset_right", "120.0"));
                    props.Add(new KeyValuePair<string, string>("offset_bottom", "40.0"));
                }
            }
            return props;
        }

        private void OnDeleteNodePressed()
        {
            var node = _selectedNode;
            if (node == null || !_controller.IsEditableModNode(node))
            {
                ShowOutput(TranslationServer.Translate("tools/scene_builder/err_del_none"), true);
                return;
            }
            if (node == _controller.GetModRoot())
            {
                ShowOutput(TranslationServer.Translate("tools/scene_builder/err_del_root"), true);
                return;
            }

            var path = _controller.GetModNodePath(node);
            if (!_controller.DeleteModNode(node))
            {
                ShowOutput(TranslationServer.Translate("tools/scene_builder/err_del_none"), true);
                return;
            }

            if (TscnTextSurgery.TryRemoveNodeSubtree(_tscnEditor.Text, path, out var updated))
            {
                _tscnEditor.Text = updated;
                RefreshFileSnapshot();
                KickAutosave();
            }
            else
            {
                ShowOutput(TranslationServer.Translate("tools/scene_builder/inspector_missing_section"), true);
                GameLogger.Log("SCENE_BUILDER",
                    $"subtree remove failed: section '{path}' absent from file", LogLevel.Warning);
            }

            _selectedNode = null;
            _selectionOverlay.Track(null);
            _inspector.ShowMessage(TranslationServer.Translate("tools/scene_builder/inspector_select"));
            _controller.BuildHierarchy(_hierarchyTree);
        }

        /// <summary>
        /// Applies one overlay drag result: patches every key of the batch into
        /// the tscn text, refreshes the file snapshot, inspector rows and handles,
        /// then arms the auto-save.
        /// </summary>
        private void OnOverlayBatchCommitted(Node node, List<KeyValuePair<string, Variant>> batch)
        {
            if (!TryGetSelectedModScene(out var scene))
            {
                ShowOutput(TranslationServer.Translate("tools/scene_builder/error_no_scene"), true);
                return;
            }

            var nodePath = _controller.GetModNodePath(node);
            if (nodePath == null)
            {
                ShowOutput(TranslationServer.Translate("tools/scene_builder/inspector_readonly"), true);
                GameLogger.Log("SCENE_BUILDER", $"batch rejected: '{node.Name}' is not an editable mod node", LogLevel.Warning);
                return;
            }

            var text = _tscnEditor.Text;
            foreach (var pair in batch)
            {
                var serialized = TscnValueFormatter.Format(pair.Value);
                if (serialized == null)
                {
                    ShowOutput(TranslationServer.Translate("tools/scene_builder/inspector_bad_value"), true);
                    GameLogger.Log("SCENE_BUILDER", $"unserializable value for '{pair.Key}'", LogLevel.Warning);
                    continue;
                }

                if (!TscnTextSurgery.TryUpsertProperty(text, nodePath, pair.Key, serialized, out text))
                {
                    ShowOutput(TranslationServer.Translate("tools/scene_builder/inspector_missing_section"), true);
                    GameLogger.Log("SCENE_BUILDER",
                        $"section not found: path='{nodePath}' key='{pair.Key}'", LogLevel.Warning);
                }
            }

            _tscnEditor.Text = text;
            RefreshFileSnapshot();
            RebuildInspectorFor(node);
            _selectionOverlay.Refresh();
            KickAutosave();
        }

        /// <summary>
        /// Write-back pipeline for one inspector edit: serialize the Variant into
        /// tscn syntax and patch the corresponding property line of the node's
        /// section in the source editor. Every failure is reported loudly —
        /// silent drops previously made edits look like they "did nothing".
        /// </summary>
        private void OnInspectorPropertyEdited(Node node, string key, Variant value)
        {
            if (!TryGetSelectedModScene(out var scene))
            {
                ShowOutput(TranslationServer.Translate("tools/scene_builder/error_no_scene"), true);
                return;
            }

            var serialized = TscnValueFormatter.Format(value);
            if (serialized == null)
            {
                ShowOutput(TranslationServer.Translate("tools/scene_builder/inspector_bad_value"), true);
                GameLogger.Log("SCENE_BUILDER", $"unserializable value for '{key}'", LogLevel.Warning);
                return;
            }

            var nodePath = _controller.GetModNodePath(node);
            if (nodePath == null)
            {
                ShowOutput(TranslationServer.Translate("tools/scene_builder/inspector_readonly"), true);
                GameLogger.Log("SCENE_BUILDER", $"edit rejected: '{node.Name}' is not an editable mod node", LogLevel.Warning);
                return;
            }

            if (!TscnTextSurgery.TryUpsertProperty(_tscnEditor.Text, nodePath, key, serialized, out var updated))
            {
                ShowOutput(TranslationServer.Translate("tools/scene_builder/inspector_missing_section"), true);
                GameLogger.Log("SCENE_BUILDER",
                    $"section not found: path='{nodePath}' key='{key}'", LogLevel.Warning);
                return;
            }

            _tscnEditor.Text = updated;
            RefreshFileSnapshot();
            _selectionOverlay.Refresh();
            KickAutosave();
        }

        /// <summary>Full inspector resync after batch writes (drag finished, focus loss is fine).</summary>
        private void RebuildInspectorFor(Node node)
        {
            if (_selectedNode != node || !_controller.IsEditableModNode(node))
                return;

            var nodePath = _controller.GetModNodePath(node);
            _inspector.ShowNode(node, _fileProps.GetNodeProperties(nodePath));
        }

        private void ShowOutput(string text, bool isError)
        {
            if (_outputLabel == null)
                return;

            var color = isError ? "red" : "green";
            _outputLabel.Text = $"[color={color}]{text}[/color]";
        }
    }
}
