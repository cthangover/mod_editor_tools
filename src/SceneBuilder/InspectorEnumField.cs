using System;
using Godot;

namespace Cthangover.Core.UI.Tool.SceneBuilder
{
    /// <summary>
    /// OptionButton-based editor for enum-valued properties (grow direction,
    /// mouse filter, ...). Maps the selected list index back to the stored
    /// numeric value from <see cref="InspectableProperty.EnumValues"/>; unknown
    /// current values leave the dropdown unselected instead of lying about state.
    /// </summary>
    public partial class InspectorEnumField : OptionButton
    {
        public event Action<string, long> Committed;

        private InspectableProperty _property;

        /// <summary>Populates the dropdown from the property's enum labels and selects the current value, if known.</summary>
        public void Setup(InspectableProperty property, long currentValue)
        {
            _property = property;
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

            for (var i = 0; i < property.EnumLabels.Length; i++)
                AddItem(property.EnumLabels[i]);

            Selected = IndexOfValue(currentValue);
            ItemSelected += OnItemSelected;
        }

        private int IndexOfValue(long value)
        {
            if (_property.EnumValues == null)
                return -1;
            for (var i = 0; i < _property.EnumValues.Length; i++)
            {
                if (_property.EnumValues[i] == value)
                    return i;
            }
            return -1;
        }

        private void OnItemSelected(long index)
        {
            if (_property.EnumValues != null && index >= 0 && index < _property.EnumValues.Length)
                Committed?.Invoke(_property.Key, _property.EnumValues[index]);
        }
    }
}
