using System;
using Godot;

namespace Cthangover.Core.UI.Tool.SceneBuilder
{
    /// <summary>LineEdit editor for string properties (tooltip_text); every keystroke commits, matching the live-preview editing model.</summary>
    public partial class InspectorTextField : LineEdit
    {
        public event Action<string, string> Committed;

        private string _key;

        /// <summary>Binds the line edit to the property key and seeds it with the current text.</summary>
        public void Setup(InspectableProperty property, string value)
        {
            _key = property.Key;
            Text = value ?? "";
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            TextChanged += OnTextChanged;
        }

        private void OnTextChanged(string text)
        {
            Committed?.Invoke(_key, text);
        }
    }
}
