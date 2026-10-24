namespace Cthangover.Tools.Schemas
{
    using Cthangover.Mods.Schemas;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Cthangover.Mods.Interface.Theming;
    using Cthangover.Tools.Common;
    using Cthangover.Tools.EntityEditor;
    using Godot;

    /// <summary>
    /// The form control of one scalar <see cref="FieldSchema"/>, chosen by its type, as a
    /// "label | editor" row. Everything reads and writes a text value, the way the DSL and
    /// most JSON fields hold it, so the palette and the data editor window share these rows:
    /// <list type="bullet">
    /// <item><c>bool</c> — a check box ("1" or empty);</item>
    /// <item><c>choice</c>, <c>ref</c>, <c>who</c> — a drop-down of the options, of the entities of
    ///   the kind (by name), of player/party/characters;</item>
    /// <item><c>ref_list</c> — a comma list with a drop-down that appends an entity;</item>
    /// <item><c>picture</c> — a path with the picture grid of the editors;</item>
    /// <item>anything else — a text box (monospace for formulas, conditions and JSON).</item>
    /// </list>
    /// A current value the options do not know is kept and shown first, so opening and
    /// saving never loses data the schema did not foresee.
    /// </summary>
    public class SchemaFieldControl
    {
        private readonly Action changed;
        private readonly LineEdit text;
        private readonly OptionButton picker;
        private readonly CheckBox check;
        private readonly List<string> pickerValues = new();
        private bool suppress;

        /// <summary>Adds the row for <paramref name="field"/> to <paramref name="parent"/>; <paramref name="changed"/> runs on every edit.</summary>
        public SchemaFieldControl(Container parent, FieldSchema field, Action changed)
        {
            Field = field;
            this.changed = changed;
            var label = string.IsNullOrEmpty(field.Name) ? field.Key : field.Name;
            var tip = string.IsNullOrEmpty(field.Description) ? null : field.Description;
            switch (Type)
            {
                case "bool":
                    check = new CheckBox();
                    check.Toggled += OnToggled;
                    Editor = ToolUi.Field(parent, label, check, tip);
                    break;
                case "choice":
                case "ref":
                case "who":
                    picker = new OptionButton { Name = "Picker_" + field.Key };
                    picker.ItemSelected += OnPicked;
                    Editor = ToolUi.Field(parent, label, picker, tip);
                    break;
                case "ref_list":
                case "picture":
                    var row = new HBoxContainer();
                    text = ToolUi.Text(field.Default ?? string.Empty, OnTyped);
                    text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                    row.AddChild(text);
                    if (Type == "picture")
                    {
                        var pick = UiKit.IconButton("…", ToolUi.Tr("tools/entity_editor/picture_pick_tip"));
                        pick.Pressed += () => OpenPicture(row);
                        row.AddChild(pick);
                    }
                    else
                    {
                        picker = new OptionButton { TooltipText = ToolUi.Tr("tools/schema/add_ref") };
                        picker.ItemSelected += OnAppend;
                        row.AddChild(picker);
                    }
                    Editor = ToolUi.Field(parent, label, row, tip);
                    break;
                default:
                    text = ToolUi.Text(field.Default ?? string.Empty, OnTyped);
                    text.Name = "Text_" + field.Key;
                    if (Type is "formula" or "condition" or "json")
                        text.AddThemeFontOverride("font", ToolUi.Mono());
                    Editor = ToolUi.Field(parent, label, text, tip);
                    break;
            }
            FillPicker(null);
        }

        /// <summary>The described field.</summary>
        public FieldSchema Field { get; }

        /// <summary>The editor control (the row's right part).</summary>
        public Control Editor { get; }

        /// <summary>The field type in lowercase (<c>text</c> when the schema names none).</summary>
        public string Type => (Field.Type ?? "text").ToLowerInvariant();

        /// <summary>The current value as text; empty when not given.</summary>
        public string Value
        {
            get
            {
                if (check != null)
                    return check.ButtonPressed ? "1" : string.Empty;
                if (text != null)
                    return text.Text.Trim();
                return picker != null && picker.Selected >= 0 && picker.Selected < pickerValues.Count ? pickerValues[picker.Selected] : string.Empty;
            }
            set
            {
                suppress = true;
                if (check != null)
                    check.ButtonPressed = value is "1" or "true" or "True" or "yes";
                else if (text != null)
                    text.Text = value ?? string.Empty;
                else
                    FillPicker(value ?? string.Empty);
                suppress = false;
            }
        }

        private void FillPicker(string current)
        {
            if (picker == null)
                return;
            picker.Clear();
            pickerValues.Clear();
            var appendOnly = Type == "ref_list";
            Add(string.Empty, appendOnly ? ToolUi.Tr("tools/schema/add_ref") : "—");
            if (!appendOnly && !string.IsNullOrEmpty(current) && !Options().Contains(current, StringComparer.OrdinalIgnoreCase))
                Add(current, current);
            foreach (var option in Options())
                Add(option, Type == "choice" ? option : GameEntityIndex.Label(KindOfOptions, option));
            var index = appendOnly ? 0 : Math.Max(0, pickerValues.FindIndex(v => string.Equals(v, current, StringComparison.OrdinalIgnoreCase)));
            picker.Select(index);
        }

        private void Add(string value, string label)
        {
            pickerValues.Add(value);
            picker.AddItem(label);
        }

        private string KindOfOptions => Type == "who" ? "character" : Field.Kind;

        private IEnumerable<string> Options()
        {
            switch (Type)
            {
                case "choice":
                    return Field.Options ?? new List<string>();
                case "who":
                    return new[] { "player", "party" }.Concat(GameEntityIndex.Of("character").Keys);
                default:
                    return GameEntityIndex.Of(Field.Kind).Keys;
            }
        }

        private void OnTyped(string value)
        {
            if (!suppress)
                changed?.Invoke();
        }

        private void OnToggled(bool on)
        {
            if (!suppress)
                changed?.Invoke();
        }

        private void OnPicked(long index)
        {
            if (!suppress)
                changed?.Invoke();
        }

        private void OnAppend(long index)
        {
            if (suppress || index <= 0 || index >= pickerValues.Count)
                return;
            var items = text.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
            if (!items.Contains(pickerValues[(int)index]))
                items.Add(pickerValues[(int)index]);
            text.Text = string.Join(",", items);
            picker.Select(0);
            changed?.Invoke();
        }

        private void OpenPicture(Node host)
        {
            var dialog = new PicturePickerDialog(Field.Group ?? "items", text.Text);
            dialog.Picked += OnPicturePicked;
            host.AddChild(dialog);
            dialog.PopupCentered();
        }

        private void OnPicturePicked(string id)
        {
            text.Text = id;
            changed?.Invoke();
        }
    }
}
