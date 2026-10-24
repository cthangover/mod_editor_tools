namespace Cthangover.Tools.Schemas
{
    using Cthangover.Mods.Schemas;
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Nodes;
    using Cthangover.Core.UI.Theming;
    using Cthangover.Tools.Common;
    using Cthangover.Tools.EntityEditor;
    using Cthangover.Tools.Services;
    using Godot;

    /// <summary>
    /// Edits a <c>locale</c> field: the locale key the JSON holds and, below it, the text of
    /// that key in every language of the mod — the way the item and character editors do,
    /// so a beginner types names and a professional can still rename the key. Texts go
    /// through the <see cref="EntityDocument"/> and are written by the window's Save.
    /// </summary>
    public class LocaleFieldEditor : ISchemaFieldEditor
    {
        private readonly FieldSchema field;
        private readonly Action changed;
        private readonly LineEdit keyEdit;
        private readonly VBoxContainer texts;
        private readonly Dictionary<string, LineEdit> edits = new();
        private EntityDocument document;
        private string modId;
        private bool suppress;

        /// <inheritdoc />
        public FieldSchema Field => field;

        /// <summary>Adds the editor for <paramref name="field"/> to <paramref name="parent"/>.</summary>
        public LocaleFieldEditor(Container parent, FieldSchema field, Action changed)
        {
            this.field = field;
            this.changed = changed;
            keyEdit = ToolUi.Field(parent, string.IsNullOrEmpty(field.Name) ? field.Key : field.Name, ToolUi.Text(string.Empty, OnTyped), "tools/entity_editor/locale_key_tip");
            keyEdit.Name = "Key_" + field.Key;
            keyEdit.AddThemeFontOverride("font", ToolUi.Mono());
            texts = new VBoxContainer();
            parent.AddChild(texts);
        }

        /// <summary>Builds one text row per language of <paramref name="mod"/>.</summary>
        public void Open(EntityDocument doc, string mod, IEnumerable<string> languages)
        {
            document = doc;
            modId = mod;
            foreach (var child in texts.GetChildren())
            {
                texts.RemoveChild(child);
                child.QueueFree();
            }
            edits.Clear();
            foreach (var language in languages)
            {
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", UiTokens.Space3);
                row.AddChild(new Label { Text = string.Format(ToolUi.Tr("tools/entity_editor/text_in"), language), CustomMinimumSize = new Vector2(ToolUi.LabelWidth, 0), HorizontalAlignment = HorizontalAlignment.Right });
                var edit = ToolUi.Text(string.Empty, OnTyped);
                edit.Name = "Text_" + field.Key + "_" + language;
                edit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                row.AddChild(edit);
                texts.AddChild(row);
                edits[language] = edit;
            }
        }

        /// <summary>Shows the key in <paramref name="node"/> and its texts.</summary>
        public void Load(JsonNode node)
        {
            suppress = true;
            var key = node?.ToString() ?? string.Empty;
            keyEdit.Text = key;
            foreach (var pair in edits)
                pair.Value.Text = TextOf(pair.Key, key);
            suppress = false;
        }

        /// <summary>The key for the JSON (null when empty); texts are put into the document.</summary>
        public JsonNode Save()
        {
            var key = keyEdit.Text.Trim();
            if (key.Length == 0)
                return null;
            foreach (var pair in edits)
                document?.SetText(pair.Key, key, pair.Value.Text);
            return JsonValue.Create(key);
        }

        private string TextOf(string language, string key)
        {
            if (string.IsNullOrEmpty(key) || document == null)
                return string.Empty;
            var text = document.GetText(language, key);
            if (text != null)
                return text;
            text = LocaleFileService.Read(modId, language, key) ?? string.Empty;
            document.LoadText(language, key, text);
            return text;
        }

        private void OnTyped(string text)
        {
            if (!suppress)
                changed?.Invoke();
        }
    }
}
