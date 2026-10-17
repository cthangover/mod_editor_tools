namespace Cthangover.Tools.Hub
{
    using Cthangover.Core.Scenes;
    using Cthangover.Core.UI.Theming;
    using Cthangover.Core.UI.Tool;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Interface.Theming;
    using Godot;

    /// <summary>
    /// Launcher screen of the <c>novel_editor</c> collection, entered as its
    /// logical entry scene. Lists every registered tool the same way the main
    /// menu does — each <see cref="IToolProvider"/> appends its own button via
    /// <see cref="IToolProvider.AddMenuButton"/> — so a new editor shows up here
    /// without touching this screen. Tool windows open on the tree root and
    /// survive scene switches (playing a scenario, returning to the menu).
    /// </summary>
    public partial class NovelEditorHub : Control
    {
        /// <summary>Logical scene name; the collection's <c>entry.scene</c>.</summary>
        public const string SceneName = "novel_editor";

        private const int PanelWidth = 420;

        public override void _Ready()
        {
            CthangoverTheme.Apply(this);

            AddChild(new ColorRect
            {
                Color = UiTokens.Page,
                MouseFilter = MouseFilterEnum.Ignore,
                AnchorRight = 1f,
                AnchorBottom = 1f,
            });

            var center = new CenterContainer { AnchorRight = 1f, AnchorBottom = 1f };
            AddChild(center);

            var panel = new PanelContainer { CustomMinimumSize = new Vector2(PanelWidth, 0) };
            var frame = UiKit.Frame(UiTokens.RadiusLg, UiTokens.Surface1, UiTokens.Line);
            frame.SetContentMarginAll(UiTokens.Space8);
            panel.AddThemeStyleboxOverride("panel", frame);
            center.AddChild(panel);

            var column = UiKit.Column(UiTokens.Space5);
            panel.AddChild(column);

            column.AddChild(UiKit.Label(Tr("tools/hub/title").ToUpperInvariant(), UiTokens.FontTitle, UiTokens.InkBright));
            var hint = UiKit.Hint(Tr("tools/hub/hint"));
            hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            column.AddChild(hint);

            column.AddChild(new HSeparator());
            column.AddChild(UiKit.SectionTitle(Tr("tools/hub/editors"), UiTokens.FontSmall));

            var tools = UiKit.Column(UiTokens.Space2);
            tools.MouseFilter = MouseFilterEnum.Pass;
            column.AddChild(tools);
            foreach (var tool in ToolFactory.Instance.GetAll())
                tool.AddMenuButton(tools);

            if (tools.GetChildCount() == 0)
                column.AddChild(UiKit.EmptyState(Tr("tools/hub/empty")));

            column.AddChild(new HSeparator());
            var menu = UiKit.Button(Tr("tools/hub/main_menu"));
            menu.Pressed += OnMainMenuPressed;
            column.AddChild(menu);

            GameLogger.Log("TOOLS", $"Novel editor hub: {tools.GetChildCount()} tool button(s)");
        }

        private void OnMainMenuPressed()
        {
            GetNodeOrNull<GodotSceneService>("/root/GodotSceneService")?.SwitchToMenu();
        }
    }
}
