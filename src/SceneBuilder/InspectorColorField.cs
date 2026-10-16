using System;
using Godot;

namespace Cthangover.Core.UI.Tool.SceneBuilder
{
    /// <summary>ColorPickerButton editor for color properties (modulate, self_modulate); commits RGBA including alpha edits.</summary>
    public partial class InspectorColorField : ColorPickerButton
    {
        public event Action<string, Color> Committed;

        private string _key;

        /// <summary>Binds the picker to the property key and seeds it with the current color.</summary>
        public void Setup(InspectableProperty property, Color value)
        {
            _key = property.Key;
            Color = value;
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            CustomMinimumSize = new Vector2(60f, 24f);
            ColorChanged += OnColorChanged;
        }

        private void OnColorChanged(Color color)
        {
            Committed?.Invoke(_key, color);
        }
    }
}
