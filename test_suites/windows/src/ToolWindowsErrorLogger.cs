using System.Collections.Generic;
using Godot;

namespace Cthangover.Tools.Tests.Windows
{
    /// <summary>
    /// Engine logger that records every error Godot reports while the test
    /// runs. An exception thrown inside a signal handler (a button toggled
    /// during construction, a draw callback) never reaches the code that opened
    /// the window — Godot only prints it — so without this the test would pass
    /// windows that are visibly broken. Errors may arrive from any thread.
    /// </summary>
    public partial class ToolWindowsErrorLogger : Logger
    {
        private readonly object _lock = new();
        private readonly List<string> _errors = new();

        /// <summary>Records the error text (rationale or code, whichever is set).</summary>
        public override void _LogError(string function, string file, int line, string code, string rationale,
            bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
        {
            if (errorType == (int)ErrorType.Warning)
                return;

            lock (_lock)
                _errors.Add($"{(string.IsNullOrEmpty(rationale) ? code : rationale)} ({file}:{line})");
        }

        /// <summary>Returns and clears the errors collected so far.</summary>
        public List<string> Drain()
        {
            lock (_lock)
            {
                var copy = new List<string>(_errors);
                _errors.Clear();
                return copy;
            }
        }
    }
}
