namespace Cthangover.Tools.Tests
{
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Core.UI.Theming;
    using Godot;

    /// <summary>A checked box or toggle under the mouse keeps the margins of its other states, so its label never jumps.</summary>
    public class ThemeToggleStatesTests
    {
        private static readonly string[] States = { "normal", "hover", "pressed", "hover_pressed", "disabled" };

        [ModTest("theme/toggle_states_keep_their_margins")]
        static void ToggleStatesKeepTheirMargins(ModTestContext ctx)
        {
            // In the tree: styles are looked up through the theme owner only there.
            var theme = CthangoverTheme.Build();
            var root = ((SceneTree)Engine.GetMainLoop()).Root;
            var controls = new (string What, Button Control)[]
            {
                ("CheckBox", new CheckBox()),
                ("toggle Button", new Button { ToggleMode = true }),
                ("tab", new Button { ToggleMode = true, ThemeTypeVariation = CthangoverTheme.TabVariation }),
                ("primary", new Button { ToggleMode = true, ThemeTypeVariation = CthangoverTheme.PrimaryVariation }),
                ("danger", new Button { ToggleMode = true, ThemeTypeVariation = CthangoverTheme.DangerVariation }),
                ("icon", new Button { ToggleMode = true, ThemeTypeVariation = CthangoverTheme.IconVariation }),
                ("ribbon", new Button { ToggleMode = true, ThemeTypeVariation = CthangoverTheme.RibbonVariation }),
                ("OptionButton", new OptionButton()),
            };
            foreach (var (what, control) in controls)
            {
                control.Theme = theme;
                root.AddChild(control);
                var normal = control.GetThemeStylebox("normal");
                foreach (var state in States)
                {
                    var box = control.GetThemeStylebox(state);
                    ModAssert.CloseTo(normal.ContentMarginLeft, box.ContentMarginLeft, what: $"{what} {state}: left margin");
                    ModAssert.CloseTo(normal.ContentMarginRight, box.ContentMarginRight, what: $"{what} {state}: right margin");
                }
                control.Free();
            }
        }
    }
}
