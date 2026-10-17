using System;
using Cthangover.Core.UI.Theming;
using Cthangover.Mods.Interface.Theming;
using Cthangover.Mods.Interface.Tool;
using Godot;

namespace Cthangover.Tools.Common
{
    /// <summary>
    /// Building blocks shared by every editor window of the tools mod, so all
    /// of them read the same way: uppercase muted section captions, "label +
    /// control" rows with a fixed label column, numeric spin boxes and token
    /// buttons from <see cref="UiKit"/>. Editors are built in code (the
    /// skill-code exception for utility tools), so this is the place where
    /// their look is kept consistent with the style guide instead of each
    /// window inventing its own spacing and label styles.
    /// </summary>
    public static class ToolUi
    {
        /// <summary>Width of the label column in <see cref="Field"/> rows.</summary>
        public const int LabelWidth = 110;

        /// <summary>Translates a locale key.</summary>
        public static string Tr(string key) => TranslationServer.Translate(key);

        /// <summary>Uppercase muted section caption (style guide <c>.ct-section-title</c>) with a little air above.</summary>
        public static Control Section(string titleKey)
        {
            var box = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            box.AddThemeConstantOverride("margin_top", UiTokens.Space2);
            box.AddChild(UiKit.SectionTitle(Tr(titleKey), UiTokens.FontSmall));
            return box;
        }

        /// <summary>
        /// Adds a "label | control" row to <paramref name="parent"/>: the label is
        /// muted, fixed width and carries the optional tooltip; the control fills
        /// the rest. Returns the control for chaining.
        /// </summary>
        public static T Field<T>(Container parent, string labelKey, T control, string tooltipKey = null) where T : Control
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", UiTokens.Space3);

            var label = new Label
            {
                Text = Tr(labelKey),
                CustomMinimumSize = new Vector2(LabelWidth, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ClipText = true,
                MouseFilter = Control.MouseFilterEnum.Pass,
                TooltipText = tooltipKey != null ? Tr(tooltipKey) : string.Empty
            };
            label.AddThemeColorOverride("font_color", UiTokens.Muted);
            label.AddThemeFontSizeOverride("font_size", UiTokens.FontBodySmall);
            row.AddChild(label);

            control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(control);
            parent.AddChild(row);
            return control;
        }

        /// <summary>
        /// Numeric field. Values are typed and shown culture-independently by
        /// <see cref="SpinBox"/>, which also clamps and steps them, so the editors
        /// no longer parse free text ("0,5" vs "0.5").
        /// </summary>
        public static SpinBox Number(double min, double max, double step, Action<double> changed)
        {
            var spin = new SpinBox
            {
                MinValue = min,
                MaxValue = max,
                Step = step,
                SelectAllOnFocus = true,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomArrowStep = step
            };
            spin.GetLineEdit().AddThemeFontOverride("font", Mono());
            if (changed != null)
                spin.ValueChanged += value => changed(value);
            return spin;
        }

        /// <summary>Single-line text field with a placeholder and an optional change handler.</summary>
        public static LineEdit Text(string placeholder = "", Action<string> changed = null)
        {
            var edit = new LineEdit { PlaceholderText = placeholder ?? string.Empty };
            if (changed != null)
                edit.TextChanged += text => changed(text);
            return edit;
        }

        /// <summary>Token button with a localized caption, optional tooltip and kind.</summary>
        public static Button Button(string textKey, Action pressed, ButtonKind kind = ButtonKind.Secondary, string tooltipKey = null)
        {
            var button = UiKit.Button(Tr(textKey), kind);
            if (tooltipKey != null)
                button.TooltipText = Tr(tooltipKey);
            if (pressed != null)
                button.Pressed += pressed;
            return button;
        }

        /// <summary>Thin vertical divider between toolbar groups.</summary>
        public static VSeparator Divider() => new() { CustomMinimumSize = new Vector2(UiTokens.Space3, 0) };

        /// <summary>Faint helper text that wraps inside narrow sidebars.</summary>
        public static Label Hint(string text)
        {
            var label = UiKit.Hint(text);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            label.CustomMinimumSize = new Vector2(80, 0);
            return label;
        }

        /// <summary>Monospace font for IDs, numbers and code (style guide <c>--ct-mono</c>).</summary>
        public static Font Mono() => ToolWindow.GetMonospaceFont();

        /// <summary>Code editor preset: monospace, line numbers, current-line highlight, no wrap.</summary>
        public static CodeEdit Code(bool editable = true)
        {
            var edit = new CodeEdit
            {
                Editable = editable,
                GuttersDrawLineNumbers = true,
                HighlightCurrentLine = true,
                WrapMode = TextEdit.LineWrappingMode.None,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                IndentUseSpaces = true,
                IndentSize = 2,
                AutoBraceCompletionEnabled = false
            };
            edit.AddThemeFontOverride("font", Mono());
            edit.AddThemeFontSizeOverride("font_size", UiTokens.FontBody);
            edit.AddThemeColorOverride("current_line_color", UiTokens.WhiteTrace);
            edit.AddThemeColorOverride("line_number_color", UiTokens.Faint);
            edit.AddThemeColorOverride("font_readonly_color", UiTokens.Body);
            return edit;
        }
    }
}
