using System.Linq;
using Cthangover.Core.Mods;
using Cthangover.Core.Utils;
using Godot;

namespace Cthangover.Tools.Tests.Windows
{
    /// <summary>
    /// Entry point of the tool windows GUI test. Activated only by the exact
    /// user arg <c>--itest=tools_windows</c>; <c>--shots=&lt;dir&gt;</c> additionally
    /// saves a screenshot of every opened window (needs a real display, e.g.
    /// xvfb — the headless renderer produces no image). The driver is added to
    /// the tree deferred because resources-ready fires before the shell scene
    /// exists; the driver waits for the boot itself.
    /// </summary>
    public class ToolWindowsTestInitializer : IModInitializer
    {
        private static bool driverQueued;

        /// <summary>Nothing to do per mod: the driver is queued on resources-ready.</summary>
        public void OnModLoaded(string modId) { }

        /// <summary>Queues the driver once when the activation flag is present.</summary>
        public void OnModResourcesReady()
        {
            if (driverQueued)
                return;

            var args = OS.GetCmdlineUserArgs();
            if (!args.Any(a => a == "--itest=tools_windows"))
                return;

            var shots = args
                .Where(a => a.StartsWith("--shots=", System.StringComparison.Ordinal))
                .Select(a => a.Substring("--shots=".Length))
                .FirstOrDefault();

            driverQueued = true;
            GameLogger.Log("TEST", $"ToolWindowsTestInitializer: activating tool windows test, shots='{shots ?? ""}'");
            ModRoot.AddDeferred(new ToolWindowsTestDriverNode(shots));
        }
    }
}
