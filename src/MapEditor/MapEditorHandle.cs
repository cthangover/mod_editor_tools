using Cthangover.Core.Utils;
using Godot;

namespace Cthangover.Tools.MapEditor
{
    /// <summary>
    /// Draggable 16x16 handle on the map editor canvas. One handle represents
    /// either a polygon vertex, the marker position or the label position,
    /// distinguished by <see cref="Index"/> with negative values reserved for
    /// the position handles. Subscribes to the GuiInput C# event rather than
    /// overriding <c>_GuiInput</c> for reliable event delivery in mod
    /// assemblies, following <c>InteractiveEditorHandle</c>.
    /// </summary>
    public partial class MapEditorHandle : ColorRect
    {
        /// <summary>Handle role: 0..n — vertex index, <see cref="MarkerHandle"/> or <see cref="LabelHandle"/>.</summary>
        public const int MarkerHandle = -2;

        /// <summary>Handle role used for the label position handle.</summary>
        public const int LabelHandle = -3;

        /// <summary>Vertex index or one of the negative role constants.</summary>
        public int Index;

        /// <summary>Invoked during drag with the handle centre in global coordinates.</summary>
        public System.Action<Vector2> DragUpdate;

        /// <summary>Invoked when the handle is clicked (mouse down).</summary>
        public System.Action Clicked;

        /// <summary>Invoked when the drag ends (mouse up).</summary>
        public System.Action DragEnd;

        /// <summary>
        /// Invoked on a right-click over the handle. Handles stop mouse input,
        /// so without this hook a right-click aimed exactly at a vertex would
        /// never reach the canvas and the vertex could not be deleted there.
        /// </summary>
        public System.Action RightClicked;

        private bool _dragging;
        private Vector2 _dragOffset;

        /// <summary>Creates a handle with the given colour and role index.</summary>
        public MapEditorHandle(Color color, int index)
        {
            Index = index;
            Size = new Vector2(16, 16);
            Color = color;
            MouseFilter = MouseFilterEnum.Stop;
            MouseDefaultCursorShape = CursorShape.PointingHand;
            GuiInput += OnGuiInput;
        }

        private void OnGuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed)
                {
                    _dragging = true;
                    _dragOffset = GetGlobalMousePosition() - GlobalPosition;
                    Clicked?.Invoke();
                }
                else
                {
                    _dragging = false;
                    DragEnd?.Invoke();
                }
                AcceptEvent();
            }
            else if (@event is InputEventMouseButton right && right.ButtonIndex == MouseButton.Right && right.Pressed)
            {
                RightClicked?.Invoke();
                AcceptEvent();
            }
            else if (@event is InputEventMouseMotion && _dragging)
            {
                var center = GlobalPosition + Size / 2;
                GlobalPosition = GetGlobalMousePosition() - _dragOffset;
                DragUpdate?.Invoke(center);
            }
        }
    }
}
