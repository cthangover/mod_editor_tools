using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cthangover.Core.Mods;
using Cthangover.Core.Mods.Resolvers;
using Cthangover.Core.UI.Theming;
using Cthangover.Core.Utils;
using Cthangover.Mods.Interface.Theming;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Tools.Common;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Tools.EntityEditor
{
    /// <summary>
    /// Base of the data editors (items, characters…): every entity of one kind in
    /// one writable mod, a search list on the left and a form on the right built
    /// from the editor's <see cref="EntityField"/> list. Layout and behaviour are
    /// shared by all such editors, so they look and work alike; a concrete editor
    /// only names its kind, its fields and how a new entity starts out.
    /// <para/>
    /// For a beginner the form is all there is: a name is typed in each language
    /// of the mod (the locale key is made from the id), a picture is picked from a
    /// grid, lists are check boxes. Save writes the mod's data and locale files and
    /// makes the running game re-read them (entities, then texts), so the change is
    /// visible at once. For a professional nothing is hidden: the locale key stays
    /// editable, and every property the form does not know is shown as JSON under
    /// "other fields", edited there and written back as it is.
    /// <para/>
    /// Every change is undoable (Ctrl+Z / Ctrl+Y): one undo step per editing
    /// session of one entity, and one per add, duplicate or delete.
    /// </summary>
    public abstract partial class EntityEditorWindow : ToolWindow
    {
        private readonly string kind;
        private readonly string folder;
        private readonly List<EntityField> fields;
        private readonly Dictionary<EntityField, Control> controls = new();
        private readonly Dictionary<EntityField, LineEdit> keyEdits = new();
        private readonly Dictionary<EntityField, Dictionary<string, LineEdit>> textEdits = new();
        private readonly Dictionary<EntityField, TextureRect> previews = new();
        private readonly Dictionary<string, Texture2D> icons = new();
        private readonly List<string> mods = new();

        private OptionButton modPicker;
        private FilteredList list;
        private VBoxContainer form;
        private Label emptyHint;
        private Label fileLabel;
        private LineEdit idEdit;
        private CodeEdit otherEdit;
        private EntityDocument document;
        private EntityRecord selected;
        private List<string> languages = new();
        private string modId;
        private bool suppress;
        private bool editing;

        /// <summary>Builds the window for entities of <paramref name="entityKind"/>; new ones go to <c>&lt;folder&gt;/&lt;mod&gt;.json</c>.</summary>
        protected EntityEditorWindow(string titleKey, string entityKind, string entityFolder, List<EntityField> entityFields) : base(titleKey)
        {
            kind = entityKind;
            folder = entityFolder;
            fields = entityFields;
            CthangoverTheme.Apply(this);
            BuildUi();
            SetHint(ToolUi.Tr("tools/entity_editor/hint"));
            FillMods();
        }

        /// <summary>A new entity's JSON: the kind's required fields with starting values.</summary>
        protected abstract JsonObject CreateEntity(string id);

        /// <summary>Id a new entity starts from (numbered when taken), e.g. <c>&lt;mod&gt;/new_item</c>.</summary>
        protected abstract string NewIdBase(string mod);

        /// <summary>The edited mod, or null.</summary>
        public string ModId => modId;

        /// <summary>The selected entity's id, or null.</summary>
        public string SelectedId => selected?.Id;

        /// <summary>The edited entities (null before a mod is opened).</summary>
        public EntityDocument Document => document;

        // ── layout ─────────────────────────────────────────────────

        private void BuildUi()
        {
            var outer = CreateFillContainer();
            AddChild(outer);
            outer.AddChild(BuildToolbar());

            var main = new HSplitContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
            outer.AddChild(main);

            var listColumn = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
            listColumn.AddChild(ToolUi.Section("tools/entity_editor/section_list"));
            list = new FilteredList(320f, "tools/entity_editor/search");
            list.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            list.Chosen += OnChosen;
            listColumn.AddChild(list);
            var listPanel = CreateSidebar(listColumn, 320);
            main.AddChild(listPanel);

            var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            main.AddChild(scroll);
            var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            column.AddThemeConstantOverride("separation", UiTokens.Space2);
            scroll.AddChild(column);
            emptyHint = ToolUi.Hint(ToolUi.Tr("tools/entity_editor/no_entity"));
            column.AddChild(emptyHint);
            form = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            form.AddThemeConstantOverride("separation", UiTokens.Space2);
            column.AddChild(form);
            BuildForm();
        }

        private HBoxContainer BuildToolbar()
        {
            var toolbar = CreateToolbar();
            modPicker = new OptionButton { TooltipText = ToolUi.Tr("tools/entity_editor/mod_tip") };
            modPicker.ItemSelected += OnModPicked;
            toolbar.AddChild(modPicker);
            toolbar.AddChild(ToolUi.Button("tools/common/save", OnSavePressed, ButtonKind.Primary, "tools/common/save_tip"));
            toolbar.AddChild(ToolUi.Divider());
            toolbar.AddChild(ToolUi.Button("tools/entity_editor/new", AddNew, ButtonKind.Secondary, "tools/entity_editor/new_tip"));
            toolbar.AddChild(ToolUi.Button("tools/entity_editor/duplicate", DuplicateSelected, ButtonKind.Secondary, "tools/entity_editor/duplicate_tip"));
            toolbar.AddChild(ToolUi.Button("tools/entity_editor/delete", DeleteSelected, ButtonKind.Secondary, "tools/entity_editor/delete_tip"));
            toolbar.AddChild(ToolUi.Divider());
            toolbar.AddChild(ToolUi.Button("tools/entity_editor/undo", Undo, ButtonKind.Secondary, "tools/entity_editor/undo_tip"));
            toolbar.AddChild(ToolUi.Button("tools/entity_editor/redo", Redo, ButtonKind.Secondary, "tools/entity_editor/redo_tip"));
            return toolbar;
        }

        private void BuildForm()
        {
            fileLabel = ToolUi.Hint(string.Empty);
            form.AddChild(fileLabel);
            idEdit = ToolUi.Field(form, "tools/entity_editor/id", ToolUi.Text("", OnTextChanged), "tools/entity_editor/id_tip");
            idEdit.AddThemeFontOverride("font", ToolUi.Mono());
            foreach (var field in fields)
                BuildField(field);

            form.AddChild(ToolUi.Section("tools/entity_editor/section_other"));
            form.AddChild(ToolUi.Hint(ToolUi.Tr("tools/entity_editor/other_hint")));
            otherEdit = ToolUi.Code();
            otherEdit.CustomMinimumSize = new Vector2(0, 160);
            otherEdit.SizeFlagsVertical = Control.SizeFlags.Fill;
            otherEdit.TextChanged += OnOtherChanged;
            form.AddChild(otherEdit);
        }

        private void BuildField(EntityField field)
        {
            switch (field.Kind)
            {
                case EntityFieldKind.Text:
                    controls[field] = ToolUi.Field(form, field.LabelKey, ToolUi.Text("", OnTextChanged), field.TooltipKey);
                    break;
                case EntityFieldKind.Integer:
                    controls[field] = ToolUi.Field(form, field.LabelKey, ToolUi.Number(-999999, 999999, 1, OnNumberChanged), field.TooltipKey);
                    break;
                case EntityFieldKind.Choice:
                    var choice = ToolUi.Field(form, field.LabelKey, new OptionButton(), field.TooltipKey);
                    choice.ItemSelected += OnChoicePicked;
                    controls[field] = choice;
                    break;
                case EntityFieldKind.Flags:
                case EntityFieldKind.List:
                    controls[field] = ToolUi.Field(form, field.LabelKey, new HFlowContainer(), field.TooltipKey);
                    break;
                case EntityFieldKind.Picture:
                    BuildPicture(field);
                    break;
                case EntityFieldKind.LocaleText:
                    // The texts get their rows when a mod is opened: its languages decide them.
                    keyEdits[field] = ToolUi.Field(form, field.LabelKey, ToolUi.Text("", OnTextChanged), "tools/entity_editor/locale_key_tip");
                    keyEdits[field].AddThemeFontOverride("font", ToolUi.Mono());
                    var texts = new VBoxContainer();
                    form.AddChild(texts);
                    controls[field] = texts;
                    textEdits[field] = new Dictionary<string, LineEdit>();
                    break;
            }
        }

        private void BuildPicture(EntityField field)
        {
            var row = new HBoxContainer();
            var path = ToolUi.Text(field.Group + "/…", OnPictureTyped);
            path.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(path);
            var pick = UiKit.IconButton("…", ToolUi.Tr("tools/entity_editor/picture_pick_tip"));
            pick.Pressed += () => OpenPicker(field);
            row.AddChild(pick);
            ToolUi.Field(form, field.LabelKey, row, field.TooltipKey);
            controls[field] = path;
            var preview = new TextureRect
            {
                CustomMinimumSize = new Vector2(96, 96),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin
            };
            var margin = new MarginContainer();
            margin.AddThemeConstantOverride("margin_left", ToolUi.LabelWidth + UiTokens.Space3);
            margin.AddChild(preview);
            form.AddChild(margin);
            previews[field] = preview;
        }

        // ── mods and the list ──────────────────────────────────────

        private void FillMods()
        {
            mods.Clear();
            mods.AddRange(ModFileService.ListWritableMods());
            modPicker.Clear();
            foreach (var mod in mods)
                modPicker.AddItem(mod);
            if (mods.Count == 0)
            {
                SetStatus(ToolUi.Tr("tools/common/no_writable_mods"), ToolStatusKind.Error);
                return;
            }
            // Start in the first mod that already has such entities: usually the game's own content.
            var start = mods.FindIndex(m => LoadDocument(m).Records.Count > 0);
            OpenMod(mods[Math.Max(0, start)]);
        }

        private EntityDocument LoadDocument(string mod)
        {
            var sources = ModFileService.ListFiles(mod, string.Empty, ".json")
                .Select(path => (path, ModFileService.ReadText(mod, path)));
            var loaded = EntityDocument.Load(kind, $"{folder}/{mod}.json", sources, out var skipped);
            if (skipped.Count > 0)
                SetStatus(string.Format(ToolUi.Tr("tools/entity_editor/broken_files"), string.Join(", ", skipped)), ToolStatusKind.Error);
            return loaded;
        }

        /// <summary>Opens the entities of a mod (asks first about unsaved changes).</summary>
        public void OpenMod(string mod)
        {
            RunDiscardingChanges(() => DoOpenMod(mod), () => modPicker.Select(mods.IndexOf(modId)));
        }

        private void DoOpenMod(string mod)
        {
            modId = mod;
            modPicker.Select(mods.IndexOf(mod));
            document = LoadDocument(mod);
            languages = LocaleFileService.Languages(mod);
            foreach (var field in fields.Where(f => f.Kind == EntityFieldKind.LocaleText))
                BuildTextRows(field);
            MarkClean();
            selected = null;
            RefreshList();
            Select(document.Records.FirstOrDefault());
            SetStatus(string.Format(ToolUi.Tr("tools/entity_editor/opened"), document.Records.Count, mod));
        }

        private void BuildTextRows(EntityField field)
        {
            var box = (VBoxContainer)controls[field];
            ClearChildren(box);
            textEdits[field].Clear();
            foreach (var language in languages)
            {
                var label = string.Format(ToolUi.Tr("tools/entity_editor/text_in"), language);
                var edit = ToolUi.Text("", OnTextChanged);
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", UiTokens.Space3);
                row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(ToolUi.LabelWidth, 0), HorizontalAlignment = HorizontalAlignment.Right });
                edit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                row.AddChild(edit);
                box.AddChild(row);
                textEdits[field][language] = edit;
            }
        }

        private void RefreshList()
        {
            var first = fields.FirstOrDefault(f => f.Kind == EntityFieldKind.Picture);
            var name = fields.FirstOrDefault(f => f.Kind == EntityFieldKind.LocaleText);
            list.SetItems(document.Records.Select(r => (r.Id, RowText(r, name), first == null ? null : Icon(first.Group, Str(r.Data, first.Key)))));
            list.Select(selected?.Id);
        }

        private string RowText(EntityRecord record, EntityField nameField)
        {
            var key = nameField == null ? null : Str(record.Data, nameField.Key);
            var text = key == null ? null : document.GetText(languages.FirstOrDefault() ?? "ru", key) ?? LocaleFileService.Read(modId, languages.FirstOrDefault() ?? "ru", key);
            return string.IsNullOrWhiteSpace(text) ? record.Id : $"{text}  ·  {record.Id}";
        }

        private Texture2D Icon(string group, string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            var cacheKey = group + "|" + id;
            if (!icons.TryGetValue(cacheKey, out var texture))
            {
                texture = ModResourceService.LoadGroupImage(group, id);
                icons[cacheKey] = texture;
            }
            return texture;
        }

        private void OnChosen(string id) => Select(document?.Find(id));

        private void OnModPicked(long index)
        {
            if (index >= 0 && index < mods.Count && mods[(int)index] != modId)
                OpenMod(mods[(int)index]);
        }

        // ── form ───────────────────────────────────────────────────

        /// <summary>Shows an entity in the form (null: none).</summary>
        public void Select(EntityRecord record)
        {
            selected = record;
            editing = false;
            emptyHint.Visible = record == null;
            form.Visible = record != null;
            list.Select(record?.Id);
            if (record == null)
                return;

            suppress = true;
            fileLabel.Text = string.Format(ToolUi.Tr("tools/entity_editor/file"), modId, record.File);
            idEdit.Text = record.Id;
            foreach (var field in fields)
                ShowField(field, record.Data);
            otherEdit.Text = OtherJson(record.Data);
            suppress = false;
        }

        private void ShowField(EntityField field, JsonObject data)
        {
            switch (field.Kind)
            {
                case EntityFieldKind.Text:
                    ((LineEdit)controls[field]).Text = Str(data, field.Key) ?? string.Empty;
                    break;
                case EntityFieldKind.Integer:
                    ((SpinBox)controls[field]).Value = data[field.Key] is JsonValue v && v.TryGetValue(out int number) ? number : 0;
                    break;
                case EntityFieldKind.Choice:
                    FillChoice((OptionButton)controls[field], field, Str(data, field.Key));
                    break;
                case EntityFieldKind.Flags:
                    FillChecks((HFlowContainer)controls[field], field, (Str(data, field.Key) ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                    break;
                case EntityFieldKind.List:
                    FillChecks((HFlowContainer)controls[field], field, (data[field.Key] as JsonArray)?.Select(n => n?.ToString()).Where(s => s != null).ToArray() ?? Array.Empty<string>());
                    break;
                case EntityFieldKind.Picture:
                    var path = Str(data, field.Key) ?? string.Empty;
                    ((LineEdit)controls[field]).Text = path;
                    previews[field].Texture = ModResourceService.LoadGroupImage(field.Group, path);
                    break;
                case EntityFieldKind.LocaleText:
                    var key = Str(data, field.Key) ?? string.Empty;
                    keyEdits[field].Text = key;
                    foreach (var pair in textEdits[field])
                        pair.Value.Text = TextOf(pair.Key, key);
                    break;
            }
        }

        private string TextOf(string language, string key)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;
            var text = document.GetText(language, key);
            if (text != null)
                return text;
            text = LocaleFileService.Read(modId, language, key) ?? string.Empty;
            document.LoadText(language, key, text);
            return text;
        }

        private static void FillChoice(OptionButton picker, EntityField field, string value)
        {
            var options = (field.Options?.Invoke() ?? Enumerable.Empty<string>()).ToList();
            if (!string.IsNullOrEmpty(value) && !options.Contains(value))
                options.Insert(0, value);
            picker.Clear();
            foreach (var option in options)
                picker.AddItem(option);
            picker.Select(options.IndexOf(value ?? string.Empty));
        }

        private void FillChecks(HFlowContainer box, EntityField field, string[] values)
        {
            ClearChildren(box);
            var options = (field.Options?.Invoke() ?? Enumerable.Empty<string>()).ToList();
            options.AddRange(values.Where(v => !options.Contains(v)));
            foreach (var option in options)
            {
                var check = new CheckBox { Text = option, ButtonPressed = values.Contains(option) };
                check.Toggled += OnChecked;
                box.AddChild(check);
            }
        }

        private void OnTextChanged(string text) => WriteForm();

        private void OnNumberChanged(double value) => WriteForm();

        private void OnChoicePicked(long index) => WriteForm();

        private void OnChecked(bool on) => WriteForm();

        private void OnPictureTyped(string text)
        {
            WriteForm();
            foreach (var pair in previews)
                pair.Value.Texture = ModResourceService.LoadGroupImage(pair.Key.Group, ((LineEdit)controls[pair.Key]).Text);
        }

        private void OpenPicker(EntityField field)
        {
            if (selected == null)
                return;
            var dialog = new PicturePickerDialog(field.Group, ((LineEdit)controls[field]).Text);
            dialog.Picked += id => ((LineEdit)controls[field]).Text = id;
            dialog.Picked += OnPictureTyped;
            AddChild(dialog);
            dialog.PopupCentered();
        }

        /// <summary>Copies the form into the selected entity; the first change of a session records an undo step.</summary>
        private void WriteForm()
        {
            if (suppress || selected == null)
                return;
            if (!editing)
            {
                document.BeginChange();
                editing = true;
            }

            var data = selected.Data;
            data["Id"] = idEdit.Text.Trim();
            foreach (var field in fields)
                WriteField(field, data);
            SetDirty();
            RefreshList();
        }

        private void WriteField(EntityField field, JsonObject data)
        {
            switch (field.Kind)
            {
                case EntityFieldKind.Text:
                case EntityFieldKind.Picture:
                    SetOrRemove(data, field.Key, ((LineEdit)controls[field]).Text.Trim());
                    break;
                case EntityFieldKind.Integer:
                    var number = (int)((SpinBox)controls[field]).Value;
                    if (number != 0 || data.ContainsKey(field.Key))
                        data[field.Key] = number;
                    break;
                case EntityFieldKind.Choice:
                    var picker = (OptionButton)controls[field];
                    if (picker.Selected >= 0)
                        data[field.Key] = picker.GetItemText(picker.Selected);
                    break;
                case EntityFieldKind.Flags:
                    var flags = Checked((HFlowContainer)controls[field]);
                    if (flags.Count > 0 || data.ContainsKey(field.Key))
                        data[field.Key] = flags.Count == 0 ? "None" : string.Join(",", flags);
                    break;
                case EntityFieldKind.List:
                    var values = Checked((HFlowContainer)controls[field]);
                    if (values.Count > 0 || data.ContainsKey(field.Key))
                        data[field.Key] = new JsonArray(values.Select(v => (JsonNode)v).ToArray());
                    break;
                case EntityFieldKind.LocaleText:
                    var key = keyEdits[field].Text.Trim();
                    SetOrRemove(data, field.Key, key);
                    if (key.Length == 0)
                        break;
                    foreach (var pair in textEdits[field])
                        document.SetText(pair.Key, key, pair.Value.Text);
                    break;
            }
        }

        private static List<string> Checked(HFlowContainer box)
            => box.GetChildren().OfType<CheckBox>().Where(c => c.ButtonPressed).Select(c => c.Text).ToList();

        /// <summary>Takes the children out of the tree at once (a search of the tree must not find them) and frees them.</summary>
        private static void ClearChildren(Node box)
        {
            foreach (var child in box.GetChildren())
            {
                box.RemoveChild(child);
                child.QueueFree();
            }
        }

        private static void SetOrRemove(JsonObject data, string key, string value)
        {
            if (value.Length > 0)
                data[key] = value;
            else
                data.Remove(key);
        }

        /// <summary>The properties no field shows, as an indented JSON object.</summary>
        private string OtherJson(JsonObject data)
        {
            var other = new JsonObject();
            foreach (var pair in data.Where(p => !IsFormKey(p.Key)))
                other[pair.Key] = pair.Value?.DeepClone();
            return EntityDocument.Serialize(other).TrimEnd();
        }

        private bool IsFormKey(string key) => key == "Id" || fields.Any(f => f.Key == key);

        private void OnOtherChanged()
        {
            if (suppress || selected == null)
                return;
            JsonObject other;
            try
            {
                other = JsonNode.Parse(otherEdit.Text) as JsonObject;
            }
            catch (JsonException ex)
            {
                SetStatus(string.Format(ToolUi.Tr("tools/entity_editor/other_invalid"), ex.Message), ToolStatusKind.Error);
                return;
            }
            if (other == null)
                return;
            if (!editing)
            {
                document.BeginChange();
                editing = true;
            }
            var data = selected.Data;
            foreach (var key in data.Select(p => p.Key).Where(k => !IsFormKey(k)).ToList())
                data.Remove(key);
            foreach (var pair in other.Where(p => !IsFormKey(p.Key)).ToList())
                data[pair.Key] = pair.Value?.DeepClone();
            SetDirty();
            SetStatus(ToolUi.Tr("tools/entity_editor/other_applied"));
        }

        // ── commands ───────────────────────────────────────────────

        private void AddNew()
        {
            if (document == null)
                return;
            document.BeginChange();
            var id = document.FreeId(NewIdBase(modId));
            var entity = CreateEntity(id);
            foreach (var field in fields.Where(f => f.Kind == EntityFieldKind.LocaleText && f.KeyPattern != null))
            {
                var key = string.Format(field.KeyPattern, id);
                entity[field.Key] = key;
                foreach (var language in languages)
                    document.SetText(language, key, field.Key == "Name" ? id.Substring(id.LastIndexOf('/') + 1) : string.Empty);
            }
            var record = document.Add(entity);
            SetDirty();
            RefreshList();
            Select(record);
            SetStatus(string.Format(ToolUi.Tr("tools/entity_editor/added"), id, record.File));
        }

        private void DuplicateSelected()
        {
            if (selected == null)
                return;
            document.BeginChange();
            var record = document.Duplicate(selected, document.FreeId(selected.Id));
            SetDirty();
            RefreshList();
            Select(record);
        }

        private void DeleteSelected()
        {
            if (selected == null)
                return;
            ShowConfirm("tools/entity_editor/delete_title", "tools/entity_editor/delete_text", ConfirmDelete);
        }

        private void ConfirmDelete()
        {
            if (selected == null)
                return;
            document.BeginChange();
            var id = selected.Id;
            document.Remove(selected);
            SetDirty();
            Select(null);
            RefreshList();
            SetStatus(string.Format(ToolUi.Tr("tools/entity_editor/deleted"), id));
        }

        private void Undo() => StepHistory(document != null && document.Undo());

        private void Redo() => StepHistory(document != null && document.Redo());

        private void StepHistory(bool stepped)
        {
            if (!stepped)
                return;
            // The document holds new JSON objects now: find the selection again by id.
            var id = selected?.Id;
            SetDirty();
            RefreshList();
            Select(document.Find(id) ?? document.Records.FirstOrDefault());
        }

        /// <summary>Ctrl+S.</summary>
        protected override void OnSaveShortcut() => Save();

        private void OnSavePressed() => Save();

        /// <summary>
        /// Writes the changed data and locale files and makes the game re-read
        /// them: the entity cache is dropped (factories rebuild from the new
        /// snapshot) and the current language's texts are reloaded.
        /// </summary>
        public bool Save()
        {
            if (document == null)
                return false;
            var problems = document.Validate();
            if (problems.Count > 0)
            {
                SetStatus(string.Format(ToolUi.Tr(problems[0].Key), problems[0].Argument), ToolStatusKind.Error);
                return false;
            }

            var changedFiles = document.ChangedFiles();
            var changedTexts = document.ChangedTexts();
            foreach (var (path, text) in changedFiles)
            {
                if (!ModFileService.WriteText(modId, path, text))
                {
                    SetStatus(ToolUi.Tr("tools/common/readonly"), ToolStatusKind.Error);
                    return false;
                }
            }
            foreach (var (language, key, value) in changedTexts)
                LocaleFileService.Write(modId, language, key, value);

            document.MarkSaved();
            MarkClean();
            editing = false;
            ModManager.Instance.GetResolver<EntityResolver>()?.ClearCache();
            ModManager.Instance.GetResolver<FileListResolver>()?.ClearCache();
            icons.Clear();
            if (changedTexts.Count > 0)
                LocaleFileService.ReloadGame();
            GameLogger.Log("TOOLS", $"entity editor: saved {changedFiles.Count} {kind} file(s), {changedTexts.Count} text(s) of '{modId}'");
            SetStatus(string.Format(ToolUi.Tr("tools/entity_editor/saved"), changedFiles.Count, changedTexts.Count, modId), ToolStatusKind.Ok);
            return true;
        }

        /// <summary>Shortcuts: Ctrl+Z / Ctrl+Y undo and redo, Ctrl+N adds, Ctrl+D duplicates.</summary>
        protected override bool HandleKey(InputEventKey key)
        {
            if (!key.CtrlPressed)
                return false;
            switch (key.Keycode)
            {
                case Key.Z when key.ShiftPressed:
                case Key.Y:
                    Redo();
                    return true;
                case Key.Z:
                    Undo();
                    return true;
                case Key.N:
                    AddNew();
                    return true;
                case Key.D:
                    DuplicateSelected();
                    return true;
            }
            return false;
        }

        private static string Str(JsonObject data, string key) => data[key]?.ToString();
    }
}
