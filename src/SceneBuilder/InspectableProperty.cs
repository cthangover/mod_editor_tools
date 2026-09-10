namespace Cthangover.Core.UI.Tool.SceneBuilder
{
    /// <summary>
    /// Declarative description of a single editable node property in the scene
    /// builder inspector. Instances come from <see cref="InspectorPropertyCatalog"/>
    /// and are consumed by <see cref="InspectorPanel"/> to build row editors.
    /// <see cref="Key"/> doubles as the .tscn property name written back by
    /// <see cref="TscnTextSurgery"/>, so only keys Godot's <c>Set()</c> understands
    /// may appear here — the runtime builder applies unknown keys the same way,
    /// which keeps live preview and saved file consistent.
    /// </summary>
    public class InspectableProperty
    {
        public string Key { get; set; }
        public InspectorPropertyKind Kind { get; set; }
        /// <summary>Whole-number mode: committed values are rounded and serialized as .tscn ints.</summary>
        public bool IsInteger { get; set; }
        public double Min { get; set; } = -100000d;
        public double Max { get; set; } = 100000d;
        public double Step { get; set; } = 1d;
        public long[] EnumValues { get; set; }
        public string[] EnumLabels { get; set; }
    }
}
