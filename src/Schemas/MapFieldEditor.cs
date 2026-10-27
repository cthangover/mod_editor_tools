namespace Cthangover.Tools.Schemas
{
    using Cthangover.Mods.Schemas;
    using SchemaFieldDefinition = Cthangover.Mods.Schemas.FieldSchema;
    using SchemaJsonValues = Cthangover.Mods.Schemas.SchemaJson;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json.Nodes;
    using Cthangover.Core.UI.Theming;
    using Cthangover.Mods.Interface.Theming;
    using Cthangover.Tools.Common;
    using Godot;

    /// <summary>
    /// Edits a <c>map</c> field — an object whose keys are ids of <see cref="SchemaFieldDefinition.Kind"/>
    /// and whose values are numbers or formulas (a character's own <c>Stats</c>, an archetype's
    /// growth): one row per key with a key picker, a value box and a remove button. A number
    /// is written as a number, anything else as a formula text.
    /// </summary>
    public class MapFieldEditor : ISchemaFieldEditor
    {
        private readonly SchemaFieldDefinition field;
        private readonly Action changed;
        private readonly VBoxContainer lines;
        private readonly List<(OptionButton Key, List<string> Keys, LineEdit Value, Control Row)> rows = new();
        private bool suppress;

        /// <inheritdoc />
        public SchemaFieldDefinition Field => field;

        /// <summary>Adds the editor for <paramref name="field"/> to <paramref name="parent"/>.</summary>
        public MapFieldEditor(Container parent, SchemaFieldDefinition field, Action changed)
        {
            this.field = field;
            this.changed = changed;
            var box = new VBoxContainer { Name = "Map_" + field.Key };
            lines = new VBoxContainer();
            lines.AddThemeConstantOverride("separation", UiTokens.Space1);
            box.AddChild(lines);
            var add = ToolUi.Button("tools/schema/map_add", AddEmpty, ButtonKind.Secondary);
            add.Name = "Add";
            add.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            box.AddChild(add);
            ToolUi.Field(parent, string.IsNullOrEmpty(field.Name) ? field.Key : field.Name, box, field.Description);
        }

        /// <summary>Shows the object <paramref name="node"/>.</summary>
        public void Load(JsonNode node)
        {
            suppress = true;
            foreach (var row in rows)
            {
                lines.RemoveChild(row.Row);
                row.Row.QueueFree();
            }
            rows.Clear();
            if (node is JsonObject obj)
            {
                foreach (var pair in obj)
                    AddRow(pair.Key, SchemaJsonValues.ToText(pair.Value));
            }
            suppress = false;
        }

        /// <summary>The edited object, or null when empty.</summary>
        public JsonNode Save()
        {
            var obj = new JsonObject();
            foreach (var row in rows)
            {
                var key = row.Key.Selected >= 0 && row.Key.Selected < row.Keys.Count ? row.Keys[row.Key.Selected] : null;
                if (!string.IsNullOrEmpty(key) && row.Value.Text.Trim().Length > 0)
                    obj[key] = SchemaJsonValues.NumberOrText(row.Value.Text.Trim());
            }
            return obj.Count == 0 ? null : obj;
        }

        private void AddEmpty()
        {
            AddRow(null, string.Empty);
            changed?.Invoke();
        }

        private void AddRow(string key, string value)
        {
            var row = new HBoxContainer { Name = "Row" + rows.Count };
            row.AddThemeConstantOverride("separation", UiTokens.Space2);
            var keys = new List<string> { string.Empty };
            if (!string.IsNullOrEmpty(key) && !GameEntityIndex.Of(field.Kind).ContainsKey(key))
                keys.Add(key);
            keys.AddRange(GameEntityIndex.Of(field.Kind).Keys);
            var picker = new OptionButton { CustomMinimumSize = new Vector2(200, 0) };
            foreach (var k in keys)
                picker.AddItem(k.Length == 0 ? "—" : GameEntityIndex.Label(field.Kind, k));
            picker.Select(Math.Max(0, keys.FindIndex(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase))));
            picker.ItemSelected += OnPicked;
            row.AddChild(picker);
            var edit = ToolUi.Text(field.Default ?? string.Empty, OnTyped);
            edit.Text = value ?? string.Empty;
            edit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            edit.AddThemeFontOverride("font", ToolUi.Mono());
            row.AddChild(edit);
            var entry = (picker, keys, edit, (Control)row);
            row.AddChild(ToolUi.Button("tools/schema/list_remove", () => Remove(entry), ButtonKind.Secondary));
            rows.Add(entry);
            lines.AddChild(row);
        }

        private void Remove((OptionButton Key, List<string> Keys, LineEdit Value, Control Row) entry)
        {
            rows.Remove(entry);
            lines.RemoveChild(entry.Row);
            entry.Row.QueueFree();
            changed?.Invoke();
        }

        private void OnPicked(long index)
        {
            if (!suppress)
                changed?.Invoke();
        }

        private void OnTyped(string text)
        {
            if (!suppress)
                changed?.Invoke();
        }
    }
}
