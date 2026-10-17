using System;
using System.Collections.Generic;
using Cthangover.Core.UI;
using Cthangover.Core.UI.Theming;
using Godot;

using Cthangover.Mods.Interface.Base.Lists;
using Cthangover.Mods.Interface.Theming;
namespace Cthangover.Core.UI.Tool.SceneBuilder
{
    /// <summary>
    /// Godot-editor-style property inspector for the scene builder tool. Shows
    /// curated rows from <see cref="InspectorPropertyCatalog"/> for the selected
    /// mod-scene node, applies every edit to the live node via <c>Set()</c> (so
    /// the preview reacts immediately) and reports it through
    /// <see cref="PropertyEdited"/> so the host window can patch the .tscn text.
    /// Initial widget values come from <paramref name="fileValues"/> — the raw
    /// literals stored in the file — because post-layout live values would show
    /// computed sizes instead of authored anchors; keys present in the file are
    /// marked with "*". Controls managed by a Container get a warning banner and
    /// container-relevant rows only (anchors/offsets would be recomputed by the
    /// parent every frame anyway). For Control nodes an anchors-preset dropdown
    /// applies Godot's preset math, then rebuilds all rows so widgets reflect
    /// the resulting anchor/offset values; the rebuild is deferred because it
    /// frees widgets mid-signal. Host must call <see cref="ModWidget.EnsureConstructed"/>
    /// after adding the panel to the tree (ModWidget contract).
    /// </summary>
    public partial class InspectorPanel : ModWidget
    {
        /// <summary>Raised after a live edit: (node, tscn property key, new value).</summary>
        public event Action<Node, string, Variant> PropertyEdited;

        private static readonly string[] PresetNames =
        {
            "Top Left", "Top Right", "Bottom Left", "Bottom Right",
            "Center Left", "Center Top", "Center Right", "Center Bottom",
            "Center", "Left Wide", "Top Wide", "Right Wide",
            "Bottom Wide", "VCenter Wide", "HCenter Wide", "Full Rect"
        };

        private Node _node;
        private bool _suppress;
        private readonly Dictionary<string, InspectableProperty> _propertiesByKey = new();

        private Label _header;
        private Label _hint;
        private Label _banner;
        private HBoxContainer _presetRow;
        private OptionButton _presetDropdown;
        private VBoxContainer _rows;

        protected override void Construct()
        {
            // ModWidget is a plain Control: it neither sizes its child nor
            // reports the child's height. Anchor the column to full width and
            // mirror its height into the minimum size so the hosting
            // ScrollContainer can scroll (and wrapped hints get real width).
            var root = new VBoxContainer();
            root.AddThemeConstantOverride("separation", UiTokens.Space2);
            AddChild(root);
            root.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
            root.MinimumSizeChanged += () => CustomMinimumSize = new Vector2(0, root.GetCombinedMinimumSize().Y);

            _hint = new Label();
            _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _hint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _hint.AddThemeColorOverride("font_color", UiTokens.Faint);
            _hint.Visible = false;
            root.AddChild(_hint);

            _header = new Label();
            _header.AddThemeColorOverride("font_color", UiTokens.InkBright);
            _header.AddThemeFontSizeOverride("font_size", UiTokens.FontBodyLarge);
            _header.Visible = false;
            root.AddChild(_header);

            _banner = new Label();
            _banner.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _banner.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _banner.AddThemeColorOverride("font_color", UiTokens.Warning);
            _banner.AddThemeFontSizeOverride("font_size", UiTokens.FontBodySmall);
            _banner.Visible = false;
            root.AddChild(_banner);

            _presetRow = new HBoxContainer();
            _presetRow.Visible = false;
            root.AddChild(_presetRow);

            var presetLabel = new Label
            {
                Text = TranslationServer.Translate("tools/scene_builder/inspector_preset"),
                TooltipText = "anchors_preset"
            };
            presetLabel.CustomMinimumSize = new Vector2(120f, 0f);
            presetLabel.AddThemeFontSizeOverride("font_size", UiTokens.FontBodySmall);
            presetLabel.AddThemeColorOverride("font_color", UiTokens.Muted);
            _presetRow.AddChild(presetLabel);

            _presetDropdown = new OptionButton();
            _presetDropdown.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            for (var i = 0; i < PresetNames.Length; i++)
                _presetDropdown.AddItem(PresetNames[i]);
            _presetDropdown.ItemSelected += OnPresetSelected;
            _presetRow.AddChild(_presetDropdown);

            _rows = new VBoxContainer();
            _rows.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _rows.AddThemeConstantOverride("separation", 2);
            root.AddChild(_rows);
        }

        /// <summary>
        /// Shows editable rows for a mod-scene node. <paramref name="fileValues"/>
        /// carries the raw tscn literals of this node's section (null when the
        /// section is not in the file yet); they seed widget values and mark
        /// file-stored keys.
        /// </summary>
        public void ShowNode(Node node, IReadOnlyDictionary<string, string> fileValues)
        {
            EnsureConstructed();
            _suppress = true;
            _node = node;

            ClearBody();
            _propertiesByKey.Clear();

            _hint.Visible = false;
            _header.Visible = true;
            _header.Text = node.Name + "  ·  " + node.GetClass();

            var insideContainer = node is Control && node.GetParent() is Container;
            _banner.Visible = insideContainer;
            if (insideContainer)
                _banner.Text = TranslationServer.Translate("tools/scene_builder/inspector_container_banner");

            var isControl = node is Control;
            _presetRow.Visible = isControl && !insideContainer;
            _presetDropdown.Selected = -1;

            foreach (var entry in InspectorPropertyCatalog.Build(node, insideContainer))
            {
                var categoryLabel = UiKit.SectionTitle(TranslationServer.Translate(entry.CategoryKey), UiTokens.FontSmall);
                _rows.AddChild(categoryLabel);

                foreach (var property in entry.Properties)
                {
                    _propertiesByKey[property.Key] = property;
                    AddPropertyRow(property, fileValues);
                }
            }

            _suppress = false;
        }

        /// <summary>Placeholder view (hint/read-only notice) when no editable node is selected.</summary>
        public void ShowMessage(string message)
        {
            EnsureConstructed();
            _suppress = true;
            _node = null;
            _propertiesByKey.Clear();

            ClearBody();
            _header.Visible = false;
            _presetRow.Visible = false;
            _banner.Visible = false;
            _hint.Visible = true;
            _hint.Text = message;
            _suppress = false;
        }

        private void AddPropertyRow(InspectableProperty property, IReadOnlyDictionary<string, string> fileValues)
        {
            var storedInFile = fileValues != null && fileValues.ContainsKey(property.Key);
            string rawValue = null;
            if (storedInFile)
                fileValues.TryGetValue(property.Key, out rawValue);

            var row = new HBoxContainer();

            var labelText = Humanize(property.Key);
            if (storedInFile)
                labelText += " *";
            var label = new Label { Text = labelText, TooltipText = BuildTooltip(property.Key, storedInFile), MouseFilter = MouseFilterEnum.Pass, ClipText = true };
            label.CustomMinimumSize = new Vector2(120f, 0f);
            label.AddThemeFontSizeOverride("font_size", UiTokens.FontBodySmall);
            label.AddThemeColorOverride("font_color", storedInFile ? UiTokens.Ink : UiTokens.Muted);
            label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(label);

            switch (property.Kind)
            {
                case InspectorPropertyKind.Numeric:
                {
                    var initial = rawValue != null && TscnLiteralReader.TryReadFloat(rawValue, out var parsed)
                        ? parsed
                        : ReadDouble(property.Key);
                    var field = new InspectorNumericField();
                    field.Setup(property, initial);
                    field.Committed += OnNumericCommitted;
                    row.AddChild(field);
                    break;
                }
                case InspectorPropertyKind.Vector2:
                {
                    var initial = rawValue != null && TscnLiteralReader.TryReadVector2(rawValue, out var parsed)
                        ? parsed
                        : ReadVector2(property.Key);
                    var field = new InspectorVector2Field();
                    field.Setup(property, initial);
                    field.Committed += OnVectorCommitted;
                    row.AddChild(field);
                    break;
                }
                case InspectorPropertyKind.Bool:
                {
                    var initial = rawValue != null && TscnLiteralReader.TryReadBool(rawValue, out var parsed)
                        ? parsed
                        : ReadBool(property.Key);
                    var field = new InspectorBoolField();
                    field.Setup(property, initial);
                    field.Committed += OnBoolCommitted;
                    row.AddChild(field);
                    break;
                }
                case InspectorPropertyKind.String:
                {
                    var initial = rawValue != null ? TscnLiteralReader.ReadString(rawValue) : null;
                    if (initial == null)
                        initial = ReadString(property.Key);
                    var field = new InspectorTextField();
                    field.Setup(property, initial);
                    field.Committed += OnTextCommitted;
                    row.AddChild(field);
                    break;
                }
                case InspectorPropertyKind.Color:
                {
                    var initial = rawValue != null && TscnLiteralReader.TryReadColor(rawValue, out var parsed)
                        ? parsed
                        : ReadColor(property.Key);
                    var field = new InspectorColorField();
                    field.Setup(property, initial);
                    field.Committed += OnColorCommitted;
                    row.AddChild(field);
                    break;
                }
                case InspectorPropertyKind.Enum:
                {
                    var initial = rawValue != null && TscnLiteralReader.TryReadLong(rawValue, out var parsed)
                        ? parsed
                        : ReadLong(property.Key);
                    var field = new InspectorEnumField();
                    field.Setup(property, initial);
                    field.Committed += OnEnumCommitted;
                    row.AddChild(field);
                    break;
                }
            }

            _rows.AddChild(row);
        }

        private static string BuildTooltip(string key, bool storedInFile)
        {
            return storedInFile
                ? key + "\n" + TranslationServer.Translate("tools/scene_builder/inspector_in_file_tooltip")
                : key;
        }

        private void OnPresetSelected(long index)
        {
            if (_suppress || !(_node is Control control))
                return;

            control.SetAnchorsAndOffsetsPreset((Control.LayoutPreset)index, Control.LayoutPresetMode.KeepSize);

            if (ReadLong("layout_mode") != 1)
                EmitChange("layout_mode", Variant.From(1L));

            EmitChange("anchor_left", Variant.From(control.AnchorLeft));
            EmitChange("anchor_top", Variant.From(control.AnchorTop));
            EmitChange("anchor_right", Variant.From(control.AnchorRight));
            EmitChange("anchor_bottom", Variant.From(control.AnchorBottom));
            EmitChange("offset_left", Variant.From(control.OffsetLeft));
            EmitChange("offset_top", Variant.From(control.OffsetTop));
            EmitChange("offset_right", Variant.From(control.OffsetRight));
            EmitChange("offset_bottom", Variant.From(control.OffsetBottom));
            EmitChange("grow_horizontal", Variant.From((long)(int)control.GrowHorizontal));
            EmitChange("grow_vertical", Variant.From((long)(int)control.GrowVertical));

            // Deferred: this handler runs inside the dropdown's own signal and the
            // resync frees the widget tree being iterated.
            CallDeferred(nameof(ResyncRows));
        }

        private void ResyncRows()
        {
            if (_node == null || !GodotObject.IsInstanceValid(_node))
                return;
            // File values may be stale here; the host refreshes rows through
            // ShowNode after committing text, so a plain rebuild suffices.
            ShowNode(_node, null);
        }

        private void EmitChange(string key, Variant value)
        {
            if (_suppress || _node == null)
                return;

            _node.Set(key, value);
            PropertyEdited?.Invoke(_node, key, value);
        }

        private void OnNumericCommitted(string key, double value)
        {
            var isInteger = _propertiesByKey.TryGetValue(key, out var prop) && prop.IsInteger;
            EmitChange(key, isInteger ? Variant.From((long)System.Math.Round(value)) : Variant.From(value));
        }

        private void OnVectorCommitted(string key, Vector2 value)
        {
            EmitChange(key, Variant.From(value));
        }

        private void OnBoolCommitted(string key, bool pressed)
        {
            EmitChange(key, Variant.From(pressed));
        }

        private void OnTextCommitted(string key, string text)
        {
            EmitChange(key, Variant.From(text));
        }

        private void OnColorCommitted(string key, Color color)
        {
            EmitChange(key, Variant.From(color));
        }

        private void OnEnumCommitted(string key, long value)
        {
            EmitChange(key, Variant.From(value));
        }

        private void ClearBody()
        {
            foreach (var child in _rows.GetChildren())
            {
                _rows.RemoveChild(child);
                child.Free();
            }
        }

        private double ReadDouble(string key)
        {
            var v = _node.Get(key);
            return v.VariantType == Variant.Type.Nil ? 0d : v.AsDouble();
        }

        private long ReadLong(string key)
        {
            var v = _node.Get(key);
            if (v.VariantType == Variant.Type.Int)
                return v.AsInt64();
            if (v.VariantType == Variant.Type.Float)
                return (long)v.AsDouble();
            return 0;
        }

        private bool ReadBool(string key)
        {
            var v = _node.Get(key);
            return v.VariantType != Variant.Type.Nil && v.AsBool();
        }

        private string ReadString(string key)
        {
            var v = _node.Get(key);
            return v.VariantType == Variant.Type.Nil ? "" : v.AsString();
        }

        private Color ReadColor(string key)
        {
            var v = _node.Get(key);
            return v.VariantType == Variant.Type.Nil ? UiTokens.White : v.AsColor();
        }

        private Vector2 ReadVector2(string key)
        {
            var v = _node.Get(key);
            return v.VariantType == Variant.Type.Nil ? Vector2.Zero : v.AsVector2();
        }

        private static string Humanize(string key)
        {
            var parts = key.Split('_');
            for (var i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0)
                    continue;
                parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
            }
            return string.Join(" ", parts);
        }
    }
}
