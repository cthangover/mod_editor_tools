using System;
using Godot;

namespace Cthangover.Core.UI.Tool.SceneBuilder
{
    /// <summary>CheckBox editor for bool properties; commits the pressed state under the bound .tscn key.</summary>
    public partial class InspectorBoolField : CheckBox
    {
        public event Action<string, bool> Committed;

        private string _key;

        /// <summary>Binds the checkbox to the property key and seeds it with the current value.</summary>
        public void Setup(InspectableProperty property, bool value)
        {
            _key = property.Key;
            ButtonPressed = value;
            Toggled += OnToggled;
        }

        private void OnToggled(bool pressed)
        {
            Committed?.Invoke(_key, pressed);
        }
    }
}
