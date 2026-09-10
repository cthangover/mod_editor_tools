namespace Cthangover.Core.UI.Tool.SceneBuilder
{
    /// <summary>
    /// Editing widget category for one inspector property. Drives which concrete
    /// field control <see cref="InspectorPanel"/> instantiates and which Variant
    /// shape is emitted back through <see cref="InspectorPanel.PropertyEdited"/>.
    /// </summary>
    public enum InspectorPropertyKind { Numeric, Vector2, Bool, String, Color, Enum }
}
