using System;
using System.Collections.Generic;
using System.Linq;
using Cthangover.Core.Interactive;
using Cthangover.Core.UI.Theming;
using Cthangover.Core.Utils;
using Cthangover.Mods.Interface.Theming;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Tools.Common;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Tools.InteractiveEditor
{
    /// <summary>
    /// Editor of interactive object definitions (<c>interactives/*.json</c>).
    /// The author opens a file of a writable mod, sees every object of the
    /// file over its scene background, picks one in the list or on the canvas
    /// and edits its collider by dragging handles or typing numbers, plus its
    /// look (texture, layer, highlight) and actions (scenario, commands, hover
    /// commands). Saving writes the whole file back, other objects included.
    /// <para/>
    /// The background is chosen automatically from the scene whose scenario
    /// adds the selected object (<c>interactive_add</c>), when that is known.
    /// </summary>
    public partial class InteractiveEditorWindow : ToolWindow
    {
        private const string InteractivesDir = "interactives";

        private readonly InteractiveEditorController _controller = new();
        private readonly List<string> _backgroundIds = new();
        private Dictionary<string, string> _interactiveScenes;

        private string _modId;
        private string _path;
        private bool _suppress;

        private ModFileSelector _fileSelector;
        private FilteredList _itemList;
        private FilteredList _bgList;
        private InteractiveEditorCanvas _canvas;
        private CodeEdit _jsonEdit;

        private Label _propsEmpty;
        private VBoxContainer _propsBody;
        private LineEdit _idEdit;
        private LineEdit _textureEdit;
        private OptionButton _layerSelect;
        private SpinBox _zIndex;
        private CheckBox _enabledCheck;
        private CheckBox _visibleCheck;
        private OptionButton _cursorSelect;
        private OptionButton _hitTypeSelect;
        private VBoxContainer _rectParams;
        private VBoxContainer _circleParams;
        private VBoxContainer _polyParams;
        private SpinBox _rectX, _rectY, _rectW, _rectH;
        private SpinBox _circleX, _circleY, _circleR;
        private VBoxContainer _vertexList;
        private ColorPickerButton _highlightColor;
        private SpinBox _highlightScale;
        private LineEdit _clickScenario;
        private CodeEdit _clickCommands;
        private LineEdit _hoverEnter;
        private LineEdit _hoverLeave;

        public InteractiveEditorWindow() : base("tools/interactive_editor/title")
        {
            CthangoverTheme.Apply(this);
            BuildUI();
            LoadBackgrounds();
            SetHint(ToolUi.Tr("tools/interactive_editor/hint"));
            _fileSelector.Refresh();
        }

        // ── UI construction ────────────────────────────────────────

        private void BuildUI()
        {
            var outer = CreateFillContainer();
            AddChild(outer);

            var toolbar = CreateToolbar();
            outer.AddChild(toolbar);
            _fileSelector = new ModFileSelector(InteractivesDir, ".json");
            _fileSelector.FileRequested += OnFileRequested;
            toolbar.AddChild(_fileSelector);
            toolbar.AddChild(ToolUi.Button("tools/common/save", Save, ButtonKind.Primary, "tools/common/save_tip"));
            toolbar.AddChild(UiKit.Spacer());
            var jsonToggle = ToolUi.Button("tools/common/json", null, ButtonKind.Tab, "tools/common/json_tip");
            jsonToggle.ToggleMode = true;
            jsonToggle.Toggled += on => _jsonEdit.Visible = on;
            toolbar.AddChild(jsonToggle);

            var main = new HSplitContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            outer.AddChild(main);

            var left = new VBoxContainer();
            main.AddChild(CreateSidebar(left, 260));
            BuildLeft(left);

            var centerSplit = new HSplitContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            main.AddChild(centerSplit);

            var canvasColumn = new VSplitContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            centerSplit.AddChild(canvasColumn);

            var canvasPanel = new PanelContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            canvasPanel.AddThemeStyleboxOverride("panel", UiKit.Frame(UiTokens.RadiusMd, UiTokens.Well, UiTokens.Line));
            canvasColumn.AddChild(canvasPanel);

            _canvas = new InteractiveEditorCanvas { Controller = _controller };
            _canvas.Changed += OnCanvasChanged;
            _canvas.ItemPicked += _ => RefreshAll();
            canvasPanel.AddChild(_canvas);

            _jsonEdit = ToolUi.Code(editable: false);
            _jsonEdit.CustomMinimumSize = new Vector2(0, 160);
            _jsonEdit.Visible = false;
            canvasColumn.AddChild(_jsonEdit);

            var right = new VBoxContainer();
            centerSplit.AddChild(CreateSidebar(right, 330));
            BuildProperties(right);
        }

        private void BuildLeft(VBoxContainer column)
        {
            column.AddChild(ToolUi.Section("tools/interactive_editor/items"));
            _itemList = new FilteredList(180);
            _itemList.Chosen += OnItemChosen;
            column.AddChild(_itemList);

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", UiTokens.Space1);
            column.AddChild(row);
            AddExpanding(row, ToolUi.Button("tools/interactive_editor/add", OnAdd));
            AddExpanding(row, ToolUi.Button("tools/interactive_editor/duplicate", OnDuplicate));
            column.AddChild(ToolUi.Button("tools/interactive_editor/delete", OnDelete, ButtonKind.Danger));

            column.AddChild(ToolUi.Section("tools/interactive_editor/background"));
            _bgList = new FilteredList(160);
            _bgList.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            _bgList.Chosen += ShowBackground;
            column.AddChild(_bgList);
        }

        private static void AddExpanding(Container parent, Control control)
        {
            control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            parent.AddChild(control);
        }

        private void BuildProperties(VBoxContainer column)
        {
            column.AddChild(ToolUi.Section("tools/interactive_editor/props"));
            _propsEmpty = ToolUi.Hint(ToolUi.Tr("tools/interactive_editor/none_selected"));
            column.AddChild(_propsEmpty);

            var body = new VBoxContainer();
            body.AddThemeConstantOverride("separation", UiTokens.Space2);
            column.AddChild(body);
            _propsBody = body;

            _idEdit = ToolUi.Field(body, "tools/interactive_editor/label_id", ToolUi.Text("crypt_spot", _ => OnIdChanged()));
            _idEdit.AddThemeFontOverride("font", ToolUi.Mono());
            _textureEdit = ToolUi.Field(body, "tools/interactive_editor/label_texture_key", ToolUi.Text(ToolUi.Tr("tools/interactive_editor/texture_placeholder"), _ => OnFieldsChanged()),
                "tools/interactive_editor/texture_tip");

            _layerSelect = ToolUi.Field(body, "tools/interactive_editor/label_layer", new OptionButton());
            foreach (var layer in InteractiveEditorController.Layers)
                _layerSelect.AddItem(layer);
            _layerSelect.ItemSelected += _ => OnFieldsChanged();

            _zIndex = ToolUi.Field(body, "tools/interactive_editor/label_z", ToolUi.Number(-100, 100, 1, _ => OnFieldsChanged()));

            _cursorSelect = ToolUi.Field(body, "tools/interactive_editor/label_cursor", new OptionButton());
            _cursorSelect.AddItem(ToolUi.Tr("tools/interactive_editor/cursor_none"));
            _cursorSelect.AddItem("PointingHand");
            _cursorSelect.ItemSelected += _ => OnFieldsChanged();

            _enabledCheck = new CheckBox { Text = ToolUi.Tr("tools/interactive_editor/enabled") };
            _enabledCheck.Toggled += _ => OnFieldsChanged();
            body.AddChild(_enabledCheck);
            _visibleCheck = new CheckBox { Text = ToolUi.Tr("tools/interactive_editor/visible") };
            _visibleCheck.Toggled += _ => OnFieldsChanged();
            body.AddChild(_visibleCheck);

            body.AddChild(ToolUi.Section("tools/interactive_editor/collider"));
            _hitTypeSelect = ToolUi.Field(body, "tools/interactive_editor/label_collider_type", new OptionButton());
            foreach (var type in InteractiveEditorController.HitTypes)
                _hitTypeSelect.AddItem(HitTypeLabel(type));
            _hitTypeSelect.ItemSelected += OnHitTypeChanged;

            _rectParams = new VBoxContainer();
            body.AddChild(_rectParams);
            _rectX = ToolUi.Field(_rectParams, "tools/interactive_editor/label_x", ToolUi.Number(0, 1, 0.001, _ => OnGeometryTyped()));
            _rectY = ToolUi.Field(_rectParams, "tools/interactive_editor/label_y", ToolUi.Number(0, 1, 0.001, _ => OnGeometryTyped()));
            _rectW = ToolUi.Field(_rectParams, "tools/interactive_editor/label_w", ToolUi.Number(0.001, 1, 0.001, _ => OnGeometryTyped()));
            _rectH = ToolUi.Field(_rectParams, "tools/interactive_editor/label_h", ToolUi.Number(0.001, 1, 0.001, _ => OnGeometryTyped()));

            _circleParams = new VBoxContainer();
            body.AddChild(_circleParams);
            _circleX = ToolUi.Field(_circleParams, "tools/interactive_editor/label_cx", ToolUi.Number(0, 1, 0.001, _ => OnGeometryTyped()));
            _circleY = ToolUi.Field(_circleParams, "tools/interactive_editor/label_cy", ToolUi.Number(0, 1, 0.001, _ => OnGeometryTyped()));
            _circleR = ToolUi.Field(_circleParams, "tools/interactive_editor/label_r", ToolUi.Number(0.001, 1, 0.001, _ => OnGeometryTyped()));

            _polyParams = new VBoxContainer();
            body.AddChild(_polyParams);
            _polyParams.AddChild(ToolUi.Hint(ToolUi.Tr("tools/interactive_editor/polygon_hint")));
            _vertexList = new VBoxContainer();
            _vertexList.AddThemeConstantOverride("separation", UiTokens.Space1);
            _polyParams.AddChild(_vertexList);

            body.AddChild(ToolUi.Section("tools/interactive_editor/highlight"));
            _highlightColor = ToolUi.Field(body, "tools/interactive_editor/label_highlight_color", new ColorPickerButton { CustomMinimumSize = new Vector2(0, 28) });
            _highlightColor.ColorChanged += _ => OnFieldsChanged();
            _highlightScale = ToolUi.Field(body, "tools/interactive_editor/label_highlight_scale", ToolUi.Number(0.5, 2, 0.01, _ => OnFieldsChanged()));

            body.AddChild(ToolUi.Section("tools/interactive_editor/actions"));
            _clickScenario = ToolUi.Field(body, "tools/interactive_editor/label_onclick_scenario", ToolUi.Text("scenarios/…/file.scenario", _ => OnFieldsChanged()));
            body.AddChild(UiKit.Label(ToolUi.Tr("tools/interactive_editor/label_onclick_commands"), UiTokens.FontBodySmall, UiTokens.Muted));
            _clickCommands = ToolUi.Code();
            _clickCommands.GuttersDrawLineNumbers = false;
            _clickCommands.CustomMinimumSize = new Vector2(0, 70);
            _clickCommands.SyntaxHighlighter = ScenarioSyntaxService.CreateHighlighter();
            _clickCommands.TextChanged += OnFieldsChanged;
            body.AddChild(_clickCommands);
            _hoverEnter = ToolUi.Field(body, "tools/interactive_editor/label_hover_enter", ToolUi.Text("", _ => OnFieldsChanged()));
            _hoverLeave = ToolUi.Field(body, "tools/interactive_editor/label_hover_leave", ToolUi.Text("", _ => OnFieldsChanged()));
        }

        // ── loading & saving ───────────────────────────────────────

        private void LoadBackgrounds()
        {
            _backgroundIds.AddRange(ModResourceService.GetBackgroundIds());
            _bgList.SetItems(_backgroundIds);
        }

        private void ShowBackground(string id)
        {
            if (string.IsNullOrEmpty(id))
                return;

            _bgList.Select(id);
            _canvas.SetBackground(ModResourceService.LoadBackgroundTexture(id));
        }

        /// <summary>Shows the background of the scene that adds the selected object, when known.</summary>
        private void ShowSceneBackground()
        {
            var id = _controller.Selected?.ID;
            if (id == null)
                return;

            _interactiveScenes ??= SceneBackgroundService.MapInteractivesToScenes();
            if (!_interactiveScenes.TryGetValue(id, out var scene))
                return;

            var background = SceneBackgroundService.ForScene(scene);
            if (background == null || background == _bgList.SelectedId)
                return;

            if (ModResourceService.LoadBackgroundTexture(background) == null)
                SetStatus(string.Format(ToolUi.Tr("tools/common/background_missing"), background, scene), ToolStatusKind.Error);
            else
                ShowBackground(background);
        }

        private void OnFileRequested(string modId, string path)
        {
            RunDiscardingChanges(() => Load(modId, path), () => _fileSelector.Show(_modId, _path));
        }

        private void Load(string modId, string path)
        {
            _modId = modId;
            _path = path;
            _controller.Clear();
            MarkClean();

            if (modId == null)
                SetStatus(ToolUi.Tr("tools/common/no_writable_mods"), ToolStatusKind.Error);
            else if (path == null)
                SetStatus(string.Format(ToolUi.Tr("tools/common/no_files"), modId));
            else
            {
                try
                {
                    var text = ModFileService.ReadText(modId, path);
                    _controller.FromJson(text);
                    SetStatus(string.Format(ToolUi.Tr(string.IsNullOrEmpty(text) ? "tools/common/new_file_status" : "tools/common/loaded"), modId + "/" + path));
                }
                catch (Exception ex)
                {
                    SetStatus($"{ToolUi.Tr("tools/common/load_error")}: {ex.Message}", ToolStatusKind.Error);
                    GameLogger.Log("INTERACTIVE_EDITOR", $"load failed for '{modId}/{path}': {ex.Message}", LogLevel.Error);
                }
            }

            _fileSelector.Show(_modId, _path);
            RefreshAll();
            ShowSceneBackground();
        }

        /// <summary>Ctrl+S.</summary>
        protected override void OnSaveShortcut() => Save();

        private void Save()
        {
            if (_path == null)
            {
                SetStatus(ToolUi.Tr("tools/common/no_file_to_save"), ToolStatusKind.Error);
                return;
            }

            var problem = Validate();
            if (problem != null)
            {
                SetStatus(problem, ToolStatusKind.Error);
                return;
            }

            if (!ModFileService.WriteText(_modId, _path, _controller.ToJson()))
            {
                SetStatus(ToolUi.Tr("tools/common/readonly"), ToolStatusKind.Error);
                return;
            }

            MarkClean();
            _fileSelector.Show(_modId, _path);
            SetStatus(string.Format(ToolUi.Tr("tools/common/saved"), _modId + "/" + _path), ToolStatusKind.Ok);
        }

        /// <summary>First blocking problem of the file (empty/duplicate ID, polygon with &lt; 3 vertices), or null.</summary>
        private string Validate()
        {
            foreach (var item in _controller.Items)
            {
                if (string.IsNullOrWhiteSpace(item.ID))
                    return ToolUi.Tr("tools/interactive_editor/id_empty");
                if (item.HitArea.Type == "polygon" && (item.HitArea.Vertices?.Length ?? 0) < 3)
                    return string.Format(ToolUi.Tr("tools/interactive_editor/polygon_too_small"), item.ID);
            }

            var duplicate = _controller.Items.GroupBy(i => i.ID, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            return duplicate != null ? $"{ToolUi.Tr("tools/interactive_editor/duplicate_id")} {duplicate.Key}" : null;
        }

        // ── list actions ───────────────────────────────────────────

        private void OnItemChosen(string indexText)
        {
            if (_suppress || !int.TryParse(indexText, out var index))
                return;

            _controller.Select(index);
            RefreshAll();
            ShowSceneBackground();
        }

        private void OnAdd()
        {
            if (_path == null)
            {
                SetStatus(ToolUi.Tr("tools/common/no_file_to_save"), ToolStatusKind.Error);
                return;
            }

            _controller.Add();
            Changed();
            RefreshAll();
        }

        private void OnDuplicate()
        {
            if (_controller.DuplicateSelected() == null)
                return;
            Changed();
            RefreshAll();
        }

        private void OnDelete()
        {
            var item = _controller.Selected;
            if (item == null)
                return;

            ShowConfirm("tools/interactive_editor/delete", "tools/interactive_editor/delete_confirm", () =>
            {
                _controller.RemoveSelected();
                Changed();
                RefreshAll();
                SetStatus(string.Format(ToolUi.Tr("tools/interactive_editor/deleted"), item.ID));
            });
        }

        /// <summary>Delete removes the selected object (with confirmation).</summary>
        protected override bool HandleKey(InputEventKey key)
        {
            if (key.Keycode != Key.Delete || _controller.Selected == null)
                return false;
            OnDelete();
            return true;
        }

        // ── property editing ───────────────────────────────────────

        private void OnIdChanged()
        {
            var item = _controller.Selected;
            if (_suppress || item == null)
                return;

            item.ID = _idEdit.Text.Trim();
            Changed();
            RefreshItemList();
        }

        private void OnFieldsChanged()
        {
            var item = _controller.Selected;
            if (_suppress || item == null)
                return;

            item.Texture = NullIfEmpty(_textureEdit.Text);
            item.Layer = InteractiveEditorController.Layers[Math.Max(0, _layerSelect.Selected)];
            item.ZIndex = (int)_zIndex.Value;
            item.Enabled = _enabledCheck.ButtonPressed;
            item.Visible = _visibleCheck.ButtonPressed;
            item.Cursor = _cursorSelect.Selected == 1 ? "PointingHand" : null;
            item.Highlight.ColorHex = "#" + _highlightColor.Color.ToHtml(true);
            item.Highlight.Scale = (float)_highlightScale.Value;

            var commands = _clickCommands.Text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
            var scenario = NullIfEmpty(_clickScenario.Text);
            item.Actions.OnClick = scenario != null || commands.Length > 0
                ? new ClickAction { Scenario = scenario, Commands = commands.Length > 0 ? commands : null }
                : null;
            item.Actions.OnHoverEnter = NullIfEmpty(_hoverEnter.Text);
            item.Actions.OnHoverLeave = NullIfEmpty(_hoverLeave.Text);
            Changed();
        }

        private void OnHitTypeChanged(long index)
        {
            var item = _controller.Selected;
            if (_suppress || item == null)
                return;

            InteractiveEditorController.SetHitType(item, InteractiveEditorController.HitTypes[index]);
            Changed();
            RefreshAll();
        }

        private void OnGeometryTyped()
        {
            var hit = _controller.Selected?.HitArea;
            if (_suppress || hit == null)
                return;

            if (hit.Type == "circle")
            {
                hit.X = (float)_circleX.Value;
                hit.Y = (float)_circleY.Value;
                hit.Radius = (float)_circleR.Value;
            }
            else if (hit.Type == "rect")
            {
                hit.X = (float)_rectX.Value;
                hit.Y = (float)_rectY.Value;
                hit.Width = (float)_rectW.Value;
                hit.Height = (float)_rectH.Value;
            }

            Changed();
            _canvas.RebuildHandles();
            _canvas.QueueRedraw();
        }

        private void OnCanvasChanged()
        {
            Changed();
            _suppress = true;
            WriteGeometry(_controller.Selected?.HitArea);
            _suppress = false;
        }

        private void Changed()
        {
            SetDirty();
            if (_jsonEdit.Visible)
                _jsonEdit.Text = _controller.ToJson();
        }

        // ── refresh ────────────────────────────────────────────────

        private void RefreshAll()
        {
            RefreshItemList();
            _suppress = true;
            WriteSidebar();
            _suppress = false;
            _jsonEdit.Text = _controller.ToJson();
            _canvas.RebuildHandles();
            _canvas.QueueRedraw();
        }

        private void RefreshItemList()
        {
            var items = _controller.Items
                .Select((item, i) => (i.ToString(), (string.IsNullOrEmpty(item.ID) ? "?" : item.ID) + "  ·  " + HitTypeLabel(item.HitArea.Type), (Texture2D)null));
            _itemList.SetItems(items);
            _itemList.Select(_controller.SelectedIndex >= 0 ? _controller.SelectedIndex.ToString() : null);
        }

        private void WriteSidebar()
        {
            var item = _controller.Selected;
            _propsEmpty.Visible = item == null;
            _propsBody.Visible = item != null;
            if (item == null)
                return;

            _idEdit.Text = item.ID ?? string.Empty;
            _textureEdit.Text = item.Texture ?? string.Empty;
            _layerSelect.Select(Math.Max(0, Array.IndexOf(InteractiveEditorController.Layers, item.Layer)));
            _zIndex.Value = item.ZIndex;
            _enabledCheck.ButtonPressed = item.Enabled;
            _visibleCheck.ButtonPressed = item.Visible;
            _cursorSelect.Select(string.IsNullOrEmpty(item.Cursor) ? 0 : 1);
            _hitTypeSelect.Select(Math.Max(0, Array.IndexOf(InteractiveEditorController.HitTypes, item.HitArea.Type)));
            _highlightColor.Color = item.Highlight.Color;
            _highlightScale.Value = item.Highlight.Scale;
            _clickScenario.Text = item.Actions.OnClick?.Scenario ?? string.Empty;
            _clickCommands.Text = item.Actions.OnClick?.Commands != null ? string.Join("\n", item.Actions.OnClick.Commands) : string.Empty;
            _hoverEnter.Text = item.Actions.OnHoverEnter ?? string.Empty;
            _hoverLeave.Text = item.Actions.OnHoverLeave ?? string.Empty;
            WriteGeometry(item.HitArea);
        }

        private void WriteGeometry(HitAreaDefinition hit)
        {
            if (hit == null)
                return;

            _rectParams.Visible = hit.Type == "rect";
            _circleParams.Visible = hit.Type == "circle";
            _polyParams.Visible = hit.Type == "polygon";

            _rectX.Value = hit.X;
            _rectY.Value = hit.Y;
            _rectW.Value = hit.Width;
            _rectH.Value = hit.Height;
            _circleX.Value = hit.X;
            _circleY.Value = hit.Y;
            _circleR.Value = hit.Radius;
            RefreshVertexList(hit);
        }

        /// <summary>X/Y spin rows per polygon vertex; handlers read the vertex at change time.</summary>
        private void RefreshVertexList(HitAreaDefinition hit)
        {
            var vertices = hit.Type == "polygon" ? hit.Vertices ?? Array.Empty<Vector2>() : Array.Empty<Vector2>();
            var rows = _vertexList.GetChildren();

            // Reuse rows while dragging so focus and scroll are not lost on every motion.
            if (rows.Count == vertices.Length)
            {
                for (var i = 0; i < vertices.Length; i++)
                {
                    var row = rows[i];
                    ((SpinBox)row.GetChild(1)).SetValueNoSignal(vertices[i].X);
                    ((SpinBox)row.GetChild(2)).SetValueNoSignal(vertices[i].Y);
                }
                return;
            }

            foreach (var child in rows)
                child.QueueFree();

            for (var i = 0; i < vertices.Length; i++)
            {
                var index = i;
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", UiTokens.Space1);
                var number = UiKit.Label((i + 1).ToString(), UiTokens.FontSmall, UiTokens.Faint);
                number.CustomMinimumSize = new Vector2(20, 0);
                row.AddChild(number);

                var x = ToolUi.Number(0, 1, 0.001, null);
                x.Value = vertices[i].X;
                x.ValueChanged += v => OnVertexTyped(index, (float)v, null);
                row.AddChild(x);

                var y = ToolUi.Number(0, 1, 0.001, null);
                y.Value = vertices[i].Y;
                y.ValueChanged += v => OnVertexTyped(index, null, (float)v);
                row.AddChild(y);

                _vertexList.AddChild(row);
            }
        }

        private void OnVertexTyped(int index, float? x, float? y)
        {
            var hit = _controller.Selected?.HitArea;
            if (_suppress || hit?.Vertices == null || index >= hit.Vertices.Length)
                return;

            var v = hit.Vertices[index];
            hit.Vertices[index] = new Vector2(x ?? v.X, y ?? v.Y);
            Changed();
            _canvas.RebuildHandles();
            _canvas.QueueRedraw();
        }

        private static string HitTypeLabel(string type) => ToolUi.Tr(type switch
        {
            "circle" => "tools/interactive_editor/hit_circle",
            "polygon" => "tools/interactive_editor/hit_polygon",
            _ => "tools/interactive_editor/hit_rect"
        });

        private static string NullIfEmpty(string text)
        {
            var trimmed = text?.Trim();
            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }
    }
}
