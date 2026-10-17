using System;
using System.Collections.Generic;
using System.Linq;
using Cthangover.Core.UI.Theming;
using Cthangover.Core.Utils;
using Godot;

using Cthangover.Mods.Interface.Theming;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Tools.Common;
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
        private CodeEdit _tscnEditor;
        private OptionButton _newNodeTypeDropdown;
        private int _shownBase;
        private int _shownMod;
        private int _shownScene;
        private bool _loadingText;

        /// <summary>Logical game resolution (project.godot) the preview canvas is laid out at.</summary>
        private static readonly Vector2I GameResolution = new(1920, 1024);

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
            SetHint(ToolUi.Tr("tools/scene_builder/hint"));
        }

        private void BuildUI()
        {
            var outerVBox = CreateFillContainer();
            AddChild(outerVBox);

            var toolbar = CreateToolbar();
            outerVBox.AddChild(toolbar);

            _baseDropdown = Dropdown(toolbar, "tools/scene_builder/base_scene", OnBaseSelected);
            _modDropdown = Dropdown(toolbar, "tools/scene_builder/mod", OnModSelected);
            _sceneDropdown = Dropdown(toolbar, "tools/scene_builder/scene", OnSceneSelected);
            toolbar.AddChild(ToolUi.Divider());
            toolbar.AddChild(ToolUi.Button("tools/common/save", OnSavePressed, ButtonKind.Primary, "tools/scene_builder/save_tip"));
            toolbar.AddChild(ToolUi.Button("tools/scene_builder/reload", OnReloadPressed, ButtonKind.Secondary, "tools/scene_builder/reload_tip"));

            var mainSplit = new HSplitContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            outerVBox.AddChild(mainSplit);

            mainSplit.AddChild(BuildHierarchyPanel());

            var centerSplit = new HSplitContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            mainSplit.AddChild(centerSplit);
            centerSplit.AddChild(BuildPreview());
            centerSplit.AddChild(BuildSidePanel());

            _autosaveTimer = new Timer { OneShot = true, WaitTime = 0.4 };
            _autosaveTimer.Timeout += OnAutosaveTimeout;
            AddChild(_autosaveTimer);
        }

        private static OptionButton Dropdown(HBoxContainer toolbar, string tooltipKey, Action<long> selected)
        {
            var dropdown = new OptionButton
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TooltipText = ToolUi.Tr(tooltipKey),
                FitToLongestItem = false
            };
            dropdown.ItemSelected += index => selected(index);
            toolbar.AddChild(dropdown);
            return dropdown;
        }

        private Control BuildHierarchyPanel()
        {
            var column = new VBoxContainer();
            column.AddThemeConstantOverride("separation", UiTokens.Space2);
            var panel = new PanelContainer { CustomMinimumSize = new Vector2(280, 0) };
            panel.AddThemeStyleboxOverride("panel", SidebarBox());
            panel.AddChild(column);

            column.AddChild(ToolUi.Section("tools/scene_builder/hierarchy"));

            var actions = new HBoxContainer();
            actions.AddThemeConstantOverride("separation", UiTokens.Space1);
            column.AddChild(actions);

            _newNodeTypeDropdown = new OptionButton
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TooltipText = ToolUi.Tr("tools/scene_builder/node_type_tip")
            };
            foreach (var t in CreatableTypes)
                _newNodeTypeDropdown.AddItem(t);
            _newNodeTypeDropdown.Selected = 0;
            actions.AddChild(_newNodeTypeDropdown);

            var addBtn = UiKit.IconButton("+", ToolUi.Tr("tools/scene_builder/add_node_tip"));
            addBtn.Pressed += OnAddNodePressed;
            actions.AddChild(addBtn);

            var deleteBtn = UiKit.IconButton("\u2212", ToolUi.Tr("tools/scene_builder/delete_node_tip"));
            deleteBtn.Pressed += OnDeleteNodePressed;
            actions.AddChild(deleteBtn);

            var refreshBtn = UiKit.IconButton("\u27F3", ToolUi.Tr("tools/scene_builder/refresh_tip"));
            refreshBtn.Pressed += OnRefreshPressed;
            actions.AddChild(refreshBtn);

            _hierarchyTree = new Tree
            {
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                HideRoot = true
            };
            _hierarchyTree.CellSelected += OnTreeCellSelected;
            column.AddChild(_hierarchyTree);
            column.AddChild(ToolUi.Hint(ToolUi.Tr("tools/scene_builder/hierarchy_hint")));
            return panel;
        }

        /// <summary>
        /// Preview canvas. The sub-viewport lays the scene out at the game
        /// resolution (2D size override) and is scaled to fit inside an
        /// aspect-locked box, so anchors and sizes look exactly as in the game
        /// whatever the window size. The selection overlay shares the box, so
        /// its coordinate mapping is a plain scale.
        /// </summary>
        private Control BuildPreview()
        {
            var previewContainer = new PanelContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            previewContainer.AddThemeStyleboxOverride("panel", UiKit.Frame(UiTokens.RadiusMd, UiTokens.Well, UiTokens.Line));

            var aspect = new AspectRatioContainer
            {
                Ratio = (float)GameResolution.X / GameResolution.Y,
                StretchMode = AspectRatioContainer.StretchModeEnum.Fit
            };
            previewContainer.AddChild(aspect);

            _viewport = new SubViewport
            {
                Size = GameResolution,
                Size2DOverride = GameResolution,
                Size2DOverrideStretch = true
            };

            var clickCatcher = new Control();
            clickCatcher.MouseFilter = Control.MouseFilterEnum.Stop;
            clickCatcher.ZIndex = 4095;
            clickCatcher.AnchorRight = 1f;
            clickCatcher.AnchorBottom = 1f;
            clickCatcher.GuiInput += OnViewportClick;
            _viewport.AddChild(clickCatcher);

            var viewportContainer = new SubViewportContainer { Stretch = true };
            viewportContainer.AddChild(_viewport);
            aspect.AddChild(viewportContainer);

            _selectionOverlay = new SelectionOverlay();
            aspect.AddChild(_selectionOverlay);
            _selectionOverlay.Setup(_viewport, viewportContainer, GameResolution);
            _selectionOverlay.PickRequested += OnOverlayPick;
            _selectionOverlay.BatchCommitted += OnOverlayBatchCommitted;
            _selectionOverlay.EnsureConstructed();

            return previewContainer;
        }

        /// <summary>Inspector and tscn source share one tabbed column.</summary>
        private Control BuildSidePanel()
        {
            var panel = new PanelContainer { CustomMinimumSize = new Vector2(380, 0) };
            panel.AddThemeStyleboxOverride("panel", SidebarBox());

            _sideTabs = new TabContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            panel.AddChild(_sideTabs);

            var inspectorScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            _sideTabs.AddChild(inspectorScroll);
            _sideTabs.SetTabTitle(0, ToolUi.Tr("tools/scene_builder/tab_inspector"));

            _inspector = new InspectorPanel { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            inspectorScroll.AddChild(_inspector);
            _inspector.PropertyEdited += OnInspectorPropertyEdited;
            _inspector.EnsureConstructed();
            _inspector.ShowMessage(ToolUi.Tr("tools/scene_builder/inspector_select"));

            // Titles are set explicitly: a node name cannot hold "." (".tscn").
            var tscnTab = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            _sideTabs.AddChild(tscnTab);
            _sideTabs.SetTabTitle(1, ToolUi.Tr("tools/scene_builder/tab_tscn"));

            _tscnEditor = ToolUi.Code();
            _tscnEditor.TextChanged += OnTscnTextChanged;
            tscnTab.AddChild(_tscnEditor);
            tscnTab.AddChild(ToolUi.Hint(ToolUi.Tr("tools/scene_builder/tscn_hint")));

            return panel;
        }

        private void PopulateBaseList()
        {
            _baseDropdown.Clear();
            _baseDropdown.AddItem(ToolUi.Tr("tools/scene_builder/empty_scene"));
            _baseScenes = _controller.GetBaseSceneList();
            foreach (var s in _baseScenes)
                _baseDropdown.AddItem(s.Display);
            _baseDropdown.Selected = 0;
        }

        private void PopulateModList()
        {
            _modDropdown.Clear();
            _modDropdown.AddItem(ToolUi.Tr("tools/scene_builder/select_mod"));
            _modScenes = _controller.GetModSceneIndex();
            _mods = _modScenes.Select(s => s.ModId).Distinct().ToList();
            foreach (var modId in _mods)
                _modDropdown.AddItem(modId);
            _modDropdown.Selected = 0;

            _sceneDropdown.Clear();
            _sceneDropdown.AddItem(ToolUi.Tr("tools/scene_builder/select_scene"));
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
            _sceneDropdown.AddItem(ToolUi.Tr("tools/scene_builder/select_scene"));
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
            _shownBase = _baseDropdown.Selected;
            _shownMod = _modDropdown.Selected;
            _shownScene = _sceneDropdown.Selected;

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
            _inspector.ShowMessage(ToolUi.Tr("tools/scene_builder/inspector_select"));
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
            _loadingText = true;
            _tscnEditor.Text = "";
            if (TryGetSelectedModScene(out var scene))
            {
                var text = _controller.ReadSceneText(scene.ModId, scene.RelativePath);
                _tscnEditor.Text = text ?? "";
            }
            _tscnEditor.ClearUndoHistory();
            _loadingText = false;
            MarkClean();
        }

        /// <summary>Hand edits of the source mark the window dirty; they reach the preview on save.</summary>
        private void OnTscnTextChanged()
        {
            if (!_loadingText)
                SetDirty();
        }

        private void OnBaseSelected(long index)
        {
            RunDiscardingChanges(RebuildPreview, RestoreDropdowns);
        }

        private void OnModSelected(long index)
        {
            RunDiscardingChanges(() =>
            {
                var modId = index == 0 ? null : _mods[(int)index - 1];
                PopulateSceneListForMod(modId);
                RebuildPreview();
            }, RestoreDropdowns);
        }

        private void OnSceneSelected(long index)
        {
            RunDiscardingChanges(RebuildPreview, RestoreDropdowns);
        }

        /// <summary>Rolls the selectors back to the scene still shown after a refused switch.</summary>
        private void RestoreDropdowns()
        {
            _baseDropdown.Select(_shownBase);
            _modDropdown.Select(_shownMod);
            PopulateSceneListForMod(_shownMod <= 0 ? null : _mods[_shownMod - 1]);
            _sceneDropdown.Select(_shownScene);
        }

        private void OnReloadPressed()
        {
            RunDiscardingChanges(() =>
            {
                if (TryGetSelectedModScene(out var scene))
                    _controller.InvalidateScene(scene.RelativePath);

                RebuildPreview();
                ShowOutput(ToolUi.Tr("tools/scene_builder/reloaded"), false);
            });
        }

        /// <summary>Ctrl+S: save the source and rebuild the preview from it.</summary>
        protected override void OnSaveShortcut() => OnSavePressed();

        /// <summary>Delete removes the selected mod node after a confirmation.</summary>
        protected override bool HandleKey(InputEventKey key)
        {
            if (key.Keycode != Key.Delete || _selectedNode == null || !_controller.IsEditableModNode(_selectedNode))
                return false;

            ShowConfirm("tools/scene_builder/delete_node_title", "tools/scene_builder/delete_node_confirm", OnDeleteNodePressed);
            return true;
        }

        /// <summary>
        /// Saves the source and, when it was edited by hand, rebuilds the preview
        /// from the saved file — hand edits do not reach the live preview
        /// otherwise (inspector and overlay edits are applied live already).
        /// </summary>
        private void OnSavePressed()
        {
            var handEdited = _dirty;
            if (!SaveCurrentText(false) || !handEdited || !TryGetSelectedModScene(out var scene))
                return;

            _controller.InvalidateScene(scene.RelativePath);
            RebuildPreview();
            ShowOutput(ToolUi.Tr("tools/scene_builder/saved_rebuilt"), false);
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
                    ShowOutput(ToolUi.Tr("tools/scene_builder/error_no_scene"), true);
                return false;
            }

            if (_controller.SaveSceneText(scene.ModId, scene.RelativePath, _tscnEditor.Text))
            {
                MarkClean();
                ShowOutput(ToolUi.Tr(auto
                    ? "tools/scene_builder/autosaved"
                    : "tools/scene_builder/saved_ok"), false);
                return true;
            }

            ShowOutput(ToolUi.Tr("tools/scene_builder/error_save"), true);
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
                _inspector.ShowMessage(ToolUi.Tr("tools/scene_builder/inspector_readonly"));
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
                ShowOutput(ToolUi.Tr("tools/scene_builder/error_no_scene"), true);
                return;
            }

            var typeName = CreatableTypes[System.Math.Max(_newNodeTypeDropdown.Selected, 0)];
            var parent = ResolveAddParent();
            if (parent == null)
            {
                ShowOutput(ToolUi.Tr("tools/scene_builder/error_no_scene"), true);
                return;
            }

            var created = _controller.CreateUniqueChild(parent, typeName);
            if (created == null)
            {
                ShowOutput(ToolUi.Tr("tools/scene_builder/err_add_failed"), true);
                GameLogger.Log("SCENE_BUILDER", $"ClassDB.Instantiate failed for '{typeName}'", LogLevel.Error);
                return;
            }

            var props = BuildInitialNodeProps(created, parent);
            var parentPath = _controller.GetModNodePath(parent);
            if (!TscnTextSurgery.TryInsertNodeSection(_tscnEditor.Text, parentPath, created.Name, typeName, props, out var updated))
            {
                created.GetParent().RemoveChild(created);
                created.QueueFree();
                ShowOutput(ToolUi.Tr("tools/scene_builder/err_add_parent_missing"), true);
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
                ShowOutput(ToolUi.Tr("tools/scene_builder/err_del_none"), true);
                return;
            }
            if (node == _controller.GetModRoot())
            {
                ShowOutput(ToolUi.Tr("tools/scene_builder/err_del_root"), true);
                return;
            }

            var path = _controller.GetModNodePath(node);
            if (!_controller.DeleteModNode(node))
            {
                ShowOutput(ToolUi.Tr("tools/scene_builder/err_del_none"), true);
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
                ShowOutput(ToolUi.Tr("tools/scene_builder/inspector_missing_section"), true);
                GameLogger.Log("SCENE_BUILDER",
                    $"subtree remove failed: section '{path}' absent from file", LogLevel.Warning);
            }

            _selectedNode = null;
            _selectionOverlay.Track(null);
            _inspector.ShowMessage(ToolUi.Tr("tools/scene_builder/inspector_select"));
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
                ShowOutput(ToolUi.Tr("tools/scene_builder/error_no_scene"), true);
                return;
            }

            var nodePath = _controller.GetModNodePath(node);
            if (nodePath == null)
            {
                ShowOutput(ToolUi.Tr("tools/scene_builder/inspector_readonly"), true);
                GameLogger.Log("SCENE_BUILDER", $"batch rejected: '{node.Name}' is not an editable mod node", LogLevel.Warning);
                return;
            }

            var text = _tscnEditor.Text;
            foreach (var pair in batch)
            {
                var serialized = TscnValueFormatter.Format(pair.Value);
                if (serialized == null)
                {
                    ShowOutput(ToolUi.Tr("tools/scene_builder/inspector_bad_value"), true);
                    GameLogger.Log("SCENE_BUILDER", $"unserializable value for '{pair.Key}'", LogLevel.Warning);
                    continue;
                }

                if (!TscnTextSurgery.TryUpsertProperty(text, nodePath, pair.Key, serialized, out text))
                {
                    ShowOutput(ToolUi.Tr("tools/scene_builder/inspector_missing_section"), true);
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
                ShowOutput(ToolUi.Tr("tools/scene_builder/error_no_scene"), true);
                return;
            }

            var serialized = TscnValueFormatter.Format(value);
            if (serialized == null)
            {
                ShowOutput(ToolUi.Tr("tools/scene_builder/inspector_bad_value"), true);
                GameLogger.Log("SCENE_BUILDER", $"unserializable value for '{key}'", LogLevel.Warning);
                return;
            }

            var nodePath = _controller.GetModNodePath(node);
            if (nodePath == null)
            {
                ShowOutput(ToolUi.Tr("tools/scene_builder/inspector_readonly"), true);
                GameLogger.Log("SCENE_BUILDER", $"edit rejected: '{node.Name}' is not an editable mod node", LogLevel.Warning);
                return;
            }

            if (!TscnTextSurgery.TryUpsertProperty(_tscnEditor.Text, nodePath, key, serialized, out var updated))
            {
                ShowOutput(ToolUi.Tr("tools/scene_builder/inspector_missing_section"), true);
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
            SetStatus(text, isError ? ToolStatusKind.Error : ToolStatusKind.Ok);
        }
    }
}
