namespace Cthangover.Tools.Schemas
{
    using Cthangover.Mods.Schemas;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json.Nodes;
    using Cthangover.Core.UI.Theming;
    using Cthangover.Mods.Interface.Theming;
    using Cthangover.Tools.Common;
    using Godot;

    /// <summary>
    /// Edits a <c>list</c> field — an array of objects described by <see cref="FieldSchema.Of"/>
    /// (an item's traits, an archetype's learnings, a shop's goods) — as a stack of small
    /// cards with an add and a remove button, each card a form of the sub-fields.
    /// <para/>
    /// The <c>values</c> type is the same editor over the engine's older list of
    /// <c>"Key=Value"</c> strings (a battle action's <c>Values</c>): known keys become fields,
    /// entries the schema does not know are kept as they were.
    /// </summary>
    public class ListFieldEditor : ISchemaFieldEditor
    {
        private readonly FieldSchema field;
        private readonly Action changed;
        private readonly VBoxContainer cards;
        private readonly List<List<SchemaFieldControl>> rows = new();
        private readonly List<string> unknownValues = new();
        private bool suppress;

        /// <inheritdoc />
        public FieldSchema Field => field;

        /// <summary>Adds the editor for <paramref name="field"/> to <paramref name="parent"/>.</summary>
        public ListFieldEditor(Container parent, FieldSchema field, Action changed)
        {
            this.field = field;
            this.changed = changed;
            var box = new VBoxContainer { Name = "List_" + field.Key };
            box.AddThemeConstantOverride("separation", UiTokens.Space2);
            cards = new VBoxContainer();
            cards.AddThemeConstantOverride("separation", UiTokens.Space2);
            box.AddChild(cards);
            var add = ToolUi.Button("tools/schema/list_add", AddEmpty, ButtonKind.Secondary);
            add.Name = "Add";
            add.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            box.AddChild(add);
            ToolUi.Field(parent, string.IsNullOrEmpty(field.Name) ? field.Key : field.Name, box, field.Description);
        }

        private bool IsValues => string.Equals(field.Type, "values", StringComparison.OrdinalIgnoreCase);

        /// <summary>Shows the array <paramref name="node"/> (null or not an array: empty).</summary>
        public void Load(JsonNode node)
        {
            suppress = true;
            Clear();
            unknownValues.Clear();
            if (node is JsonArray array)
            {
                if (IsValues)
                    LoadValues(array);
                else
                    foreach (var item in array.OfType<JsonObject>())
                        AddCard(item);
            }
            suppress = false;
        }

        /// <summary>The edited array, or null when it has no entries (the key is then removed).</summary>
        public JsonNode Save()
        {
            var array = new JsonArray();
            if (IsValues)
            {
                foreach (var card in rows)
                {
                    foreach (var control in card.Where(c => c.Value.Length > 0))
                        array.Add(control.Field.Key + "=" + control.Value);
                }
                foreach (var raw in unknownValues)
                    array.Add(raw);
                return array.Count == 0 ? null : array;
            }
            foreach (var card in rows)
            {
                var item = new JsonObject();
                foreach (var control in card.Where(c => c.Value.Length > 0))
                    item[control.Field.Key] = SchemaJson.ToNode(control.Field, control.Value);
                if (item.Count > 0)
                    array.Add(item);
            }
            return array.Count == 0 ? null : array;
        }

        private void LoadValues(JsonArray array)
        {
            var known = new JsonObject();
            foreach (var raw in array.Select(n => n?.ToString()).Where(s => s != null))
            {
                var eq = raw.IndexOf('=');
                var key = eq > 0 ? raw.Substring(0, eq) : raw;
                var def = field.Of?.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
                if (def == null || eq <= 0)
                    unknownValues.Add(raw);
                else
                    known[def.Key] = raw.Substring(eq + 1);
            }
            AddCard(known);
        }

        private void AddEmpty()
        {
            AddCard(new JsonObject());
            if (!suppress)
                changed?.Invoke();
        }

        private void AddCard(JsonObject item)
        {
            if (IsValues && rows.Count > 0)
                return;
            var card = new PanelContainer { Name = "Card" + rows.Count };
            card.AddThemeStyleboxOverride("panel", UiKit.Frame(UiTokens.RadiusSm, UiTokens.Well, UiTokens.LineSoft));
            var column = new VBoxContainer();
            card.AddChild(column);
            var controls = new List<SchemaFieldControl>();
            foreach (var sub in field.Of ?? new List<FieldSchema>())
            {
                var control = new SchemaFieldControl(column, sub, OnChanged);
                control.Value = SchemaJson.ToText(Find(item, sub.Key));
                controls.Add(control);
            }
            if (!IsValues)
            {
                var remove = ToolUi.Button("tools/schema/list_remove", () => Remove(card, controls), ButtonKind.Secondary);
                remove.Name = "Remove";
                remove.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
                column.AddChild(remove);
            }
            rows.Add(controls);
            cards.AddChild(card);
        }

        private static JsonNode Find(JsonObject item, string key)
            => item.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

        private void Remove(Control card, List<SchemaFieldControl> controls)
        {
            rows.Remove(controls);
            cards.RemoveChild(card);
            card.QueueFree();
            changed?.Invoke();
        }

        private void OnChanged()
        {
            if (!suppress)
                changed?.Invoke();
        }

        private void Clear()
        {
            rows.Clear();
            foreach (var child in cards.GetChildren())
            {
                cards.RemoveChild(child);
                child.QueueFree();
            }
        }
    }
}
