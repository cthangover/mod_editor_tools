using Cthangover.Core.UI.Theming;
using Godot;

namespace Cthangover.Tools.Common
{
    /// <summary>
    /// Sidebar section with a clickable uppercase caption (style guide
    /// <c>.ct-section-title</c> plus a ▸/▾ marker) that shows or hides its
    /// <see cref="Body"/>. Editors expand the sections relevant to the selected
    /// object and collapse the rest, so a long property panel stays readable
    /// without losing access to rarely used fields.
    /// </summary>
    public partial class CollapsibleSection : VBoxContainer
    {
        private readonly Button _header;
        private readonly string _title;

        /// <summary>Container for the section's controls.</summary>
        public VBoxContainer Body { get; }

        /// <summary>Whether the body is shown.</summary>
        public bool Expanded
        {
            get => Body.Visible;
            set
            {
                Body.Visible = value;
                _header.Text = (value ? "▾  " : "▸  ") + _title;
            }
        }

        /// <summary>Creates a section titled by <paramref name="titleKey"/>.</summary>
        public CollapsibleSection(string titleKey, bool expanded = true)
        {
            _title = ToolUi.Tr(titleKey).ToUpperInvariant();
            AddThemeConstantOverride("separation", UiTokens.Space2);

            _header = new Button
            {
                Flat = true,
                Alignment = HorizontalAlignment.Left,
                FocusMode = FocusModeEnum.None,
                MouseDefaultCursorShape = CursorShape.PointingHand
            };
            _header.AddThemeFontSizeOverride("font_size", UiTokens.FontSmall);
            _header.AddThemeColorOverride("font_color", UiTokens.Muted);
            _header.AddThemeColorOverride("font_hover_color", UiTokens.AccentBright);
            _header.AddThemeColorOverride("font_pressed_color", UiTokens.Muted);
            _header.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
            _header.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
            _header.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
            _header.Pressed += () => Expanded = !Expanded;
            AddChild(_header);

            Body = new VBoxContainer();
            Body.AddThemeConstantOverride("separation", UiTokens.Space2);
            AddChild(Body);

            Expanded = expanded;
        }
    }
}
