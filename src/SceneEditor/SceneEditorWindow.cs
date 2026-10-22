using System;
using System.Collections.Generic;
using System.Linq;
using Cthangover.Core.Audio;
using Cthangover.Core.Mods;
using Cthangover.Core.Scenes;
using Cthangover.Core.UI.Theming;
using Cthangover.Core.UI.Tool.ScenarioEditor;
using Cthangover.Mods.Dialog;
using Cthangover.Mods.Interface.Theming;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Tools.Common;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Tools.SceneEditor
{
    /// <summary>
    /// Editor of logical scenes — the places of a novel: which backgrounds a scene shows
    /// (one of them at random on every entry), its ambient sound, the scenario played
    /// when nothing else starts, and the scenarios that happen there. Left: every scene
    /// of the loaded mods; centre: the background preview, the scene's backgrounds and a
    /// searchable list of all backgrounds to add; right: properties and the scene's
    /// scenarios (open one in the scenario editor, or start a new one).
    /// <para/>
    /// A scene is the merge of the <c>scenes/&lt;name&gt;.json</c> files of all mods
    /// (backgrounds add up, a later ambient or default scenario wins). Scenarios are optional:
    /// a place may hold only interactives, so a new scene starts without any. The editor writes
    /// one of them — the file of the mod chosen in "Save to": the scene's own file when
    /// that mod defines it, otherwise a new file that adds to a foreign scene without
    /// touching the foreign mod. Backgrounds of the other files are shown locked, with
    /// their mod. After saving the game re-reads scenes (<see cref="SceneFileService.RefreshGame"/>),
    /// so a new scene can be entered at once with "Go to scene".
    /// </summary>
    public partial class SceneEditorWindow : ToolWindow
    {
        private const int ThumbWidth = 160;
        private const int ThumbHeight = 90;

        private readonly List<SceneFileEntry> _files = new();
        private readonly List<string> _writableMods = new();
        private readonly List<string> _sounds = new();
        private readonly List<string> _scenarioKeys = new();
        private readonly List<string> _defaultChoices = new();

        private string _sceneName;
        private bool _isNew;
        private SceneFileEntry _target;
        private string _preview;
        private bool _suppress;
        private bool _ambientPlaying;
        private string _pendingMod;
        private string _pendingScene;

        private FilteredList _sceneList;
        private TextureRect _previewRect;
        private Label _previewCaption;
        private HFlowContainer _backgroundRow;
        private FilteredList _picker;
        private Button _addBackgroundBtn;
        private LineEdit _nameEdit;
        private OptionButton _modSelect;
        private Label _modHint;
        private OptionButton _ambientSelect;
        private Button _ambientPlayBtn;
        private Label _ambientHint;
        private OptionButton _defaultSelect;
        private Label _defaultHint;
        private ItemList _scenarioList;
        private Button _openScenarioBtn;
        private Button _newScenarioBtn;
        private Button _deleteBtn;
        private Button _goBtn;

        public SceneEditorWindow() : base("tools/scene_editor/title")
        {
            CthangoverTheme.Apply(this);
            BuildUI();
            LoadSources();
            SetHint(ToolUi.Tr("tools/scene_editor/hint"));
            RefreshAll();
        }

        // ── UI construction ────────────────────────────────────────

        private void BuildUI()
        {
            var outer = CreateFillContainer();
            AddChild(outer);

            var toolbar = CreateToolbar();
            outer.AddChild(toolbar);
            toolbar.AddChild(ToolUi.Button("tools/common/save", Save, ButtonKind.Primary, "tools/scene_editor/save_tip"));
            toolbar.AddChild(ToolUi.Button("tools/scene_editor/new_scene", NewScene, ButtonKind.Secondary, "tools/scene_editor/new_scene_tip"));
            _deleteBtn = ToolUi.Button("tools/scene_editor/delete", AskDelete, ButtonKind.Danger, "tools/scene_editor/delete_tip");
            toolbar.AddChild(_deleteBtn);
            toolbar.AddChild(ToolUi.Divider());
            _goBtn = ToolUi.Button("tools/scene_editor/go", GoToScene, ButtonKind.Secondary, "tools/scene_editor/go_tip");
            toolbar.AddChild(_goBtn);
            toolbar.AddChild(UiKit.Spacer());
            toolbar.AddChild(ToolUi.Button("tools/scenario_editor/rescan", Rescan, ButtonKind.Secondary, "tools/scene_editor/rescan_tip"));

            var main = new HSplitContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            outer.AddChild(main);

            var left = new VBoxContainer();
            main.AddChild(CreateSidebar(left, 260));
            left.AddChild(ToolUi.Section("tools/scene_editor/scenes"));
            _sceneList = new FilteredList(200);
            _sceneList.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            _sceneList.Chosen += OnScenePicked;
            left.AddChild(_sceneList);

            var rest = new HSplitContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            main.AddChild(rest);
            rest.AddChild(BuildCentre());

            var right = new VBoxContainer();
            right.AddThemeConstantOverride("separation", UiTokens.Space2);
            rest.AddChild(CreateSidebar(right, 340));
            BuildProperties(right);
        }

        private Control BuildCentre()
        {
            var column = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            column.AddThemeConstantOverride("separation", UiTokens.Space2);

            var frame = new PanelContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 260)
            };
            frame.AddThemeStyleboxOverride("panel", UiKit.Frame(UiTokens.RadiusMd, UiTokens.Well, UiTokens.Line));
            column.AddChild(frame);
            _previewRect = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            frame.AddChild(_previewRect);
            _previewCaption = ToolUi.Hint(string.Empty);
            column.AddChild(_previewCaption);

            column.AddChild(ToolUi.Section("tools/scene_editor/backgrounds"));
            _backgroundRow = new HFlowContainer();
            _backgroundRow.AddThemeConstantOverride("h_separation", UiTokens.Space2);
            _backgroundRow.AddThemeConstantOverride("v_separation", UiTokens.Space2);
            column.AddChild(_backgroundRow);

            column.AddChild(ToolUi.Section("tools/scene_editor/all_backgrounds"));
            _picker = new FilteredList(120);
            _picker.Chosen += ShowPreview;
            _picker.Activated += AddBackground;
            column.AddChild(_picker);
            _addBackgroundBtn = ToolUi.Button("tools/scene_editor/add_background", AddPreviewed, ButtonKind.Secondary, "tools/scene_editor/add_background_tip");
            column.AddChild(_addBackgroundBtn);
            return column;
        }

        private void BuildProperties(VBoxContainer column)
        {
            column.AddChild(ToolUi.Section("tools/scene_editor/properties"));
            _nameEdit = ToolUi.Field(column, "tools/scene_editor/name", ToolUi.Text("my_scene", OnNameChanged), "tools/scene_editor/name_tip");
            _modSelect = ToolUi.Field(column, "tools/scene_editor/mod", new OptionButton(), "tools/scene_editor/mod_tip");
            _modSelect.ItemSelected += OnModPicked;
            _modHint = ToolUi.Hint(string.Empty);
            column.AddChild(_modHint);

            var ambientRow = new HBoxContainer();
            ambientRow.AddThemeConstantOverride("separation", UiTokens.Space1);
            _ambientSelect = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FitToLongestItem = false };
            _ambientSelect.ItemSelected += OnAmbientPicked;
            ambientRow.AddChild(_ambientSelect);
            _ambientPlayBtn = ToolUi.Button("tools/scene_editor/listen", ToggleAmbientPreview, ButtonKind.Secondary, "tools/scene_editor/listen_tip");
            ambientRow.AddChild(_ambientPlayBtn);
            ToolUi.Field(column, "tools/scene_editor/ambient", ambientRow, "tools/scene_editor/ambient_tip");
            _ambientHint = ToolUi.Hint(string.Empty);
            column.AddChild(_ambientHint);

            _defaultSelect = ToolUi.Field(column, "tools/scene_editor/default_scenario",
                new OptionButton { FitToLongestItem = false }, "tools/scene_editor/default_scenario_tip");
            _defaultSelect.ItemSelected += OnDefaultPicked;
            _defaultHint = ToolUi.Hint(string.Empty);
            column.AddChild(_defaultHint);

            column.AddChild(ToolUi.Section("tools/scene_editor/scenarios"));
            _scenarioList = new ItemList { CustomMinimumSize = new Vector2(0, 140), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
            _scenarioList.ItemActivated += OnScenarioActivated;
            column.AddChild(_scenarioList);

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", UiTokens.Space1);
            column.AddChild(row);
            _openScenarioBtn = ToolUi.Button("tools/scene_editor/open_scenario", OpenSelectedScenario, ButtonKind.Secondary, "tools/scene_editor/open_scenario_tip");
            _openScenarioBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(_openScenarioBtn);
            _newScenarioBtn = ToolUi.Button("tools/scene_editor/new_scenario", NewScenario, ButtonKind.Secondary, "tools/scene_editor/new_scenario_tip");
            _newScenarioBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(_newScenarioBtn);
        }

        // ── loading ────────────────────────────────────────────────

        private void LoadSources()
        {
            _files.Clear();
            _files.AddRange(SceneFileService.Scan());
            _writableMods.Clear();
            _writableMods.AddRange(ModFileService.ListWritableMods());
            _sounds.Clear();
            _sounds.AddRange(ModResourceService.GetSoundIds());

            _picker.SetItems(ModResourceService.GetBackgroundIds());
            _modSelect.Clear();
            foreach (var mod in _writableMods)
                _modSelect.AddItem(mod);
            _ambientSelect.Clear();
            _ambientSelect.AddItem(ToolUi.Tr("tools/scene_editor/none"));
            foreach (var sound in _sounds)
                _ambientSelect.AddItem(sound);
            RebuildSceneList();
        }

        private void RebuildSceneList()
        {
            var items = new List<(string Id, string Text, Texture2D Icon)>();
            foreach (var group in _files.GroupBy(f => f.Name).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
                items.Add((group.Key, group.Key + "   · " + string.Join(", ", group.Select(f => f.ModId).Distinct()), null));
            _sceneList.SetItems(items);
            _sceneList.Select(_isNew ? null : _sceneName);
        }

        private void OnScenePicked(string name)
        {
            if (name == _sceneName && !_isNew)
                return;
            _pendingScene = name;
            RunDiscardingChanges(OpenPendingScene, KeepSceneSelection);
        }

        private void OpenPendingScene() => OpenScene(_pendingScene);

        private void KeepSceneSelection() => _sceneList.Select(_isNew ? null : _sceneName);

        private void OpenScene(string name)
        {
            _sceneName = name;
            _isNew = false;
            var target = FilesOf(name).LastOrDefault(f => _writableMods.Contains(f.ModId))?.ModId
                         ?? _writableMods.FirstOrDefault();
            LoadTarget(target);
            _preview = Merged().FirstOrDefault().Background;
            MarkClean();
            SetStatus(string.Format(ToolUi.Tr("tools/scene_editor/opened"), name));
            RefreshAll();
        }

        /// <summary>Takes the chosen mod's file of the current scene as the edited one (a copy), or a new empty file.</summary>
        private void LoadTarget(string modId)
        {
            if (modId == null)
            {
                _target = null;
                return;
            }

            var own = FilesOf(_sceneName).FirstOrDefault(f => f.ModId == modId);
            _target = new SceneFileEntry
            {
                ModId = modId,
                RelativePath = own?.RelativePath ?? SceneFileEntry.PathFor(_sceneName ?? string.Empty),
                Name = _sceneName,
                Backgrounds = new List<string>(own?.Backgrounds ?? new List<string>()),
                Ambient = own?.Ambient ?? string.Empty,
                DefaultScenario = own?.DefaultScenario ?? string.Empty,
            };
        }

        private IEnumerable<SceneFileEntry> FilesOf(string name) => _files.Where(f => f.Name == name);

        /// <summary>
        /// The scene as the game will merge it after saving: the other mods' files in load
        /// order with the edited file at its mod's place. Yields each background with the
        /// file it comes from.
        /// </summary>
        private IEnumerable<(string Background, SceneFileEntry File)> Merged()
        {
            foreach (var file in MergedFiles())
            {
                foreach (var background in file.Backgrounds)
                    yield return (background, file);
            }
        }

        private List<SceneFileEntry> MergedFiles()
        {
            var order = ModRegistry.Instance.GetOrderedModIds();
            var result = FilesOf(_sceneName).Where(f => _target == null || f.ModId != _target.ModId).ToList();
            if (_target != null)
                result.Add(_target);
            return result.OrderBy(f => Rank(order, f.ModId)).ToList();
        }

        private static int Rank(IReadOnlyList<string> order, string modId)
        {
            for (var i = 0; i < order.Count; i++)
            {
                if (order[i] == modId)
                    return i;
            }
            return order.Count;
        }

        // ── editing ────────────────────────────────────────────────

        private void NewScene()
        {
            RunDiscardingChanges(StartNewScene);
        }

        private void StartNewScene()
        {
            _sceneName = string.Empty;
            _isNew = true;
            LoadTarget(_writableMods.FirstOrDefault());
            _preview = null;
            _sceneList.Select(null);
            SetDirty();
            SetStatus(ToolUi.Tr("tools/scene_editor/new_started"));
            RefreshAll();
            _nameEdit.GrabFocus();
        }

        private void OnNameChanged(string text)
        {
            if (_suppress || !_isNew || _target == null)
                return;
            _sceneName = SceneFileService.ValidName(text) ?? string.Empty;
            _target.Name = _sceneName;
            _target.RelativePath = SceneFileEntry.PathFor(_sceneName);
            SetDirty();
        }

        private void OnModPicked(long index)
        {
            if (_suppress)
                return;
            var mod = index >= 0 && index < _writableMods.Count ? _writableMods[(int)index] : null;
            if (_isNew)
            {
                if (_target != null)
                    _target.ModId = mod;
                RefreshAll();
                return;
            }
            _pendingMod = mod;
            RunDiscardingChanges(SwitchTargetMod, RefreshAll);
        }

        private void SwitchTargetMod()
        {
            LoadTarget(_pendingMod);
            MarkClean();
            RefreshAll();
        }

        private void OnAmbientPicked(long index)
        {
            if (_suppress || _target == null)
                return;
            _target.Ambient = index > 0 ? _sounds[(int)index - 1] : string.Empty;
            StopAmbientPreview();
            SetDirty();
            RefreshAll();
        }

        private void OnDefaultPicked(long index)
        {
            if (_suppress || _target == null)
                return;
            _target.DefaultScenario = index > 0 ? _defaultChoices[(int)index - 1] : string.Empty;
            SetDirty();
            RefreshAll();
        }

        private void ShowPreview(string background)
        {
            _preview = background;
            RefreshPreview();
        }

        private void AddPreviewed() => AddBackground(_preview);

        private void AddBackground(string background)
        {
            if (_target == null || string.IsNullOrEmpty(background))
                return;
            if (Merged().Any(m => m.Background == background))
            {
                SetStatus(string.Format(ToolUi.Tr("tools/scene_editor/already_there"), background));
                return;
            }
            _target.Backgrounds.Add(background);
            _preview = background;
            SetDirty();
            RefreshAll();
        }

        private void RemoveBackground(string background)
        {
            if (_target == null || !_target.Backgrounds.Remove(background))
                return;
            if (_preview == background)
                _preview = Merged().FirstOrDefault().Background;
            SetDirty();
            RefreshAll();
        }

        // ── saving, deleting, going ────────────────────────────────

        /// <summary>Ctrl+S.</summary>
        protected override void OnSaveShortcut() => Save();

        private void Save()
        {
            if (_target == null || !ModFileService.IsWritable(_target.ModId))
            {
                SetStatus(ToolUi.Tr("tools/scene_editor/no_writable_mod"), ToolStatusKind.Error);
                return;
            }
            if (string.IsNullOrEmpty(_sceneName))
            {
                SetStatus(ToolUi.Tr("tools/scene_editor/bad_name"), ToolStatusKind.Error);
                return;
            }
            if (_isNew && _files.Any(f => f.Name == _sceneName))
            {
                SetStatus(string.Format(ToolUi.Tr("tools/scene_editor/name_taken"), _sceneName), ToolStatusKind.Error);
                return;
            }
            if (!Merged().Any())
            {
                SetStatus(ToolUi.Tr("tools/scene_editor/need_background"), ToolStatusKind.Error);
                return;
            }

            if (!SceneFileService.Write(_target))
            {
                SetStatus(ToolUi.Tr("tools/common/readonly"), ToolStatusKind.Error);
                return;
            }

            var created = _isNew;
            SceneFileService.RefreshGame();
            var name = _sceneName;
            var mod = _target.ModId;
            LoadSources();
            _sceneName = name;
            _isNew = false;
            LoadTarget(mod);
            MarkClean();
            _sceneList.Select(name);
            RefreshAll();
            SetStatus(string.Format(ToolUi.Tr(created ? "tools/scene_editor/created" : "tools/common/saved"),
                created ? name : mod + "/" + _target.RelativePath), ToolStatusKind.Ok);
        }

        private void AskDelete()
        {
            if (_target == null || _isNew || !FilesOf(_sceneName).Any(f => f.ModId == _target.ModId))
                return;
            ShowConfirm("tools/scene_editor/delete", "tools/scene_editor/delete_confirm", Delete);
        }

        private void Delete()
        {
            if (!SceneFileService.Delete(_target))
            {
                SetStatus(ToolUi.Tr("tools/common/readonly"), ToolStatusKind.Error);
                return;
            }

            var path = _target.ModId + "/" + _target.RelativePath;
            SceneFileService.RefreshGame();
            LoadSources();
            if (FilesOf(_sceneName).Any())
            {
                OpenScene(_sceneName);
            }
            else
            {
                _sceneName = null;
                _target = null;
                MarkClean();
                RefreshAll();
            }
            SetStatus(string.Format(ToolUi.Tr("tools/scene_editor/deleted"), path), ToolStatusKind.Ok);
        }

        private void GoToScene()
        {
            if (_isNew || string.IsNullOrEmpty(_sceneName))
                return;
            var manager = GetTree().Root.GetNodeOrNull<SceneManager>("SceneManager");
            if (manager == null || !manager.HasScene(_sceneName))
            {
                SetStatus(string.Format(ToolUi.Tr("tools/scene_editor/go_failed"), _sceneName), ToolStatusKind.Error);
                return;
            }
            StopAmbientPreview();
            // Like the studio's teleport: a dialog left running would keep the box, and the
            // entered scene's own scenario would wait behind it (or the old one would go on).
            SceneContextNode.FindNode<DialogBox>("DialogBox")?.Abort();
            manager.SwitchScene(_sceneName);
            SetStatus(string.Format(ToolUi.Tr("tools/scene_editor/went"), _sceneName), ToolStatusKind.Ok);
        }

        private void Rescan()
        {
            RunDiscardingChanges(RescanNow);
        }

        private void RescanNow()
        {
            var name = _isNew ? null : _sceneName;
            LoadSources();
            if (name != null && FilesOf(name).Any())
                OpenScene(name);
            else
                RefreshAll();
            SetStatus(ToolUi.Tr("tools/scene_editor/rescanned"));
        }

        // ── scenarios ──────────────────────────────────────────────

        private void NewScenario()
        {
            if (_target == null || _isNew || string.IsNullOrEmpty(_sceneName))
                return;
            var key = ScenarioFileService.CreateForScene(_target.ModId, _sceneName, NewLine());
            if (key == null)
            {
                SetStatus(ToolUi.Tr("tools/common/readonly"), ToolStatusKind.Error);
                return;
            }
            SceneFileService.RefreshGame();
            RefreshAll();
            OpenScenarioEditor(key);
            SetStatus(string.Format(ToolUi.Tr("tools/scene_editor/scenario_created"), key), ToolStatusKind.Ok);
        }

        private void OnScenarioActivated(long index) => OpenSelectedScenario();

        private void OpenSelectedScenario()
        {
            var selected = _scenarioList.GetSelectedItems();
            if (selected.Length == 0 || selected[0] >= _scenarioKeys.Count)
                return;
            OpenScenarioEditor(_scenarioKeys[selected[0]]);
        }

        private static void OpenScenarioEditor(string key)
        {
            var window = new ScenarioEditorWindow();
            window.OpenScenario(key);
            ShowWindow(window);
        }

        private static string NewLine() => "text \"" + ToolUi.Tr("tools/scene_editor/new_line_text").Replace("\"", "\\\"") + "\"";

        // ── ambient preview ────────────────────────────────────────

        private void ToggleAmbientPreview()
        {
            if (_ambientPlaying)
            {
                StopAmbientPreview();
                RefreshAll();
                return;
            }

            var ambient = EffectiveAmbient().Ambient;
            var audio = GetTree().Root.GetNodeOrNull<Node>("AudioService") as IAudioService;
            if (audio == null || string.IsNullOrEmpty(ambient))
                return;
            audio.PlayAmbient(ambient);
            _ambientPlaying = true;
            RefreshAll();
        }

        private void StopAmbientPreview()
        {
            if (!_ambientPlaying)
                return;
            _ambientPlaying = false;
            (GetTree()?.Root.GetNodeOrNull<Node>("AudioService") as IAudioService)?.StopAmbient();
        }

        /// <summary>Stops a playing ambient preview when the window closes.</summary>
        protected override void Cleanup() => StopAmbientPreview();

        // ── refresh ────────────────────────────────────────────────

        /// <summary>The ambient the game will play: the last non-empty one in load order, and the mod it comes from.</summary>
        private (string Ambient, string ModId) EffectiveAmbient()
        {
            var file = MergedFiles().LastOrDefault(f => !string.IsNullOrEmpty(f.Ambient));
            return (file?.Ambient ?? string.Empty, file?.ModId);
        }

        private (string Scenario, string ModId) EffectiveDefault()
        {
            var file = MergedFiles().LastOrDefault(f => !string.IsNullOrEmpty(f.DefaultScenario));
            return (file?.DefaultScenario ?? string.Empty, file?.ModId);
        }

        private void RefreshAll()
        {
            _suppress = true;
            var hasScene = _target != null && (_isNew || !string.IsNullOrEmpty(_sceneName));

            _nameEdit.Text = _sceneName ?? string.Empty;
            _nameEdit.Editable = _isNew;
            _modSelect.Disabled = !hasScene;
            _modSelect.Select(_target == null ? -1 : _writableMods.IndexOf(_target.ModId));
            _modHint.Text = ModHint();

            _ambientSelect.Disabled = !hasScene;
            _ambientSelect.Select(_target == null ? 0 : _sounds.IndexOf(_target.Ambient) + 1);
            var ambient = EffectiveAmbient();
            _ambientHint.Text = ambient.ModId != null && ambient.ModId != _target?.ModId
                ? string.Format(ToolUi.Tr("tools/scene_editor/effective"), ambient.Ambient, ambient.ModId)
                : string.Empty;
            _ambientPlayBtn.Text = ToolUi.Tr(_ambientPlaying ? "tools/scene_editor/stop" : "tools/scene_editor/listen");
            _ambientPlayBtn.Disabled = string.IsNullOrEmpty(ambient.Ambient) && !_ambientPlaying;

            RefreshScenarios(hasScene);
            RefreshBackgrounds();
            RefreshPreview();

            _deleteBtn.Disabled = _target == null || _isNew || !FilesOf(_sceneName).Any(f => f.ModId == _target.ModId);
            _goBtn.Disabled = _isNew || string.IsNullOrEmpty(_sceneName);
            _suppress = false;
        }

        private string ModHint()
        {
            if (_writableMods.Count == 0)
                return ToolUi.Tr("tools/scene_editor/no_writable_mod");
            if (_target == null || _isNew)
                return string.Empty;
            var others = FilesOf(_sceneName).Select(f => f.ModId).Where(m => m != _target.ModId).Distinct().ToList();
            var own = FilesOf(_sceneName).Any(f => f.ModId == _target.ModId);
            if (!own)
                return string.Format(ToolUi.Tr("tools/scene_editor/adds_to"), string.Join(", ", others));
            return others.Count > 0 ? string.Format(ToolUi.Tr("tools/scene_editor/also_in"), string.Join(", ", others)) : string.Empty;
        }

        private void RefreshScenarios(bool hasScene)
        {
            _scenarioKeys.Clear();
            _defaultChoices.Clear();
            _scenarioList.Clear();
            if (hasScene && !_isNew)
            {
                foreach (var file in ScenarioFileService.ScanScenarioFiles().SelectMany(m => m.Groups).SelectMany(g => g.Files))
                {
                    var text = ScenarioFileService.ReadAllText(file.ModId, file.RelativePath);
                    if (text == null || ScenarioFileService.ParseMetadata(text).Scene != _sceneName)
                        continue;
                    _scenarioKeys.Add(file.Key);
                    _scenarioList.AddItem(file.ModId + ": " + file.RelativePath);
                    if (!_defaultChoices.Contains(file.RelativePath))
                        _defaultChoices.Add(file.RelativePath);
                }
            }
            if (_target != null && !string.IsNullOrEmpty(_target.DefaultScenario) && !_defaultChoices.Contains(_target.DefaultScenario))
                _defaultChoices.Add(_target.DefaultScenario);

            _defaultSelect.Clear();
            _defaultSelect.AddItem(ToolUi.Tr("tools/scene_editor/none"));
            foreach (var path in _defaultChoices)
                _defaultSelect.AddItem(path);
            _defaultSelect.Select(_target == null ? 0 : _defaultChoices.IndexOf(_target.DefaultScenario) + 1);
            _defaultSelect.Disabled = !hasScene || _isNew;
            var effective = EffectiveDefault();
            _defaultHint.Text = effective.ModId != null && effective.ModId != _target?.ModId
                ? string.Format(ToolUi.Tr("tools/scene_editor/effective"), effective.Scenario, effective.ModId)
                : string.Empty;

            if (hasScene && !_isNew && _scenarioKeys.Count == 0)
                _scenarioList.AddItem(ToolUi.Tr("tools/scene_editor/no_scenarios"), null, false);
            _openScenarioBtn.Disabled = _scenarioKeys.Count == 0;
            _newScenarioBtn.Disabled = !hasScene || _isNew || !ModFileService.IsWritable(_target?.ModId);
        }

        private void RefreshBackgrounds()
        {
            foreach (var child in _backgroundRow.GetChildren())
            {
                _backgroundRow.RemoveChild(child);
                child.QueueFree();
            }

            var any = false;
            foreach (var (background, file) in Merged())
            {
                any = true;
                _backgroundRow.AddChild(new SceneBackgroundTile(background, file.ModId, file == _target, background == _preview,
                    ShowPreview, RemoveBackground));
            }
            if (!any)
                _backgroundRow.AddChild(ToolUi.Hint(ToolUi.Tr(_target == null ? "tools/scene_editor/pick_scene" : "tools/scene_editor/no_backgrounds")));
        }

        private void RefreshPreview()
        {
            _previewRect.Texture = string.IsNullOrEmpty(_preview) ? null : ModResourceService.LoadBackgroundTexture(_preview);
            _previewCaption.Text = string.IsNullOrEmpty(_preview)
                ? ToolUi.Tr("tools/scene_editor/preview_hint")
                : _preview + (Merged().Any(m => m.Background == _preview) ? string.Empty : "   " + ToolUi.Tr("tools/scene_editor/not_in_scene"));
            if (_addBackgroundBtn != null)
                _addBackgroundBtn.Disabled = _target == null || string.IsNullOrEmpty(_preview) || Merged().Any(m => m.Background == _preview);
        }

        internal static Vector2 ThumbSize => new(ThumbWidth, ThumbHeight);
    }
}
