using System.Linq;
using Cthangover.Core.Mods;
using Cthangover.Core.Utils;
using Godot;

namespace Cthangover.Tools.Tests.NovelFlow
{
    /// <summary>
    /// Entry point of the novel_flow test: adds the driver to the tree root when the exact
    /// flag <c>--itest=novel_flow</c> is given (the driver quits the process). Optional
    /// <c>--shots=&lt;dir&gt;</c> makes the driver save a screenshot per step.
    /// </summary>
    public class NovelFlowTestInitializer : IModInitializer
    {
        private static bool driverQueued;

        /// <summary>Nothing per mod.</summary>
        public void OnModLoaded(string modId)
        {
        }

        /// <summary>Queues the driver once.</summary>
        public void OnModResourcesReady()
        {
            var args = OS.GetCmdlineUserArgs();
            if (driverQueued || !args.Any(a => a == "--itest=novel_flow"))
                return;

            driverQueued = true;
            var shots = args.Where(a => a.StartsWith("--shots=", System.StringComparison.Ordinal))
                .Select(a => a.Substring("--shots=".Length))
                .FirstOrDefault();
            GameLogger.Log("TEST", $"NovelFlowTestInitializer: activating novel_flow, shots={shots ?? "off"}");
            (Godot.Engine.GetMainLoop() as SceneTree)?.Root?.CallDeferred("add_child", new NovelFlowTestDriver(shots));
        }
    }
}
