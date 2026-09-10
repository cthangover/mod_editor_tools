using System;
using Godot;

namespace Cthangover.Core.UI.Tool.SceneBuilder
{
    /// <summary>
    /// SpinBox-based inspector editor for scalar properties. Carries the .tscn
    /// property key and re-emits committed values through <see cref="Committed"/>,
    /// so the panel needs no per-widget closures (mod assemblies cannot rely on
    /// Godot's source-generated signal wiring). Integer mode rounds away the
    /// fractional part so enum-ish ints survive serialization.
    /// </summary>
    public partial class InspectorNumericField : SpinBox
    {
        public event Action<string, double> Committed;

        private string _key;
        private bool _integer;

        public void Setup(InspectableProperty property, double initialValue)
        {
            _key = property.Key;
            _integer = property.IsInteger;
            MinValue = property.Min;
            MaxValue = property.Max;
            Step = property.Step;
            Value = System.Math.Clamp(initialValue, property.Min, property.Max);
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            ValueChanged += OnValueChanged;
        }

        private void OnValueChanged(double value)
        {
            Committed?.Invoke(_key, _integer ? System.Math.Round(value) : value);
        }
    }
}
