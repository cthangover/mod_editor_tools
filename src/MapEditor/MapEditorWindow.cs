using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cthangover.Core.Localization;
using Cthangover.Core.Mods.Resolvers;
using Cthangover.Core.Scenes;
using Cthangover.Core.Settings;
using Cthangover.Core.UI;
using Cthangover.Core.UI.Theming;
using Cthangover.Mods.Map;
using Cthangover.Core.UI.Tool;
using Cthangover.Core.Utils;
using Cthangover.Tools.Services;
using Godot;

using Cthangover.Mods.Interface.Tool;
namespace Cthangover.Tools.MapEditor
{
    /// <summary>
    /// Editor window for the world map. Authors pick a folder-backed mod and a
    /// file under its <c>map/</c> directory, then draw polygons, place icon
    /// markers and drop free-standing text labels over a background preview.
    /// <para/>
    /// The editor edits raw authoring data only: the produced JSON matches
    /// <c>MapZone</c> (polygon <c>points</c>, marker <c>image</c>/<c>image_pos</c>,
    /// label <c>label_pos</c>, initial <c>locked</c>/<c>hidden</c> flags).
    /// Runtime state — what scenarios open or reveal — is not touched; after a
    /// successful save the editor asks the running map widget to reload so the
    /// edit is visible immediately.
    /// <para/>
    /// Layout mirrors the other tool windows: toolbar for file and mode actions,
    /// sidebar with the zone list and property editors, centre preview, and a
    /// read-only JSON preview at the bottom for quick inspection.
    /// </summary>
    public partial class MapEditorWindow : ToolWindow
    {
        private readonly MapEditorController _controller = new();

        private readonly List<string> _writableMods = new();
        private readonly List<string> _backgroundIds = new();
        private readonly List<string> _sceneIds = new();
        private readonly List<string> _textureIds = new();

        private OptionButton _modSelect;
        private LineEdit _fileEdit;
        private OptionButton _modeSelect;
        private OptionButton _backgroundSelect;
        private Label _statusLabel;

        private ItemList _zoneList;
        private LineEdit _idEdit;
        private LineEdit _titleEdit;
        private Label _titlePreview;
        private OptionButton _sceneSelect;
        private LineEdit _descEdit;
        private LineEdit _imageEdit;
        private ItemList _iconList;
        private CheckBox _lockedCheck;
        private CheckBox _hiddenCheck;
        private Label _availabilityLabel;
        private CheckBox _labelCheck;
        private LineEdit _posX;
        private LineEdit _posY;
        private LineEdit _labelX;
        private LineEdit _labelY;
        private VBoxContainer _vertexList;

        private MapEditorCanvas _canvas;
        private TextEdit _jsonEdit;
        private Button _previewButton;
        private MapWidget _preview;

        private bool _suppress;

        public MapEditorWindow() : base("tools/map_editor/title")
        {
            Size = new Vector2I(1280, 820);
            CthangoverTheme.Apply(this);

            ReloadLocalization();
            BuildUI();
            LoadModList();
            LoadSceneList();
            LoadTextureList();
            LoadBackgroundList();
            RefreshAll();
        }

        // ── UI construction ────────────────────────────────────────

        private void BuildUI()
        {
            var outer = CreateFillContainer();
            AddChild(outer);

            outer.AddChild(BuildToolbar());

            var main = new HBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            outer.AddChild(main);

            var sidebarContent = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            main.AddChild(CreateSidebar(sidebarContent, 320));
            BuildSidebar(sidebarContent);

            var previewPanel = new PanelContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            main.AddChild(previewPanel);

            _canvas = new MapEditorCanvas { Controller = _controller };
            _canvas.Changed += OnCanvasChanged;
            _canvas.ZonePicked += OnCanvasZonePicked;
            previewPanel.AddChild(_canvas);

            _jsonEdit = new TextEdit
            {
                CustomMinimumSize = new Vector2(0, 150),
                Editable = false,
                WrapMode = TextEdit.LineWrappingMode.Boundary
            };
            _jsonEdit.AddThemeFontOverride("font", GetMonospaceFont());
            outer.AddChild(_jsonEdit);
        }

        private HBoxContainer BuildToolbar()
        {
            var toolbar = CreateToolbar();

            toolbar.AddChild(new Label { Text = Tr("tools/map_editor/mod") });
            _modSelect = new OptionButton();
            _modSelect.ItemSelected += _ => OnFileChanged();
            toolbar.AddChild(_modSelect);

            toolbar.AddChild(new Label { Text = Tr("tools/map_editor/file") });
            _fileEdit = new LineEdit { Text = "map.json", CustomMinimumSize = new Vector2(160, 0) };
            toolbar.AddChild(_fileEdit);

            var loadBtn = new Button { Text = Tr("tools/map_editor/load") };
            loadBtn.Pressed += OnLoad;
            toolbar.AddChild(loadBtn);

            var saveBtn = new Button { Text = Tr("tools/map_editor/save") };
            saveBtn.Pressed += OnSave;
            toolbar.AddChild(saveBtn);

            var reloadBtn = new Button { Text = Tr("tools/map_editor/reload_map") };
            reloadBtn.Pressed += OnReloadMap;
            toolbar.AddChild(reloadBtn);

            _modeSelect = new OptionButton();
            _modeSelect.AddItem(Tr("tools/map_editor/select_mode"));
            _modeSelect.AddItem(Tr("tools/map_editor/draw_region"));
            _modeSelect.ItemSelected += index => _canvas.SetMode(index == 1 ? MapEditorCanvas.ModeDraw : MapEditorCanvas.ModeSelect);
            toolbar.AddChild(_modeSelect);

            toolbar.AddChild(new Label { Text = Tr("tools/map_editor/background") });
            _backgroundSelect = new OptionButton();
            _backgroundSelect.ItemSelected += OnBackgroundSelected;
            toolbar.AddChild(_backgroundSelect);

            _previewButton = new Button { Text = Tr("tools/map_editor/preview") };
            _previewButton.TooltipText = Tr("tools/map_editor/preview_tip");
            _previewButton.Pressed += OnPreviewPressed;
            toolbar.AddChild(_previewButton);

            _statusLabel = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Right };
            _statusLabel.AddThemeColorOverride("font_color", UiTokens.Muted);
            toolbar.AddChild(_statusLabel);

            return toolbar;
        }

        private void BuildSidebar(VBoxContainer sidebar)
        {
            sidebar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

            sidebar.AddChild(new Label { Text = Tr("tools/map_editor/zones") });

            _zoneList = new ItemList { CustomMinimumSize = new Vector2(0, 140) };
            _zoneList.ItemSelected += OnZoneSelected;
            sidebar.AddChild(_zoneList);

            var addRow = new HBoxContainer();
            sidebar.AddChild(addRow);
            AddButton(addRow, "tools/map_editor/add_region", OnAddRegion);
            AddButton(addRow, "tools/map_editor/add_marker", OnAddMarker);
            AddButton(addRow, "tools/map_editor/add_label", OnAddLabel);

            var deleteBtn = new Button { Text = Tr("tools/map_editor/delete") };
            deleteBtn.Pressed += OnDeleteZone;
            sidebar.AddChild(deleteBtn);

            sidebar.AddChild(new Label { Text = Tr("tools/map_editor/props") });

            sidebar.AddChild(new Label { Text = Tr("tools/map_editor/id") });
            _idEdit = new LineEdit();
            _idEdit.TextChanged += _ => OnIdChanged();
            sidebar.AddChild(_idEdit);

            sidebar.AddChild(new Label { Text = Tr("tools/map_editor/title_key") });
            _titleEdit = new LineEdit();
            _titleEdit.TextChanged += _ => OnTitleChanged();
            sidebar.AddChild(_titleEdit);

            _titlePreview = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
            _titlePreview.AddThemeColorOverride("font_color", UiTokens.Faint);
            sidebar.AddChild(_titlePreview);

            sidebar.AddChild(new Label { Text = Tr("tools/map_editor/scene") });
            _sceneSelect = new OptionButton();
            _sceneSelect.ItemSelected += _ => OnFieldsChanged();
            sidebar.AddChild(_sceneSelect);

            sidebar.AddChild(new Label { Text = Tr("tools/map_editor/description") });
            _descEdit = new LineEdit();
            _descEdit.TextChanged += _ => OnFieldsChanged();
            sidebar.AddChild(_descEdit);

            sidebar.AddChild(new Label { Text = Tr("tools/map_editor/image") });
            _imageEdit = new LineEdit();
            _imageEdit.TextChanged += _ => OnImageChanged();
            sidebar.AddChild(_imageEdit);

            _iconList = new ItemList { CustomMinimumSize = new Vector2(0, 100) };
            _iconList.ItemSelected += OnIconSelected;
            sidebar.AddChild(_iconList);

            sidebar.AddChild(new Label { Text = Tr("tools/map_editor/image_pos") });
            var posRow = new HBoxContainer();
            sidebar.AddChild(posRow);
            _posX = AddLabeledLine(posRow, "tools/map_editor/x");
            _posY = AddLabeledLine(posRow, "tools/map_editor/y");
            _posX.TextChanged += _ => OnPositionChanged();
            _posY.TextChanged += _ => OnPositionChanged();

            _labelCheck = new CheckBox { Text = Tr("tools/map_editor/label") };
            _labelCheck.Toggled += OnLabelToggled;
            sidebar.AddChild(_labelCheck);

            var labelRow = new HBoxContainer();
            sidebar.AddChild(labelRow);
            _labelX = AddLabeledLine(labelRow, "tools/map_editor/x");
            _labelY = AddLabeledLine(labelRow, "tools/map_editor/y");
            _labelX.TextChanged += _ => OnPositionChanged();
            _labelY.TextChanged += _ => OnPositionChanged();

            _lockedCheck = new CheckBox { Text = Tr("tools/map_editor/locked") };
            _lockedCheck.Toggled += _ => OnFieldsChanged();
            sidebar.AddChild(_lockedCheck);

            _hiddenCheck = new CheckBox { Text = Tr("tools/map_editor/hidden") };
            _hiddenCheck.Toggled += _ => OnFieldsChanged();
            sidebar.AddChild(_hiddenCheck);

            _availabilityLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
            _availabilityLabel.AddThemeColorOverride("font_color", UiTokens.Faint);
            sidebar.AddChild(_availabilityLabel);

            sidebar.AddChild(new Label { Text = Tr("tools/map_editor/vertices") });

            var vertexButtons = new HBoxContainer();
            sidebar.AddChild(vertexButtons);
            var addVertexBtn = new Button { Text = Tr("tools/map_editor/add_vertex") };
            addVertexBtn.Pressed += OnAddVertex;
            vertexButtons.AddChild(addVertexBtn);

            _vertexList = new VBoxContainer();
            sidebar.AddChild(_vertexList);
        }

        private static Button AddButton(Container parent, string localeKey, Action pressed)
        {
            var button = new Button { Text = Tr(localeKey) };
            button.Pressed += pressed;
            parent.AddChild(button);
            return button;
        }

        private static LineEdit AddLabeledLine(Container parent, string localeKey)
        {
            var label = new Label { Text = Tr(localeKey), CustomMinimumSize = new Vector2(20, 0) };
            parent.AddChild(label);

            var edit = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            parent.AddChild(edit);
            return edit;
        }

        // ── data loading ───────────────────────────────────────────

        private void LoadModList()
        {
            _writableMods.Clear();
            _modSelect.Clear();

            _writableMods.AddRange(MapFileService.ListWritableMods());
            foreach (var modId in _writableMods)
                _modSelect.AddItem(modId);

            if (_writableMods.Count == 0)
                SetStatus(Tr("tools/map_editor/no_mods"));
        }

        private void LoadSceneList()
        {
            _sceneIds.Clear();
            _sceneSelect.Clear();
            _sceneSelect.AddItem(Tr("tools/map_editor/scene_none"));

            var defs = SceneDefinitions.Collect();
            if (defs != null)
                _sceneIds.AddRange(defs.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase));

            foreach (var scene in _sceneIds)
                _sceneSelect.AddItem(scene);
        }

        private void LoadTextureList()
        {
            _textureIds.Clear();
            _iconList.Clear();

            _textureIds.AddRange(ModResourceService.GetTextureIds());
            foreach (var id in _textureIds)
                _iconList.AddItem(id);
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

        private string SelectedMod => _modSelect.Selected >= 0 && _modSelect.Selected < _writableMods.Count
            ? _writableMods[_modSelect.Selected]
            : null;

        private string SelectedFileName
        {
            get
            {
                var name = _fileEdit.Text.Trim().Replace('\\', '/');
                if (string.IsNullOrEmpty(name))
                    name = "map.json";
                if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    name += ".json";
                return name;
            }
        }

        private void OnFileChanged()
        {
            OnLoad();
        }

        // ── toolbar actions ────────────────────────────────────────

        private void OnLoad()
        {
            var modId = SelectedMod;
            if (modId == null)
            {
                SetStatus(Tr("tools/map_editor/no_mods"));
                return;
            }

            var path = "map/" + SelectedFileName;
            try
            {
                var text = MapFileService.ReadFile(modId, path);
                if (string.IsNullOrEmpty(text))
                {
                    _controller.Clear();
                    MarkClean();
                    SetStatus(Tr("tools/map_editor/no_file"));
                }
                else
                {
                    _controller.FromJson(text);
                    MarkClean();
                    SetStatus(path);
                }
            }
            catch (Exception ex)
            {
                SetStatus($"{Tr("tools/map_editor/load_error")}: {ex.Message}");
                GameLogger.Log("MAP_EDITOR", $"load failed for '{modId}/{path}': {ex.Message}", LogLevel.Error);
            }

            RefreshAll();
        }

        private void OnSave()
        {
            var zone = _controller.Selected;
            if (zone != null && !ValidateId(zone.ID, out var error))
            {
                SetStatus(error);
                return;
            }

            var modId = SelectedMod;
            if (modId == null || !MapFileService.WriteFile(modId, "map/" + SelectedFileName, _controller.ToJson()))
            {
                SetStatus(Tr("tools/map_editor/readonly"));
                return;
            }

            MarkClean();
            SetStatus(Tr("tools/map_editor/save_ok"));
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
            SetStatus(Tr("tools/map_editor/reloaded"));
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
                SetStatus(Tr("tools/map_editor/preview_missing"));
                return false;
            }

            widget.SetPreviewZones(_controller.Zones);
            widget.VisibilityChanged += OnPreviewVisibilityChanged;
            AddChild(widget);
            widget.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _preview = widget;

            widget.EnsureConstructed();
            widget.Show();
            SetStatus(Tr("tools/map_editor/preview_on"));
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
            SetStatus(Tr("tools/map_editor/preview_off"));
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
                GameLogger.Log("MAP_EDITOR", "localisation reloaded");
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
            _controller.AddRegion();
            _modeSelect.Select(1);
            _canvas.SetMode(MapEditorCanvas.ModeDraw);
            SetDirty();
            RefreshAll();
        }

        private void OnAddMarker()
        {
            _controller.AddMarker();
            _modeSelect.Select(0);
            _canvas.SetMode(MapEditorCanvas.ModeSelect);
            SetDirty();
            RefreshAll();
        }

        private void OnAddLabel()
        {
            _controller.AddLabel();
            _modeSelect.Select(0);
            _canvas.SetMode(MapEditorCanvas.ModeSelect);
            SetDirty();
            RefreshAll();
        }

        private void OnDeleteZone()
        {
            if (_controller.SelectedIndex < 0)
            {
                SetStatus(Tr("tools/map_editor/zone_missing"));
                return;
            }

            _controller.RemoveAt(_controller.SelectedIndex);
            SetDirty();
            RefreshAll();
        }

        private void OnZoneSelected(long index)
        {
            if (_suppress)
                return;

            _controller.Select((int)index);
            RefreshAll();
        }

        private void OnCanvasZonePicked(int index)
        {
            if (_suppress)
                return;

            _controller.Select(index);
            RefreshSelectionOnly();
        }

        private void OnCanvasChanged()
        {
            if (_suppress)
                return;

            SetDirty();
            RefreshValuesOnly();
        }

        private void OnAddVertex()
        {
            var zone = _controller.Selected;
            if (zone == null)
            {
                SetStatus(Tr("tools/map_editor/zone_missing"));
                return;
            }

            _controller.AddVertex(zone.Points != null && zone.Points.Count > 0
                ? zone.Points[zone.Points.Count - 1]
                : new Godot.Vector2(0.5f, 0.5f));
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
            }
            else
            {
                SetStatus(error);
            }

            RefreshJson();
            UpdateZoneListLabel();
        }

        private bool ValidateId(string id, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(id))
            {
                error = Tr("tools/map_editor/id_empty");
                return false;
            }

            foreach (var zone in _controller.Zones)
            {
                if (!ReferenceEquals(zone, _controller.Selected)
                    && string.Equals(zone.ID, id, StringComparison.OrdinalIgnoreCase))
                {
                    error = Tr("tools/map_editor/duplicate_id");
                    return false;
                }
            }

            return true;
        }

        private void OnTitleChanged()
        {
            if (_suppress)
                return;

            var zone = _controller.Selected;
            if (zone == null)
                return;

            zone.Title = _titleEdit.Text.Trim();
            UpdateTitlePreview();
            SetDirty();
            RefreshJson();
        }

        private void OnFieldsChanged()
        {
            if (_suppress)
                return;

            var zone = _controller.Selected;
            if (zone == null)
                return;

            zone.Scene = _sceneSelect.Selected <= 0 ? string.Empty : _sceneIds[(int)_sceneSelect.Selected - 1];
            zone.Description = _descEdit.Text.Trim();
            zone.Locked = _lockedCheck.ButtonPressed;
            zone.Hidden = _hiddenCheck.ButtonPressed;

            SetDirty();
            RefreshJson();
            UpdateZoneListLabel();
            UpdateAvailability(zone);
            _canvas.QueueRedraw();
        }

        private void OnImageChanged()
        {
            if (_suppress)
                return;

            var zone = _controller.Selected;
            if (zone == null)
                return;

            zone.Image = _imageEdit.Text.Trim();
            SetDirty();
            _canvas.RebuildHandles();
            _canvas.QueueRedraw();
            RefreshJson();
        }

        private void OnIconSelected(long index)
        {
            if (_suppress || index < 0 || index >= _textureIds.Count)
                return;

            var zone = _controller.Selected;
            if (zone == null)
                return;

            zone.Image = _textureIds[(int)index];
            _imageEdit.Text = zone.Image;
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

            if (TryParseFloat(_posX.Text, out var mx) && TryParseFloat(_posY.Text, out var my))
                zone.ImagePos = Clamp01(new Godot.Vector2(mx, my));

            if (zone.LabelPos.HasValue
                && TryParseFloat(_labelX.Text, out var lx)
                && TryParseFloat(_labelY.Text, out var ly))
            {
                zone.LabelPos = Clamp01(new Godot.Vector2(lx, ly));
            }

            SetDirty();
            _canvas.QueueRedraw();
            RefreshJson();
        }

        private void OnLabelToggled(bool enabled)
        {
            if (_suppress)
                return;

            _controller.SetLabelEnabled(enabled);
            _labelX.Editable = enabled;
            _labelY.Editable = enabled;
            _suppress = true;
            try
            {
                WritePositionsToSidebar(_controller.Selected);
            }
            finally
            {
                _suppress = false;
            }
            SetDirty();
            _canvas.RebuildHandles();
            _canvas.QueueRedraw();
            RefreshJson();
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

        private void RefreshZoneList()
        {
            _suppress = true;
            try
            {
                _zoneList.Clear();
                foreach (var zone in _controller.Zones)
                    _zoneList.AddItem(string.IsNullOrEmpty(zone.ID) ? "?" : zone.ID);

                if (_controller.SelectedIndex >= 0)
                    _zoneList.Select(_controller.SelectedIndex);
            }
            finally
            {
                _suppress = false;
            }
        }

        private void UpdateZoneListLabel()
        {
            var index = _controller.SelectedIndex;
            if (index < 0 || index >= _zoneList.ItemCount)
                return;

            _zoneList.SetItemText(index, _controller.Selected?.ID ?? "?");
        }

        private void WriteSidebarFromController()
        {
            var zone = _controller.Selected;
            var hasZone = zone != null;

            _idEdit.Editable = hasZone;
            _titleEdit.Editable = hasZone;
            _descEdit.Editable = hasZone;
            _imageEdit.Editable = hasZone;
            _sceneSelect.Disabled = !hasZone;
            _lockedCheck.Disabled = !hasZone;
            _hiddenCheck.Disabled = !hasZone;
            _labelCheck.Disabled = !hasZone;
            _posX.Editable = hasZone;
            _posY.Editable = hasZone;
            _labelX.Editable = hasZone && zone.LabelPos.HasValue;
            _labelY.Editable = hasZone && zone.LabelPos.HasValue;

            if (!hasZone)
            {
                _idEdit.Text = string.Empty;
                _titleEdit.Text = string.Empty;
                _descEdit.Text = string.Empty;
                _imageEdit.Text = string.Empty;
                _titlePreview.Text = string.Empty;
                _posX.Text = string.Empty;
                _posY.Text = string.Empty;
                _labelX.Text = string.Empty;
                _labelY.Text = string.Empty;
                _labelCheck.ButtonPressed = false;
                _availabilityLabel.Text = string.Empty;
                return;
            }

            _idEdit.Text = zone.ID ?? string.Empty;
            _titleEdit.Text = zone.Title ?? string.Empty;
            _descEdit.Text = zone.Description ?? string.Empty;
            _imageEdit.Text = zone.Image ?? string.Empty;
            _lockedCheck.ButtonPressed = zone.Locked;
            _hiddenCheck.ButtonPressed = zone.Hidden;
            _labelCheck.ButtonPressed = zone.LabelPos.HasValue;

            var sceneIndex = _sceneIds.FindIndex(id => string.Equals(id, zone.Scene, StringComparison.OrdinalIgnoreCase));
            _sceneSelect.Select(sceneIndex >= 0 ? sceneIndex + 1 : 0);

            var iconIndex = _textureIds.FindIndex(id => string.Equals(id, zone.Image, StringComparison.OrdinalIgnoreCase));
            if (iconIndex >= 0)
                _iconList.Select(iconIndex);

            WritePositionsToSidebar(zone);
            UpdateTitlePreview();
            UpdateAvailability(zone);
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
            if (zone.Hidden)
                state = Tr("tools/map_editor/effective_hidden");
            else if (zone.Locked)
                state = Tr("tools/map_editor/effective_locked");
            else if (string.IsNullOrEmpty(zone.Scene))
                state = Tr("tools/map_editor/effective_no_scene");
            else if (MapZoneRules.IsLocked(zone, true))
                state = Tr("tools/map_editor/effective_unknown_scene");
            else
                state = Tr("tools/map_editor/effective_open");

            _availabilityLabel.Text = $"{Tr("tools/map_editor/effective")} {state}";
        }

        private void WritePositionsToSidebar(MapZone zone)
        {
            if (zone == null)
                return;

            _posX.Text = zone.ImagePos.X.ToString("F3", CultureInfo.InvariantCulture);
            _posY.Text = zone.ImagePos.Y.ToString("F3", CultureInfo.InvariantCulture);

            var labelPos = zone.LabelPos ?? Godot.Vector2.Zero;
            _labelX.Text = labelPos.X.ToString("F3", CultureInfo.InvariantCulture);
            _labelY.Text = labelPos.Y.ToString("F3", CultureInfo.InvariantCulture);
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
                ? Tr("tools/map_editor/no_translation")
                : translated;
        }

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
                var point = zone.Points[i];
                var row = new HBoxContainer();

                var xEdit = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                xEdit.Text = point.X.ToString("F3", CultureInfo.InvariantCulture);
                xEdit.TextChanged += text =>
                {
                    if (TryParseFloat(text, out var value))
                        _controller.SetVertex(index, new Godot.Vector2(value, point.Y));
                    OnVertexEdited();
                };
                row.AddChild(xEdit);

                var yEdit = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                yEdit.Text = point.Y.ToString("F3", CultureInfo.InvariantCulture);
                yEdit.TextChanged += text =>
                {
                    if (TryParseFloat(text, out var value))
                        _controller.SetVertex(index, new Godot.Vector2(point.X, value));
                    OnVertexEdited();
                };
                row.AddChild(yEdit);

                var deleteBtn = new Button { Text = "X", CustomMinimumSize = new Vector2(32, 0) };
                deleteBtn.Pressed += () => RemoveVertex(index);
                row.AddChild(deleteBtn);

                _vertexList.AddChild(row);
            }
        }

        private void OnVertexEdited()
        {
            SetDirty();
            _canvas.RebuildHandles();
            _canvas.QueueRedraw();
            RefreshJson();
        }

        private void RefreshJson()
        {
            _jsonEdit.Text = _controller.ToJson();
        }

        private void SetStatus(string text)
        {
            _statusLabel.Text = text ?? string.Empty;
        }

        private static string Tr(string key) => TranslationServer.Translate(key);

        private static bool TryParseFloat(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static Godot.Vector2 Clamp01(Godot.Vector2 value)
        {
            return new Godot.Vector2(Mathf.Clamp(value.X, 0f, 1f), Mathf.Clamp(value.Y, 0f, 1f));
        }
    }
}
