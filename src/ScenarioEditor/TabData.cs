namespace Cthangover.Core.UI.Tool.ScenarioEditor
{
    /// <summary>
    /// State of one open scenario file tab: identity, the current text buffer
    /// (including unsaved edits while the tab is in the background) and the
    /// dirty flag.
    /// </summary>
    public sealed class TabData
    {
        public string Key;
        public string ModId;
        public string RelativePath;
        public string FileName;
        public string Buffer;
        public bool IsDirty;
    }
}
