using System;
using Godot;

namespace Cthangover.Core.UI.Tool.SceneBuilder
{
    /// <summary>
    /// Paired x/y SpinBox row for Vector2 properties. Both axes share one commit
    /// channel: any axis change emits the full current vector, which keeps the
    /// live node, the preview and the .tscn text in sync on every keystroke.
    /// </summary>
    public partial class InspectorVector2Field : HBoxContainer
    {
        public event Action<string, Vector2> Committed;

        private string _key;
        private SpinBox _x;
        private SpinBox _y;

        public void Setup(InspectableProperty property, Vector2 initialValue)
        {
            _key = property.Key;
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

            AddChild(new Label { Text = "x", CustomMinimumSize = new Vector2(14f, 0f) });
            _x = CreateAxis(property, initialValue.X);
            AddChild(new Label { Text = "y", CustomMinimumSize = new Vector2(14f, 0f) });
            _y = CreateAxis(property, initialValue.Y);
        }

        private SpinBox CreateAxis(InspectableProperty property, double initial)
        {
            var box = new InspectorNumericField();
            box.Setup(property, initial);
            box.CustomMinimumSize = new Vector2(60f, 0f);
            box.Committed += OnAxisChanged;
            AddChild(box);
            return box;
        }

        private void OnAxisChanged(string key, double value)
        {
            Committed?.Invoke(_key, new Vector2((float)_x.Value, (float)_y.Value));
        }
    }
}
