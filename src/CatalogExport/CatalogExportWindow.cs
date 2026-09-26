using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Cthangover.Core.Mods;
using Cthangover.Core.UI.Theming;
using Cthangover.Core.UI.Tool;
using Cthangover.Core.Utils;
using Godot;

namespace Cthangover.Core.UI.Tool.CatalogExport
{
    /// <summary>
    /// Developer tool that builds a catalog entry (JSON for the online mod
    /// catalog) from a locally installed mod's manifest. Ported out of the old
    /// mods menu so that mod-management UI can live in the <c>mod_manager</c>
    /// mod while this authoring utility stays a tool.
    /// <para/>
    /// The window lets the author pick any discovered mod, fill in the
    /// catalog-only metadata (download URL, category, tags, changelog, status,
    /// minimum game version, icon URL, localized names/descriptions), preview
    /// the resulting JSON, copy it to the clipboard, or export it next to the
    /// mod's zip via <see cref="ModCatalogExporter"/>.
    /// </summary>
    public partial class CatalogExportWindow : ToolWindow
    {
        private static readonly string[] Statuses = { "stable", "beta", "deprecated", "archived" };
        private static readonly CategoryType[] Categories = Enum.GetValues<CategoryType>();

        private OptionButton _modSelect;
        private VBoxContainer _formHost;

        private ModInfo _modInfo;
        private ModManifest _manifest;
        private LineEdit _urlEdit;
        private ItemList _categoryList;
        private LineEdit _tagsEdit;
        private TextEdit _changelogEdit;
        private OptionButton _statusOpt;
        private LineEdit _minGameVerEdit;
        private LineEdit _iconUrlEdit;
        private VBoxContainer _localeContainer;
        private TextEdit _previewEdit;

        public CatalogExportWindow() : base("mods/catalog_title")
        {
            Size = new Vector2I(620, 720);
            CthangoverTheme.Apply(this);

            var vbox = CreateFillContainer();
            vbox.AddThemeConstantOverride("separation", 8);
            AddChild(vbox);

            var selectRow = new HBoxContainer();
            selectRow.AddThemeConstantOverride("separation", 8);
            var selectLabel = new Label { Text = TranslationServer.Translate("mods/catalog_select_mod") };
            selectLabel.AddThemeColorOverride("font_color", UiTokens.Muted);
            selectRow.AddChild(selectLabel);

            _modSelect = new OptionButton
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            _modSelect.ItemSelected += OnModSelected;
            selectRow.AddChild(_modSelect);
            vbox.AddChild(selectRow);

            var scroll = new ScrollContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            };
            vbox.AddChild(scroll);

            _formHost = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            _formHost.AddThemeConstantOverride("separation", 8);
            scroll.AddChild(_formHost);

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

                _modSelect.AddItem(info.DisplayTitle);
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

            _modInfo = ModRegistry.Instance.GetMod(modId) as ModInfo;
            _manifest = _modInfo?.Manifest;
            if (_modInfo == null || _manifest == null)
                return;

            AddSectionLabel(_formHost, "Mod Info");
            AddReadOnlyField(_formHost, "mods/id", _modInfo.Id);
            AddReadOnlyField(_formHost, "mods/name", _modInfo.DisplayTitle);
            AddReadOnlyField(_formHost, "mods/version", _modInfo.Version ?? "—");
            AddReadOnlyField(_formHost, "mods/author", _modInfo.Author ?? "—");

            AddSectionLabel(_formHost, "Catalog Metadata");
            _urlEdit = AddLineEdit(_formHost, "mods/catalog_download_url");

            _categoryList = new ItemList
            {
                CustomMinimumSize = new Vector2(0, 130),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SelectMode = ItemList.SelectModeEnum.Toggle,
            };
            var preselected = _manifest.Category ?? CategoryType.Other;
            for (var i = 0; i < Categories.Length; i++)
            {
                _categoryList.AddItem(GetCategoryDisplayName(Categories[i]));
                if ((preselected & Categories[i]) != 0)
                    _categoryList.Select(i, false);
            }
            AddLabeledControl(_formHost, "mods/catalog_category", _categoryList);

            _tagsEdit = AddLineEdit(_formHost, "mods/catalog_tags");

            _changelogEdit = new TextEdit
            {
                CustomMinimumSize = new Vector2(0, 60),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                WrapMode = TextEdit.LineWrappingMode.Boundary,
            };
            AddLabeledControl(_formHost, "mods/catalog_changelog", _changelogEdit);

            _statusOpt = AddOptionButton(_formHost, "mods/catalog_status", Statuses);

            _minGameVerEdit = AddLineEdit(_formHost, "mods/catalog_min_game_version");
            _iconUrlEdit = AddLineEdit(_formHost, "mods/catalog_icon_url");

            AddSectionLabel(_formHost, "JSON Preview");
            _previewEdit = new TextEdit
            {
                CustomMinimumSize = new Vector2(0, 160),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                Editable = false,
                WrapMode = TextEdit.LineWrappingMode.None,
            };
            _previewEdit.AddThemeFontSizeOverride("font_size", UiTokens.FontSmall);
            _formHost.AddChild(_previewEdit);

            var btnRow = new HBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            btnRow.AddThemeConstantOverride("separation", 8);

            var copyBtn = new Button
            {
                Text = TranslationServer.Translate("mods/catalog_copy"),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            copyBtn.Pressed += OnCopyPressed;
            btnRow.AddChild(copyBtn);

            var exportBtn = new Button
            {
                Text = TranslationServer.Translate("mods/catalog_export_file"),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            exportBtn.Pressed += OnExportPressed;
            btnRow.AddChild(exportBtn);

            var closeBtn = new Button
            {
                Text = TranslationServer.Translate("mods/catalog_close"),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            closeBtn.Pressed += () => QueueFree();
            btnRow.AddChild(closeBtn);

            _formHost.AddChild(btnRow);

            AddSectionLabel(_formHost, "Locales");
            _localeContainer = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            _localeContainer.AddThemeConstantOverride("separation", 4);
            _formHost.AddChild(_localeContainer);

            var addLocaleBtn = new Button
            {
                Text = TranslationServer.Translate("mods/catalog_add_locale"),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            addLocaleBtn.Pressed += () => AddLocaleRow(string.Empty, string.Empty, string.Empty);
            _formHost.AddChild(addLocaleBtn);

            _urlEdit.TextChanged += _ => UpdatePreview();
            _categoryList.ItemSelected += _ => UpdatePreview();
            _categoryList.MultiSelected += (_, _) => UpdatePreview();
            _tagsEdit.TextChanged += _ => UpdatePreview();
            _changelogEdit.TextChanged += UpdatePreview;
            _statusOpt.ItemSelected += _ => UpdatePreview();
            _minGameVerEdit.TextChanged += _ => UpdatePreview();
            _iconUrlEdit.TextChanged += _ => UpdatePreview();

            UpdatePreview();
        }

        private static string GetCategoryDisplayName(CategoryType cat) => cat switch
        {
            CategoryType.Story => TranslationServer.Translate("mods/category_story"),
            CategoryType.Characters => TranslationServer.Translate("mods/category_characters"),
            CategoryType.Gameplay => TranslationServer.Translate("mods/category_gameplay"),
            CategoryType.Visual => TranslationServer.Translate("mods/category_visual"),
            CategoryType.Audio => TranslationServer.Translate("mods/category_audio"),
            CategoryType.UI => TranslationServer.Translate("mods/category_ui"),
            CategoryType.Locale => TranslationServer.Translate("mods/category_locale"),
            CategoryType.Library => TranslationServer.Translate("mods/category_library"),
            CategoryType.Engine => TranslationServer.Translate("mods/category_engine"),
            CategoryType.Overhaul => TranslationServer.Translate("mods/category_overhaul"),
            CategoryType.Tools => TranslationServer.Translate("mods/category_tools"),
            CategoryType.Editor => TranslationServer.Translate("mods/category_editor"),
            CategoryType.Debug => TranslationServer.Translate("mods/category_debug"),
            CategoryType.Other => TranslationServer.Translate("mods/category_other"),
            _ => cat.ToString(),
        };

        private void AddLocaleRow(string lang, string name, string description)
        {
            var row = new HBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            row.AddThemeConstantOverride("separation", 4);

            var langEdit = new LineEdit
            {
                CustomMinimumSize = new Vector2(44, 0),
                PlaceholderText = "en",
                MaxLength = 5,
                Text = lang,
            };
            row.AddChild(langEdit);

            var nameEdit = new LineEdit
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                PlaceholderText = TranslationServer.Translate("mods/catalog_locale_name"),
                Text = name,
            };
            row.AddChild(nameEdit);

            var descEdit = new LineEdit
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                PlaceholderText = TranslationServer.Translate("mods/catalog_locale_desc"),
                Text = description,
            };
            row.AddChild(descEdit);

            var removeBtn = new Button { Text = "×", CustomMinimumSize = new Vector2(28, 0) };
            removeBtn.Pressed += () => RemoveLocaleRow(row);
            row.AddChild(removeBtn);

            langEdit.TextChanged += _ => UpdatePreview();
            nameEdit.TextChanged += _ => UpdatePreview();
            descEdit.TextChanged += _ => UpdatePreview();

            _localeContainer.AddChild(row);
        }

        private void RemoveLocaleRow(HBoxContainer row)
        {
            _localeContainer.RemoveChild(row);
            row.QueueFree();
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (_previewEdit == null || _modInfo == null || _manifest == null)
                return;

            var entry = BuildEntryFromManifest(_manifest);
            entry.Downloads = new List<CatalogEntryDownload>
            {
                new()
                {
                    Version = entry.Version,
                    Urls = new List<string> { _urlEdit.Text.Trim() },
                    SizeBytes = 0,
                    ArchiveSha256 = "—",
                    InstallSha256 = "—",
                },
            };
            entry.Category = CollectSelectedCategories(_categoryList, Categories);
            entry.Tags = ParseTags(_tagsEdit.Text);
            entry.Downloads[0].Changelog = NullIfEmpty(_changelogEdit.Text);
            entry.Status = Statuses[_statusOpt.Selected];
            entry.MinGameVersion = NullIfEmpty(_minGameVerEdit.Text);
            entry.IconUrl = NullIfEmpty(_iconUrlEdit.Text);
            entry.Locale = CollectLocales();
            _previewEdit.Text = JsonSerializer.Serialize(entry, JsonOptionsProvider.WriteIndentedIgnoreNullOptions);
        }

        private List<CatalogLocaleEntry> CollectLocales()
        {
            var list = new List<CatalogLocaleEntry>();
            foreach (var child in _localeContainer.GetChildren())
            {
                if (child is not HBoxContainer row || row.GetChildCount() < 3)
                    continue;

                var lang = ((LineEdit)row.GetChild(0)).Text.Trim();
                var name = ((LineEdit)row.GetChild(1)).Text.Trim();
                var description = ((LineEdit)row.GetChild(2)).Text.Trim();
                if (lang.Length == 0)
                    continue;

                list.Add(new CatalogLocaleEntry
                {
                    Lang = lang,
                    Name = NullIfEmpty(name),
                    Description = NullIfEmpty(description),
                });
            }

            if (list.Count == 0 && _manifest != null)
            {
                list.Add(new CatalogLocaleEntry
                {
                    Lang = "en",
                    Name = _manifest.Name,
                    Description = _manifest.Description,
                });
            }

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
                },
            };
            entry.Category = CollectSelectedCategories(_categoryList, Categories);
            entry.Tags = ParseTags(_tagsEdit.Text);
            entry.Downloads[0].Changelog = NullIfEmpty(_changelogEdit.Text);
            entry.Status = Statuses[_statusOpt.Selected];
            entry.MinGameVersion = NullIfEmpty(_minGameVerEdit.Text);
            entry.IconUrl = NullIfEmpty(_iconUrlEdit.Text);
            entry.Locale = CollectLocales();
            return entry;
        }

        private void OnCopyPressed()
        {
            DisplayServer.ClipboardSet(_previewEdit.Text);
            GameLogger.Log("CATALOG_EXPORT", $"Copied catalog JSON for '{_modInfo?.Id}' to clipboard");
        }

        private void OnExportPressed()
        {
            if (_modInfo == null)
                return;

            var entry = BuildExportEntry();
            var modsRoot = ModRegistry.ModsPath;
            var isZip = _modInfo.FileProvider.RootPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            if (isZip)
                ModCatalogExporter.ExportFromZip(_modInfo, entry, modsRoot);
            else
                ModCatalogExporter.ExportFromFolder(_modInfo, entry, modsRoot);

            QueueFree();
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

        private static CategoryType CollectSelectedCategories(ItemList list, CategoryType[] categories)
        {
            var selected = (CategoryType)0;
            for (var i = 0; i < categories.Length && i < list.ItemCount; i++)
            {
                if (list.IsSelected(i))
                    selected |= categories[i];
            }
            return selected != 0 ? selected : CategoryType.Other;
        }

        private static void AddLabeledControl(Container parent, string labelKey, Control control)
        {
            var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var label = new Label
            {
                Text = TranslationServer.Translate(labelKey),
            };
            label.AddThemeFontSizeOverride("font_size", UiTokens.FontBodySmall);
            label.AddThemeColorOverride("font_color", UiTokens.Muted);
            box.AddChild(label);
            box.AddChild(control);
            parent.AddChild(box);
        }

        private static void AddReadOnlyField(Container parent, string labelKey, string value)
        {
            var label = new Label
            {
                Text = $"{TranslationServer.Translate(labelKey)}: {value}",
            };
            label.AddThemeFontSizeOverride("font_size", UiTokens.FontBody);
            label.AddThemeColorOverride("font_color", UiTokens.Body);
            parent.AddChild(label);
        }

        private static LineEdit AddLineEdit(Container parent, string labelKey)
        {
            var edit = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            AddLabeledControl(parent, labelKey, edit);
            return edit;
        }

        private static OptionButton AddOptionButton(Container parent, string labelKey, string[] items)
        {
            var opt = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            foreach (var item in items)
                opt.AddItem(item);
            AddLabeledControl(parent, labelKey, opt);
            return opt;
        }

        private static void AddSectionLabel(Container parent, string text)
        {
            var sep = new HSeparator();
            parent.AddChild(sep);
            var label = new Label { Text = text };
            label.AddThemeFontSizeOverride("font_size", UiTokens.FontBodyLarge);
            label.AddThemeColorOverride("font_color", UiTokens.Ink);
            parent.AddChild(label);
        }
    }
}
