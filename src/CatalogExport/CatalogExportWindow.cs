using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Cthangover.Core.Mods;
using Cthangover.Core.UI.Theming;
using Cthangover.Core.Utils;
using Cthangover.Mods.Interface.Theming;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Tools.Common;
using Godot;

namespace Cthangover.Core.UI.Tool.CatalogExport
{
    /// <summary>
    /// Developer tool that builds a catalog entry (JSON for the online mod
    /// catalog) from a locally installed mod's manifest. Ported out of the old
    /// mods menu so that mod-management UI can live in the <c>mod_manager</c>
    /// mod while this authoring utility stays a tool.
    /// <para/>
    /// Left: the form — read-only manifest data, catalog-only metadata
    /// (download URL, categories, tags, changelog, status, minimum game
    /// version, icon URL) and localized names. Right: the live JSON preview.
    /// Actions: copy the JSON or export it next to the mod's zip via
    /// <see cref="ModCatalogExporter"/> (archive and hashes are computed there).
    /// </summary>
    public partial class CatalogExportWindow : ToolWindow
    {
        private static readonly string[] Statuses = { "stable", "beta", "deprecated", "archived" };
        private static readonly CategoryType[] Categories = Enum.GetValues<CategoryType>();

        private OptionButton _modSelect;
        private VBoxContainer _formHost;
        private CodeEdit _previewEdit;

        private ModInfo _modInfo;
        private ModManifest _manifest;
        private LineEdit _urlEdit;
        private readonly List<CheckBox> _categoryChecks = new();
        private LineEdit _tagsEdit;
        private TextEdit _changelogEdit;
        private OptionButton _statusOpt;
        private LineEdit _minGameVerEdit;
        private LineEdit _iconUrlEdit;
        private VBoxContainer _localeContainer;

        public CatalogExportWindow() : base("mods/catalog_title")
        {
            Size = new Vector2I(1200, 820);
            CthangoverTheme.Apply(this);

            var outer = CreateFillContainer();
            AddChild(outer);

            var toolbar = CreateToolbar();
            outer.AddChild(toolbar);
            _modSelect = new OptionButton { CustomMinimumSize = new Vector2(320, 0), TooltipText = ToolUi.Tr("mods/catalog_select_mod") };
            _modSelect.ItemSelected += OnModSelected;
            toolbar.AddChild(_modSelect);
            toolbar.AddChild(UiKit.Spacer());
            toolbar.AddChild(ToolUi.Button("mods/catalog_copy", OnCopyPressed));
            toolbar.AddChild(ToolUi.Button("mods/catalog_export_file", OnExportPressed, ButtonKind.Primary, "tools/catalog_export/export_tip"));

            var split = new HSplitContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            outer.AddChild(split);

            _formHost = new VBoxContainer();
            var form = CreateSidebar(_formHost, 520);
            form.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            split.AddChild(form);

            var previewColumn = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            previewColumn.AddChild(ToolUi.Section("tools/catalog_export/preview"));
            _previewEdit = ToolUi.Code(editable: false);
            previewColumn.AddChild(_previewEdit);
            split.AddChild(previewColumn);

            SetHint(ToolUi.Tr("tools/catalog_export/hint"));
            PopulateModList();
        }

        private void PopulateModList()
        {
            var ordered = ModRegistry.Instance.GetOrderedModIds(filterDisabled: false);
            foreach (var id in ordered)
            {
                var info = ModRegistry.Instance.GetMod(id);
                if (info == null)
                    continue;

                _modSelect.AddItem($"{info.DisplayTitle}  ({id})");
                _modSelect.SetItemMetadata(_modSelect.ItemCount - 1, id);
            }

            if (_modSelect.ItemCount > 0)
            {
                _modSelect.Selected = 0;
                BuildForm(_modSelect.GetItemMetadata(0).AsString());
            }
        }

        private void OnModSelected(long index)
        {
            BuildForm(_modSelect.GetItemMetadata((int)index).AsString());
        }

        private void BuildForm(string modId)
        {
            foreach (var child in _formHost.GetChildren())
                child.QueueFree();
            _categoryChecks.Clear();

            _modInfo = ModRegistry.Instance.GetMod(modId) as ModInfo;
            _manifest = _modInfo?.Manifest;
            if (_modInfo == null || _manifest == null)
                return;

            _formHost.AddChild(ToolUi.Section("tools/catalog_export/mod_info"));
            AddReadOnly("tools/catalog_export/id", _modInfo.Id);
            AddReadOnly("tools/catalog_export/name", _modInfo.DisplayTitle);
            AddReadOnly("mods/version", _modInfo.Version ?? "—");
            AddReadOnly("mods/author", _modInfo.Author ?? "—");

            _formHost.AddChild(ToolUi.Section("tools/catalog_export/metadata"));
            _urlEdit = ToolUi.Field(_formHost, "mods/catalog_download_url", ToolUi.Text("https://…/" + modId + ".zip", _ => UpdatePreview()));
            _statusOpt = ToolUi.Field(_formHost, "mods/catalog_status", new OptionButton());
            foreach (var status in Statuses)
                _statusOpt.AddItem(status);
            _statusOpt.ItemSelected += _ => UpdatePreview();
            _minGameVerEdit = ToolUi.Field(_formHost, "mods/catalog_min_game_version", ToolUi.Text("1.0.0", _ => UpdatePreview()));
            _iconUrlEdit = ToolUi.Field(_formHost, "mods/catalog_icon_url", ToolUi.Text("https://…/icon.png", _ => UpdatePreview()));
            _tagsEdit = ToolUi.Field(_formHost, "mods/catalog_tags", ToolUi.Text("story, ru", _ => UpdatePreview()));

            _formHost.AddChild(ToolUi.Section("mods/catalog_category"));
            var grid = new GridContainer { Columns = 3 };
            grid.AddThemeConstantOverride("h_separation", UiTokens.Space5);
            var preselected = _manifest.Category ?? CategoryType.Other;
            foreach (var category in Categories)
            {
                var check = new CheckBox { Text = GetCategoryDisplayName(category), ButtonPressed = (preselected & category) != 0 };
                check.Toggled += _ => UpdatePreview();
                _categoryChecks.Add(check);
                grid.AddChild(check);
            }
            _formHost.AddChild(grid);

            _formHost.AddChild(ToolUi.Section("mods/catalog_changelog"));
            _changelogEdit = new TextEdit
            {
                CustomMinimumSize = new Vector2(0, 80),
                WrapMode = TextEdit.LineWrappingMode.Boundary,
            };
            _changelogEdit.TextChanged += UpdatePreview;
            _formHost.AddChild(_changelogEdit);

            _formHost.AddChild(ToolUi.Section("tools/catalog_export/locales"));
            _formHost.AddChild(ToolUi.Hint(ToolUi.Tr("tools/catalog_export/locales_hint")));
            _localeContainer = new VBoxContainer();
            _localeContainer.AddThemeConstantOverride("separation", UiTokens.Space1);
            _formHost.AddChild(_localeContainer);
            _formHost.AddChild(ToolUi.Button("mods/catalog_add_locale", () => AddLocaleRow(string.Empty, string.Empty, string.Empty)));

            UpdatePreview();
            SetStatus(string.Empty);
        }

        private void AddReadOnly(string labelKey, string value)
        {
            var label = UiKit.Label(value, UiTokens.FontBody, UiTokens.Body);
            label.AddThemeFontOverride("font", ToolUi.Mono());
            ToolUi.Field(_formHost, labelKey, label);
        }

        private static string GetCategoryDisplayName(CategoryType cat) => cat switch
        {
            CategoryType.Story => ToolUi.Tr("mods/category_story"),
            CategoryType.Characters => ToolUi.Tr("mods/category_characters"),
            CategoryType.Gameplay => ToolUi.Tr("mods/category_gameplay"),
            CategoryType.Visual => ToolUi.Tr("mods/category_visual"),
            CategoryType.Audio => ToolUi.Tr("mods/category_audio"),
            CategoryType.UI => ToolUi.Tr("mods/category_ui"),
            CategoryType.Locale => ToolUi.Tr("mods/category_locale"),
            CategoryType.Library => ToolUi.Tr("mods/category_library"),
            CategoryType.Engine => ToolUi.Tr("mods/category_engine"),
            CategoryType.Overhaul => ToolUi.Tr("mods/category_overhaul"),
            CategoryType.Tools => ToolUi.Tr("mods/category_tools"),
            CategoryType.Editor => ToolUi.Tr("mods/category_editor"),
            CategoryType.Debug => ToolUi.Tr("mods/category_debug"),
            CategoryType.Other => ToolUi.Tr("mods/category_other"),
            _ => cat.ToString(),
        };

        private void AddLocaleRow(string lang, string name, string description)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", UiTokens.Space1);

            var langEdit = new LineEdit { CustomMinimumSize = new Vector2(52, 0), PlaceholderText = "en", MaxLength = 5, Text = lang };
            row.AddChild(langEdit);
            var nameEdit = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, PlaceholderText = ToolUi.Tr("mods/catalog_locale_name"), Text = name };
            row.AddChild(nameEdit);
            var descEdit = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, PlaceholderText = ToolUi.Tr("mods/catalog_locale_desc"), Text = description };
            row.AddChild(descEdit);

            var removeBtn = UiKit.IconButton("✕", ToolUi.Tr("tools/catalog_export/remove_locale"));
            removeBtn.Pressed += () => RemoveLocaleRow(row);
            row.AddChild(removeBtn);

            langEdit.TextChanged += _ => UpdatePreview();
            nameEdit.TextChanged += _ => UpdatePreview();
            descEdit.TextChanged += _ => UpdatePreview();

            _localeContainer.AddChild(row);
            UpdatePreview();
        }

        private void RemoveLocaleRow(HBoxContainer row)
        {
            _localeContainer.RemoveChild(row);
            row.QueueFree();
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (_previewEdit == null || _modInfo == null || _manifest == null || _changelogEdit == null)
                return;

            var entry = BuildExportEntry();
            // Hashes are computed by the exporter from the real archive.
            entry.Downloads[0].ArchiveSha256 = "computed-on-export";
            entry.Downloads[0].InstallSha256 = "computed-on-export";
            _previewEdit.Text = JsonSerializer.Serialize(entry, JsonOptionsProvider.WriteIndentedIgnoreNullOptions);
        }

        private List<CatalogLocaleEntry> CollectLocales()
        {
            var list = new List<CatalogLocaleEntry>();
            foreach (var child in _localeContainer.GetChildren())
            {
                if (child is not HBoxContainer row || row.IsQueuedForDeletion() || row.GetChildCount() < 3)
                    continue;

                var lang = ((LineEdit)row.GetChild(0)).Text.Trim();
                if (lang.Length == 0)
                    continue;

                list.Add(new CatalogLocaleEntry
                {
                    Lang = lang,
                    Name = NullIfEmpty(((LineEdit)row.GetChild(1)).Text),
                    Description = NullIfEmpty(((LineEdit)row.GetChild(2)).Text),
                });
            }

            if (list.Count == 0)
                list.Add(new CatalogLocaleEntry { Lang = "en", Name = _manifest.Name, Description = _manifest.Description });

            return list;
        }

        private CatalogExportEntry BuildExportEntry()
        {
            var entry = BuildEntryFromManifest(_manifest);
            entry.Downloads = new List<CatalogEntryDownload>
            {
                new()
                {
                    Version = entry.Version,
                    Urls = new List<string> { _urlEdit.Text.Trim() },
                    SizeBytes = 0,
                    Changelog = NullIfEmpty(_changelogEdit.Text),
                },
            };
            entry.Category = CollectSelectedCategories();
            entry.Tags = ParseTags(_tagsEdit.Text);
            entry.Status = Statuses[Math.Max(0, _statusOpt.Selected)];
            entry.MinGameVersion = NullIfEmpty(_minGameVerEdit.Text);
            entry.IconUrl = NullIfEmpty(_iconUrlEdit.Text);
            entry.Locale = CollectLocales();
            return entry;
        }

        private void OnCopyPressed()
        {
            DisplayServer.ClipboardSet(_previewEdit.Text);
            SetStatus(ToolUi.Tr("tools/catalog_export/copied"), ToolStatusKind.Ok);
        }

        private void OnExportPressed()
        {
            if (_modInfo == null)
                return;

            if (string.IsNullOrWhiteSpace(_urlEdit.Text))
            {
                SetStatus(ToolUi.Tr("tools/catalog_export/url_required"), ToolStatusKind.Error);
                return;
            }

            try
            {
                var entry = BuildExportEntry();
                var modsRoot = ModRegistry.ModsPath;
                var isZip = _modInfo.FileProvider.RootPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
                var path = isZip
                    ? ModCatalogExporter.ExportFromZip(_modInfo, entry, modsRoot)
                    : ModCatalogExporter.ExportFromFolder(_modInfo, entry, modsRoot);
                SetStatus(string.Format(ToolUi.Tr("tools/catalog_export/exported"), path), ToolStatusKind.Ok);
            }
            catch (Exception ex)
            {
                GameLogger.Log("CATALOG_EXPORT", $"export of '{_modInfo.Id}' failed: {ex.Message}", LogLevel.Error);
                SetStatus($"{ToolUi.Tr("tools/catalog_export/export_error")}: {ex.Message}", ToolStatusKind.Error);
            }
        }

        private static List<string> ParseTags(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            return text.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
        }

        private static string NullIfEmpty(string text)
        {
            var trimmed = text?.Trim();
            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }

        private static CatalogExportEntry BuildEntryFromManifest(ModManifest manifest)
        {
            var depends = manifest.Depends?.Select(d => d.ToString()).Where(s => s.Length > 0).ToList();

            return new CatalogExportEntry
            {
                Id = manifest.Id,
                Name = manifest.Name,
                Author = manifest.Author,
                Version = manifest.Version,
                Description = manifest.Description,
                Category = manifest.Category ?? CategoryType.Other,
                Depends = depends,
                ConflictsWith = manifest.ConflictsWith,
            };
        }

        private CategoryType CollectSelectedCategories()
        {
            var selected = (CategoryType)0;
            for (var i = 0; i < Categories.Length && i < _categoryChecks.Count; i++)
            {
                if (_categoryChecks[i].ButtonPressed)
                    selected |= Categories[i];
            }
            return selected != 0 ? selected : CategoryType.Other;
        }
    }
}
