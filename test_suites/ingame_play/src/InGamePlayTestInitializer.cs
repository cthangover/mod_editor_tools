using System.Linq;
using Cthangover.Core.Mods;
using Cthangover.Core.Utils;
using Godot;

namespace Cthangover.Tools.Tests.InGamePlay
{
    /// <summary>
    /// Entry point of the in-game play-test GUI test, activated only by the
    /// exact user arg <c>--itest=ingame_play</c>. The driver is added deferred:
    /// resources-ready fires before the game's shell exists.
    /// </summary>
    public class InGamePlayTestInitializer : IModInitializer
    {
        private static bool driverQueued;

        /// <summary>Nothing to do per mod: the driver is queued on resources-ready.</summary>
        public void OnModLoaded(string modId) { }

        /// <summary>Queues the driver once when the activation flag is present.</summary>
        public void OnModResourcesReady()
        {
            if (driverQueued || !OS.GetCmdlineUserArgs().Any(a => a == "--itest=ingame_play"))
                return;

            driverQueued = true;
            GameLogger.Log("TEST", "InGamePlayTestInitializer: activating in-game play-test test");
            (Godot.Engine.GetMainLoop() as SceneTree)?.Root?.CallDeferred("add_child", new InGamePlayTestDriverNode());
        }
    }
}
