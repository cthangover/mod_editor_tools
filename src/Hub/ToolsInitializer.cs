namespace Cthangover.Tools.Hub
{
    using Cthangover.Core.Mods;
    using Cthangover.Core.Scenes;

    /// <summary>
    /// Lifecycle entry point of the tools mod. Publishes the logical scene
    /// <see cref="NovelEditorHub.SceneName"/>, the entry scene of the
    /// <c>novel_editor</c> collection: launching that collection from the game
    /// picker goes through the regular new-game flow and lands on the editor
    /// launcher instead of a story scene.
    /// </summary>
    public class ToolsInitializer : IModInitializer
    {
        private const string HubScenePath = "scenes/novel_editor_hub.tscn";

        public void OnModLoaded(string modId)
        {
        }

        public void OnModResourcesReady()
        {
            SceneRouteRegistry.Register(NovelEditorHub.SceneName, HubScenePath, fade: false);
        }
    }
}
