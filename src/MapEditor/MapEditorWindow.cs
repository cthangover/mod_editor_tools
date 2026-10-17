using System;
using System.Collections.Generic;
using System.Linq;
using Cthangover.Core.Localization;
using Cthangover.Core.Mods.Resolvers;
using Cthangover.Core.Scenes;
using Cthangover.Core.Settings;
using Cthangover.Core.UI;
using Cthangover.Core.UI.Theming;
using Cthangover.Core.Utils;
using Cthangover.Mods.Interface.Theming;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Mods.Map;
using Cthangover.Tools.Common;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Tools.MapEditor
{
    /// <summary>
    /// Editor window for the world map. The author picks a writable mod and a
    /// file under its <c>map/</c> folder (or creates one), then draws region
    /// polygons, places icon markers and free-standing text labels over a
    /// background preview.
    /// <para/>
    /// Layout: toolbar (file, save, mode, background, preview), zone list on
    /// the left, canvas in the middle, properties of the selected zone on the
    /// right, and an optional read-only JSON view below the canvas. Switching
    /// file or mod with unsaved edits asks first.
    /// <para/>
    /// The editor edits raw authoring data only: the produced JSON matches
    /// <c>MapZone</c>. Runtime state — what scenarios open or reveal — is not
    /// touched; after a successful save the running map widget is asked to
    /// reload so the edit is visible immediately.
    /// </summary>
    public partial class MapEditorWindow : ToolWindow
    {
        private readonly MapEditorController _controller = new();

        private readonly List<string> _backgroundIds = new();
        private readonly List<string> _sceneIds = new();

        private string _modId;
        private string _path;

        private ModFileSelector _fileSelector;
        private Button _selectModeBtn;
        private Button _drawModeBtn;
        private OptionButton _backgroundSelect;

        private FilteredList _zoneList;
        private Button _deleteBtn;

        private VBoxContainer _props;
        private Label _propsEmpty;
        private VBoxContainer _propsBody;
        private LineEdit _idEdit;
        private LineEdit _titleEdit;
        private Label _titlePreview;
        private OptionButton _sceneSelect;
        private LineEdit _descEdit;
        private LineEdit _imageEdit;
        private TextureRect _imagePreview;
        private FilteredList _iconList;
        private SpinBox _posX;
        private SpinBox _posY;
        private CheckBox _labelCheck;
        private SpinBox _labelX;
        private SpinBox _labelY;
        private CheckBox _lockedCheck;
        private CheckBox _hiddenCheck;
        private Label _availabilityLabel;
        private VBoxContainer _vertexList;
        private CollapsibleSection _markerSection;
        private CollapsibleSection _labelSection;
        private CollapsibleSection _vertexSection;
        private MapZone _sectionsShownFor;

        private MapEditorCanvas _canvas;
        private TextEdit _jsonEdit;
        private Button _jsonToggle;
        private MapWidget _preview;

        private bool _suppress;

        public MapEditorWindow() : base("tools/map_editor/title")
        {
            CthangoverTheme.Apply(this);

            ReloadLocalization();
            BuildUI();
            LoadSceneList();
            LoadTextureList();
            LoadBackgroundList();
            SetHint(ToolUi.Tr("tools/map_editor/hint"));
            _fileSelector.Refresh();
        }

        // ── UI construction ────────────────────────────────────────

        private void BuildUI()
        {
            var outer = CreateFillContainer();
            AddChild(outer);

            outer.AddChild(BuildToolbar());

            var main = new HSplitContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            outer.AddChild(main);

            var zonesColumn = new VBoxContainer();
            main.AddChild(CreateSidebar(zonesColumn, 240));
            BuildZoneList(zonesColumn);

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

            _canvas = new MapEditorCanvas { Controller = _controller };
            _canvas.Changed += OnCanvasChanged;
            _canvas.ZonePicked += OnCanvasZonePicked;
            _canvas.ModeChanged += SyncModeButtons;
            canvasPanel.AddChild(_canvas);

            _jsonEdit = ToolUi.Code(editable: false);
            _jsonEdit.CustomMinimumSize = new Vector2(0, 160);
            _jsonEdit.Visible = false;
            canvasColumn.AddChild(_jsonEdit);

            _props = new VBoxContainer();
            centerSplit.AddChild(CreateSidebar(_props, 320));
            BuildProperties(_props);
        }

        private HBoxContainer BuildToolbar()
        {
            var toolbar = CreateToolbar();

            _fileSelector = new ModFileSelector(MapFileService.MapDir, ".json");
            _fileSelector.FileRequested += OnFileRequested;
            toolbar.AddChild(_fileSelector);

            toolbar.AddChild(ToolUi.Button("tools/common/save", Save, ButtonKind.Primary, "tools/common/save_tip"));
            toolbar.AddChild(ToolUi.Button("tools/map_editor/reload_map", OnReloadMap, ButtonKind.Secondary, "tools/map_editor/reload_map_tip"));
            toolbar.AddChild(ToolUi.Divider());

            var modeGroup = new ButtonGroup();
            _selectModeBtn = ModeButton("tools/map_editor/select_mode", modeGroup, MapEditorCanvas.ModeSelect);
            _selectModeBtn.ButtonPressed = true;
            toolbar.AddChild(_selectModeBtn);
            _drawModeBtn = ModeButton("tools/map_editor/draw_region", modeGroup, MapEditorCanvas.ModeDraw);
            toolbar.AddChild(_drawModeBtn);
            toolbar.AddChild(ToolUi.Divider());

            _backgroundSelect = new OptionButton { TooltipText = ToolUi.Tr("tools/map_editor/background"), CustomMinimumSize = new Vector2(160, 0) };
            _backgroundSelect.ItemSelected += OnBackgroundSelected;
            toolbar.AddChild(_backgroundSelect);

            toolbar.AddChild(UiKit.Spacer());

            _jsonToggle = ToolUi.Button("tools/common/json", null, ButtonKind.Tab, "tools/common/json_tip");
            _jsonToggle.ToggleMode = true;
            _jsonToggle.Toggled += on => _jsonEdit.Visible = on;
            toolbar.AddChild(_jsonToggle);

            toolbar.AddChild(ToolUi.Button("tools/map_editor/preview", OnPreviewPressed, ButtonKind.Secondary, "tools/map_editor/preview_tip"));
            return toolbar;
        }

        private Button ModeButton(string textKey, ButtonGroup group, string mode)
        {
            var button = ToolUi.Button(textKey, null, ButtonKind.Tab);
            button.ToggleMode = true;
            button.ButtonGroup = group;
            button.Toggled += on =>
            {
                if (on && !_suppress && _canvas != null)
                    _canvas.SetMode(mode);
            };
            return button;
        }

        private void BuildZoneList(VBoxContainer column)
        {
            column.AddChild(ToolUi.Section("tools/map_editor/zones"));

            _zoneList = new FilteredList(240);
            _zoneList.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            _zoneList.Chosen += OnZoneChosen;
            column.AddChild(_zoneList);

            var addRow = new HBoxContainer();
            addRow.AddThemeConstantOverride("separation", UiTokens.Space1);
            column.AddChild(addRow);
            AddExpanding(addRow, ToolUi.Button("tools/map_editor/add_region", OnAddRegion, ButtonKind.Secondary, "tools/map_editor/add_region_tip"));
            AddExpanding(addRow, ToolUi.Button("tools/map_editor/add_marker", OnAddMarker));
            AddExpanding(addRow, ToolUi.Button("tools/map_editor/add_label", OnAddLabel));

            _deleteBtn = ToolUi.Button("tools/map_editor/delete", OnDeleteZone, ButtonKind.Danger, "tools/map_editor/delete_tip");
            column.AddChild(_deleteBtn);
        }

        private static void AddExpanding(Container parent, Control control)
        {
            control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            parent.AddChild(control);
        }

        private void BuildProperties(VBoxContainer column)
        {
            column.AddChild(ToolUi.Section("tools/map_editor/props"));

            _propsEmpty = ToolUi.Hint(ToolUi.Tr("tools/map_editor/zone_missing"));
            column.AddChild(_propsEmpty);

            var body = new VBoxContainer();
            body.AddThemeConstantOverride("separation", UiTokens.Space2);
            column.AddChild(body);
            _propsBody = body;

            _idEdit = ToolUi.Field(body, "tools/map_editor/id", ToolUi.Text("region", _ => OnIdChanged()));
            _idEdit.AddThemeFontOverride("font", ToolUi.Mono());
            _titleEdit = ToolUi.Field(body, "tools/map_editor/title_key", ToolUi.Text("ui/map/…", _ => OnTitleChanged()));
            _titlePreview = ToolUi.Hint(string.Empty);
            body.AddChild(_titlePreview);
            _descEdit = ToolUi.Field(body, "tools/map_editor/description", ToolUi.Text("ui/map/…", _ => OnFieldsChanged()));

            _sceneSelect = ToolUi.Field(body, "tools/map_editor/scene", new OptionButton());
            _sceneSelect.ItemSelected += _ => OnFieldsChanged();

            body.AddChild(ToolUi.Section("tools/map_editor/availability"));
            _lockedCheck = new CheckBox { Text = ToolUi.Tr("tools/map_editor/locked") };
            _lockedCheck.Toggled += _ => OnFieldsChanged();
            body.AddChild(_lockedCheck);
            _hiddenCheck = new CheckBox { Text = ToolUi.Tr("tools/map_editor/hidden") };
            _hiddenCheck.Toggled += _ => OnFieldsChanged();
            body.AddChild(_hiddenCheck);
            _availabilityLabel = ToolUi.Hint(string.Empty);
            body.AddChild(_availabilityLabel);

            _markerSection = new CollapsibleSection("tools/map_editor/marker");
            body.AddChild(_markerSection);
            var marker = _markerSection.Body;
            var imageRow = new HBoxContainer();
            _imagePreview = new TextureRect
            {
                CustomMinimumSize = new Vector2(32, 32),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
            };
            _imageEdit = ToolUi.Text(ToolUi.Tr("tools/map_editor/image_placeholder"), _ => OnImageChanged());
            _imageEdit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            imageRow.AddChild(_imageEdit);
            imageRow.AddChild(_imagePreview);
            ToolUi.Field(marker, "tools/map_editor/image", imageRow);

            _iconList = new FilteredList(110, "tools/map_editor/icon_search");
            _iconList.Chosen += OnIconChosen;
            marker.AddChild(_iconList);

            _posX = ToolUi.Field(marker, "tools/map_editor/x", ToolUi.Number(0, 1, 0.001, _ => OnPositionChanged()));
            _posY = ToolUi.Field(marker, "tools/map_editor/y", ToolUi.Number(0, 1, 0.001, _ => OnPositionChanged()));

            _labelSection = new CollapsibleSection("tools/map_editor/label_section");
            body.AddChild(_labelSection);
            _labelCheck = new CheckBox { Text = ToolUi.Tr("tools/map_editor/label") };
            _labelCheck.Toggled += OnLabelToggled;
            _labelSection.Body.AddChild(_labelCheck);
            _labelX = ToolUi.Field(_labelSection.Body, "tools/map_editor/x", ToolUi.Number(0, 1, 0.001, _ => OnPositionChanged()));
            _labelY = ToolUi.Field(_labelSection.Body, "tools/map_editor/y", ToolUi.Number(0, 1, 0.001, _ => OnPositionChanged()));

            _vertexSection = new CollapsibleSection("tools/map_editor/vertices");
            body.AddChild(_vertexSection);
            _vertexSection.Body.AddChild(ToolUi.Hint(ToolUi.Tr("tools/map_editor/vertices_hint")));
            _vertexList = new VBoxContainer();
            _vertexList.AddThemeConstantOverride("separation", UiTokens.Space1);
            _vertexSection.Body.AddChild(_vertexList);
            _vertexSection.Body.AddChild(ToolUi.Button("tools/map_editor/add_vertex", OnAddVertex));
        }

        // ── data loading ───────────────────────────────────────────

        private void LoadSceneList()
        {
            _sceneIds.Clear();
            _sceneSelect.Clear();
            _sceneSelect.AddItem(ToolUi.Tr("tools/map_editor/scene_none"));

            var defs = SceneDefinitions.Collect();
            if (defs != null)
                _sceneIds.AddRange(defs.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase));

            foreach (var scene in _sceneIds)
                _sceneSelect.AddItem(scene);
        }

        private void LoadTextureList()
        {
            _iconList.SetItems(ModResourceService.GetIconIds());
        }

        private void LoadBackgroundList()
        {
            _backgroundIds.Clear();
            _backgroundSelect.Clear();

            _backgroundIds.AddRange(ModResourceService.GetBackgroundIds());
            foreach (var id in _backgroundIds)
                _backgroundSelect.AddItem(id);

            var index = _backgroundIds.FindIndex(id => string.Equals(id, "map", StringComparison.OrdinalIgnoreCase));
            if (index < 0 && _backgroundIds.Count > 0)
                index = 0;

            if (index >= 0)
            {
                _backgroundSelect.Select(index);
                _canvas.SetBackground(ModResourceService.LoadBackgroundTexture(_backgroundIds[index]));
            }
        }

        // ── file actions ───────────────────────────────────────────

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
            {
                SetStatus(ToolUi.Tr("tools/common/no_writable_mods"), ToolStatusKind.Error);
            }
            else if (path == null)
            {
                SetStatus(string.Format(ToolUi.Tr("tools/common/no_files"), modId), ToolStatusKind.Info);
            }
            else
            {
                try
                {
                    var text = ModFileService.ReadText(modId, path);
                    if (!string.IsNullOrEmpty(text))
                        _controller.FromJson(text);
                    SetStatus(string.Format(ToolUi.Tr(string.IsNullOrEmpty(text) ? "tools/common/new_file_status" : "tools/common/loaded"), modId + "/" + path));
                }
                catch (Exception ex)
                {
                    SetStatus($"{ToolUi.Tr("tools/common/load_error")}: {ex.Message}", ToolStatusKind.Error);
                    GameLogger.Log("MAP_EDITOR", $"load failed for '{modId}/{path}': {ex.Message}", LogLevel.Error);
                }
            }

            _fileSelector.Show(_modId, _path);
            _canvas.FinishDrawing();
            RefreshAll();
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

            var invalid = _controller.Zones.FirstOrDefault(z => string.IsNullOrWhiteSpace(z.ID));
            if (invalid != null)
            {
                SetStatus(ToolUi.Tr("tools/map_editor/id_empty"), ToolStatusKind.Error);
                return;
            }

            var duplicate = _controller.Zones.GroupBy(z => z.ID, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
            {
                SetStatus($"{ToolUi.Tr("tools/map_editor/duplicate_id")} {duplicate.Key}", ToolStatusKind.Error);
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
            ReloadRunningMap();
        }

        /// <summary>
        /// Pushes the saved data into the running game. When the map panel was
        /// already opened once, its widget re-reads through the service and
        /// rebuilds; otherwise only the service is reloaded and the widget
        /// picks the data up on its first open.
        /// </summary>
        private static void ReloadRunningMap()
        {
            if (UiHost.Instance != null
                && UiHost.Instance.TryGetInstance("map", out var control)
                && control is MapWidget mapWidget)
            {
                mapWidget.Reload();
                return;
            }

            MapZoneService.Instance.Reload();
        }

        private void OnReloadMap()
        {
            ReloadRunningMap();
            SetStatus(ToolUi.Tr("tools/map_editor/reloaded"), ToolStatusKind.Ok);
        }

        // ── shortcuts ──────────────────────────────────────────────

        /// <summary>Esc/Enter finish drawing, Delete removes the selected zone (with confirmation).</summary>
        protected override bool HandleKey(InputEventKey key)
        {
            if (_preview != null)
                return false;

            switch (key.Keycode)
            {
                case Key.Escape:
                case Key.Enter:
                case Key.KpEnter:
                    if (_canvas.Mode != MapEditorCanvas.ModeDraw)
                        return false;
                    _canvas.FinishDrawing();
                    return true;
                case Key.Delete:
                    OnDeleteZone();
                    return true;
                default:
                    return false;
            }
        }

        // ── game-like preview ──────────────────────────────────────

        /// <summary>True while the game-like preview covers the editor.</summary>
        public bool PreviewActive => _preview != null;

        /// <summary>
        /// Opens the game-like preview on top of the editor: the real
        /// <c>map_widget.tscn</c> fed with the current — possibly unsaved —
        /// zone list, so hover highlights, marker pills, the location card and
        /// the transition button behave exactly as in the running map. The
        /// preview ends through the widget itself: its close button or Escape
        /// hides it, and the editor resumes. Returns <c>false</c> when the map
        /// widget scene cannot be resolved.
        /// </summary>
        public bool StartPreview()
        {
            if (_preview != null)
                return true;

            ReloadLocalization();

            var widget = TscnScenes.LoadAndBuild("scenes/ui/map_widget.tscn") as MapWidget;
            if (widget == null)
            {
                SetStatus(ToolUi.Tr("tools/map_editor/preview_missing"), ToolStatusKind.Error);
                return false;
            }

            widget.SetPreviewZones(_controller.Zones);
            widget.VisibilityChanged += OnPreviewVisibilityChanged;
            AddChild(widget);
            widget.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _preview = widget;

            widget.EnsureConstructed();
            widget.Show();
            SetStatus(ToolUi.Tr("tools/map_editor/preview_on"));
            return true;
        }

        /// <summary>Closes the preview and returns to editing.</summary>
        public void StopPreview()
        {
            if (_preview == null)
                return;

            var widget = _preview;
            _preview = null;

            if (GodotObject.IsInstanceValid(widget))
            {
                widget.VisibilityChanged -= OnPreviewVisibilityChanged;
                widget.QueueFree();
            }

            UpdateTitlePreview();
            _canvas.QueueRedraw();
            SetStatus(ToolUi.Tr("tools/map_editor/preview_off"));
        }

        private void OnPreviewPressed()
        {
            StartPreview();
        }

        private void OnPreviewVisibilityChanged()
        {
            if (_preview != null && GodotObject.IsInstanceValid(_preview) && !_preview.Visible)
                StopPreview();
        }

        /// <summary>
        /// Re-reads every mod's locale files so keys edited after the game
        /// booted — the common case while authoring titles and descriptions —
        /// resolve to their new values in the editor and in the preview.
        /// </summary>
        private static void ReloadLocalization()
        {
            if (GameData.Instance == null)
                return;

            try
            {
                LocaleLoader.LoadCurrentLanguage();
            }
            catch (Exception ex)
            {
                GameLogger.Log("MAP_EDITOR", $"localisation reload failed: {ex.Message}", LogLevel.Warning);
            }
        }

        private void OnBackgroundSelected(long index)
        {
            if (index < 0 || index >= _backgroundIds.Count)
                return;

            _canvas.SetBackground(ModResourceService.LoadBackgroundTexture(_backgroundIds[(int)index]));
        }

        // ── zone structure actions ─────────────────────────────────

        private void OnAddRegion()
        {
            _controller.StartRegion();
            _canvas.SetMode(MapEditorCanvas.ModeDraw);
            SyncModeButtons();
            SetDirty();
            RefreshAll();
            SetStatus(ToolUi.Tr("tools/map_editor/draw_started"));
        }

        private void OnAddMarker()
        {
            _controller.AddMarker();
            _canvas.FinishDrawing();
            SetDirty();
            RefreshAll();
        }

        private void OnAddLabel()
        {
            _controller.AddLabel();
            _canvas.FinishDrawing();
            SetDirty();
            RefreshAll();
        }

        private void OnDeleteZone()
        {
            var zone = _controller.Selected;
            if (zone == null)
            {
                SetStatus(ToolUi.Tr("tools/map_editor/zone_missing"), ToolStatusKind.Error);
                return;
            }

            var index = _controller.SelectedIndex;
            ShowConfirm("tools/map_editor/delete", "tools/map_editor/delete_confirm", () =>
            {
                _controller.RemoveAt(index);
                _canvas.FinishDrawing();
                SetDirty();
                RefreshAll();
                SetStatus(string.Format(ToolUi.Tr("tools/map_editor/deleted"), zone.ID));
            });
        }

        private void OnZoneChosen(string indexText)
        {
            if (_suppress || !int.TryParse(indexText, out var index))
                return;

            _canvas.FinishDrawing();
            _controller.Select(index);
            RefreshAll();
        }

        private void OnCanvasZonePicked(int index)
        {
            RefreshSelectionOnly();
        }

        private void OnCanvasChanged()
        {
            SetDirty();
            RefreshValuesOnly();
        }

        private void SyncModeButtons()
        {
            _suppress = true;
            if (_canvas.Mode == MapEditorCanvas.ModeDraw)
                _drawModeBtn.ButtonPressed = true;
            else
                _selectModeBtn.ButtonPressed = true;
            _suppress = false;
        }

        private void OnAddVertex()
        {
            var zone = _controller.Selected;
            if (zone == null)
            {
                SetStatus(ToolUi.Tr("tools/map_editor/zone_missing"), ToolStatusKind.Error);
                return;
            }

            _controller.AddVertex(zone.Points != null && zone.Points.Count > 0
                ? zone.Points[zone.Points.Count - 1] + new Vector2(0.02f, 0.02f)
                : new Vector2(0.5f, 0.5f));
            SetDirty();
            RefreshAll();
        }

        private void RemoveVertex(int index)
        {
            _controller.RemoveVertex(index);
            SetDirty();
            RefreshAll();
        }

        // ── property editors ───────────────────────────────────────

        private void OnIdChanged()
        {
            if (_suppress)
                return;

            var zone = _controller.Selected;
            if (zone == null)
                return;

            var id = _idEdit.Text.Trim();
            if (ValidateId(id, out var error))
            {
                zone.ID = id;
                SetDirty();
                SetStatus(string.Empty);
                _idEdit.RemoveThemeColorOverride("font_color");
            }
            else
            {
                SetStatus(error, ToolStatusKind.Error);
                _idEdit.AddThemeColorOverride("font_color", UiTokens.DangerText);
            }

            RefreshJson();
            RefreshZoneList();
        }

        private bool ValidateId(string id, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(id))
            {
                error = ToolUi.Tr("tools/map_editor/id_empty");
                return false;
            }

            foreach (var zone in _controller.Zones)
            {
                if (!ReferenceEquals(zone, _controller.Selected)
                    && string.Equals(zone.ID, id, StringComparison.OrdinalIgnoreCase))
                {
                    error = ToolUi.Tr("tools/map_editor/duplicate_id");
                    return false;
                }
            }

            return true;
        }

        private void OnTitleChanged()
        {
            if (_suppress || _controller.Selected == null)
                return;

            _controller.Selected.Title = _titleEdit.Text.Trim();
            UpdateTitlePreview();
            SetDirty();
            RefreshJson();
            _canvas.QueueRedraw();
        }

        private void OnFieldsChanged()
        {
            if (_suppress)
                return;

            var zone = _controller.Selected;
            if (zone == null)
                return;

            zone.Scene = _sceneSelect.Selected <= 0 ? string.Empty : _sceneIds[_sceneSelect.Selected - 1];
            zone.Description = _descEdit.Text.Trim();
            zone.Locked = _lockedCheck.ButtonPressed;
            zone.Hidden = _hiddenCheck.ButtonPressed;

            SetDirty();
            RefreshJson();
            UpdateAvailability(zone);
            _canvas.QueueRedraw();
        }

        private void OnImageChanged()
        {
            if (_suppress || _controller.Selected == null)
                return;

            _controller.Selected.Image = _imageEdit.Text.Trim();
            _iconList.Select(_controller.Selected.Image);
            AfterImageChanged();
        }

        private void OnIconChosen(string id)
        {
            if (_suppress || _controller.Selected == null || id == null)
                return;

            _controller.Selected.Image = id;
            _suppress = true;
            _imageEdit.Text = id;
            _suppress = false;
            AfterImageChanged();
        }

        private void AfterImageChanged()
        {
            UpdateImagePreview(_controller.Selected);
            SetDirty();
            _canvas.RebuildHandles();
            _canvas.QueueRedraw();
            RefreshJson();
        }

        private void OnPositionChanged()
        {
            if (_suppress)
                return;

            var zone = _controller.Selected;
            if (zone == null)
                return;

            zone.ImagePos = new Vector2((float)_posX.Value, (float)_posY.Value);
            if (zone.LabelPos.HasValue)
                zone.LabelPos = new Vector2((float)_labelX.Value, (float)_labelY.Value);

            SetDirty();
            _canvas.RebuildHandles();
            _canvas.QueueRedraw();
            RefreshJson();
        }

        private void OnLabelToggled(bool enabled)
        {
            if (_suppress)
                return;

            _controller.SetLabelEnabled(enabled);
            SetDirty();
            RefreshValuesOnly();
            _canvas.RebuildHandles();
            _canvas.QueueRedraw();
        }

        // ── refresh ────────────────────────────────────────────────

        private void RefreshAll()
        {
            RefreshZoneList();
            RefreshValuesOnly();
            _canvas.RebuildHandles();
            _canvas.QueueRedraw();
        }

        private void RefreshSelectionOnly()
        {
            RefreshZoneList();
            RefreshValuesOnly();
        }

        private void RefreshValuesOnly()
        {
            _suppress = true;
            try
            {
                WriteSidebarFromController();
            }
            finally
            {
                _suppress = false;
            }

            RefreshVertexList();
            RefreshJson();
        }

        /// <summary>Zone rows are "kind glyph + id"; the row id is the zone index.</summary>
        private void RefreshZoneList()
        {
            var items = new List<(string, string, Texture2D)>();
            for (var i = 0; i < _controller.Zones.Count; i++)
            {
                var zone = _controller.Zones[i];
                var id = string.IsNullOrEmpty(zone.ID) ? "?" : zone.ID;
                items.Add((i.ToString(), KindGlyph(zone) + "  " + id, null));
            }

            _zoneList.SetItems(items);
            _zoneList.Select(_controller.SelectedIndex >= 0 ? _controller.SelectedIndex.ToString() : null);
            _deleteBtn.Disabled = _controller.Selected == null;
        }

        private static string KindGlyph(MapZone zone)
        {
            if (zone.Points != null && zone.Points.Count > 0)
                return "▱";
            if (!string.IsNullOrEmpty(zone.Image) || !zone.LabelPos.HasValue)
                return "◆";
            return "T";
        }

        private void WriteSidebarFromController()
        {
            var zone = _controller.Selected;
            var hasZone = zone != null;

            _propsEmpty.Visible = !hasZone;
            _propsBody.Visible = hasZone;

            if (!hasZone)
                return;

            _idEdit.Text = zone.ID ?? string.Empty;
            _idEdit.RemoveThemeColorOverride("font_color");
            _titleEdit.Text = zone.Title ?? string.Empty;
            _descEdit.Text = zone.Description ?? string.Empty;
            _imageEdit.Text = zone.Image ?? string.Empty;
            _lockedCheck.ButtonPressed = zone.Locked;
            _hiddenCheck.ButtonPressed = zone.Hidden;
            _labelCheck.ButtonPressed = zone.LabelPos.HasValue;
            _labelX.Editable = zone.LabelPos.HasValue;
            _labelY.Editable = zone.LabelPos.HasValue;

            var sceneIndex = _sceneIds.FindIndex(id => string.Equals(id, zone.Scene, StringComparison.OrdinalIgnoreCase));
            _sceneSelect.Select(sceneIndex >= 0 ? sceneIndex + 1 : 0);

            _iconList.Select(zone.Image);
            UpdateImagePreview(zone);

            _posX.Value = zone.ImagePos.X;
            _posY.Value = zone.ImagePos.Y;
            var labelPos = zone.LabelPos ?? Vector2.Zero;
            _labelX.Value = labelPos.X;
            _labelY.Value = labelPos.Y;

            UpdateTitlePreview();
            UpdateAvailability(zone);
            ExpandRelevantSections(zone);
        }

        /// <summary>
        /// On a new selection expands only the sections that matter for the
        /// zone's kind (polygon → vertices, marker → icon, label → label); the
        /// author may open the others by hand, which sticks until the next pick.
        /// </summary>
        private void ExpandRelevantSections(MapZone zone)
        {
            if (ReferenceEquals(zone, _sectionsShownFor))
                return;

            _sectionsShownFor = zone;
            var hasPolygon = zone.Points is { Count: > 0 } || _canvas.Mode == MapEditorCanvas.ModeDraw;
            _vertexSection.Expanded = hasPolygon;
            _labelSection.Expanded = zone.LabelPos.HasValue;
            _markerSection.Expanded = !string.IsNullOrEmpty(zone.Image) || (!hasPolygon && !zone.LabelPos.HasValue);
        }

        private void UpdateImagePreview(MapZone zone)
        {
            _imagePreview.Texture = zone == null || string.IsNullOrEmpty(zone.Image) ? null : Textures.Resolve(zone.Image);
        }

        /// <summary>
        /// Shows the availability the current authoring data produces in the
        /// game. The sidebar flags alone are not the whole truth: the player
        /// also refuses to travel to a zone with an empty or unregistered
        /// scene, so the hint goes through the shared <see cref="MapZoneRules"/>
        /// and the editor cannot disagree with the map the player sees.
        /// </summary>
        private void UpdateAvailability(MapZone zone)
        {
            if (zone == null)
            {
                _availabilityLabel.Text = string.Empty;
                return;
            }

            string state;
            var color = UiTokens.DangerText;
            if (zone.Hidden)
                state = ToolUi.Tr("tools/map_editor/effective_hidden");
            else if (zone.Locked)
                state = ToolUi.Tr("tools/map_editor/effective_locked");
            else if (string.IsNullOrEmpty(zone.Scene))
                state = ToolUi.Tr("tools/map_editor/effective_no_scene");
            else if (MapZoneRules.IsLocked(zone, true))
                state = ToolUi.Tr("tools/map_editor/effective_unknown_scene");
            else
            {
                state = ToolUi.Tr("tools/map_editor/effective_open");
                color = UiTokens.Ok;
            }

            _availabilityLabel.Text = $"{ToolUi.Tr("tools/map_editor/effective")} {state}";
            _availabilityLabel.AddThemeColorOverride("font_color", color);
        }

        private void UpdateTitlePreview()
        {
            var key = _titleEdit.Text.Trim();
            if (string.IsNullOrEmpty(key))
            {
                _titlePreview.Text = string.Empty;
                return;
            }

            var translated = TranslationServer.Translate(key).ToString();
            _titlePreview.Text = string.Equals(translated, key, StringComparison.Ordinal)
                ? ToolUi.Tr("tools/map_editor/no_translation")
                : "« " + translated + " »";
        }

        /// <summary>
        /// One row per vertex with X/Y spin boxes. Handlers read the vertex at
        /// change time — capturing the original point made editing Y revert X.
        /// </summary>
        private void RefreshVertexList()
        {
            foreach (var child in _vertexList.GetChildren())
                child.QueueFree();

            var zone = _controller.Selected;
            if (zone?.Points == null)
                return;

            for (var i = 0; i < zone.Points.Count; i++)
            {
                var index = i;
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", UiTokens.Space1);

                var number = UiKit.Label((i + 1).ToString(), UiTokens.FontSmall, UiTokens.Faint);
                number.CustomMinimumSize = new Vector2(20, 0);
                row.AddChild(number);

                var xSpin = ToolUi.Number(0, 1, 0.001, null);
                xSpin.Value = zone.Points[i].X;
                xSpin.ValueChanged += value => OnVertexEdited(index, (float)value, null);
                row.AddChild(xSpin);

                var ySpin = ToolUi.Number(0, 1, 0.001, null);
                ySpin.Value = zone.Points[i].Y;
                ySpin.ValueChanged += value => OnVertexEdited(index, null, (float)value);
                row.AddChild(ySpin);

                var deleteBtn = UiKit.IconButton("✕", ToolUi.Tr("tools/map_editor/delete_vertex"));
                deleteBtn.Pressed += () => RemoveVertex(index);
                row.AddChild(deleteBtn);

                _vertexList.AddChild(row);
            }
        }

        private void OnVertexEdited(int index, float? x, float? y)
        {
            var zone = _controller.Selected;
            if (zone?.Points == null || index >= zone.Points.Count)
                return;

            var current = zone.Points[index];
            _controller.SetVertex(index, new Vector2(x ?? current.X, y ?? current.Y));
            SetDirty();
            _canvas.RebuildHandles();
            _canvas.QueueRedraw();
            RefreshJson();
        }

        private void RefreshJson()
        {
            _jsonEdit.Text = _controller.ToJson();
        }
    }
}
