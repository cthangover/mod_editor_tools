using System;
using Cthangover.Core.UI.Theming;
using Godot;

namespace Cthangover.Tools.Common
{
    /// <summary>
    /// Round draggable point used by every canvas editor (polygon vertices,
    /// rect corners, markers, lights). It is a small Control placed over its
    /// canvas: the left button drags it and reports the new centre in global
    /// coordinates, the right button reports a delete request. Input arrives
    /// through the <c>GuiInput</c> event and drawing through the <c>draw</c>
    /// signal — the forms that dispatch reliably for mod assemblies.
    /// <para/>
    /// <see cref="Index"/> is free for the owner: a vertex index or a negative
    /// role constant (marker, label, rect corner...).
    /// </summary>
    public partial class DragHandle : Control
    {
        /// <summary>Owner-defined identity of the handle.</summary>
        public int Index;

        /// <summary>Called while dragging with the handle centre in global coordinates.</summary>
        public Action<Vector2> DragUpdate;

        /// <summary>Called on left press.</summary>
        public Action Clicked;

        /// <summary>Called on left release after a press.</summary>
        public Action DragEnd;

        /// <summary>Called on right press.</summary>
        public Action RightClicked;

        private readonly Color _fill;
        private bool _dragging;
        private bool _hovered;
        private Vector2 _dragOffset;

        /// <summary>Creates a handle of the given fill colour, role index and diameter.</summary>
        public DragHandle(Color fill, int index, float diameter = 14f)
        {
            _fill = fill;
            Index = index;
            Size = new Vector2(diameter, diameter);
            MouseFilter = MouseFilterEnum.Stop;
            MouseDefaultCursorShape = CursorShape.Drag;
            GuiInput += OnGuiInput;
            MouseEntered += () => { _hovered = true; QueueRedraw(); };
            MouseExited += () => { _hovered = false; QueueRedraw(); };
            Connect("draw", Callable.From((Action)Paint));
        }

        /// <summary>Places the handle so that its centre sits at <paramref name="globalCenter"/>; call after it is in the tree.</summary>
        public void CenterAt(Vector2 globalCenter)
        {
            GlobalPosition = globalCenter - Size / 2f;
        }

        private void Paint()
        {
            var center = Size / 2f;
            var radius = Size.X / 2f - 1f;
            DrawCircle(center, radius, UiTokens.Surface0);
            DrawCircle(center, radius - 2f, _fill);
            DrawArc(center, radius, 0f, Mathf.Tau, 24, _hovered || _dragging ? UiTokens.AccentHover : UiTokens.Ink, 1.5f, true);
        }

        private void OnGuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton mb)
            {
                if (mb.ButtonIndex == MouseButton.Left)
                {
                    if (mb.Pressed)
                    {
                        _dragging = true;
                        _dragOffset = GetGlobalMousePosition() - GlobalPosition;
                        Clicked?.Invoke();
                    }
                    else if (_dragging)
                    {
                        _dragging = false;
                        DragEnd?.Invoke();
                    }
                    AcceptEvent();
                }
                else if (mb.ButtonIndex == MouseButton.Right && mb.Pressed)
                {
                    RightClicked?.Invoke();
                    AcceptEvent();
                }
            }
            else if (@event is InputEventMouseMotion && _dragging)
            {
                GlobalPosition = GetGlobalMousePosition() - _dragOffset;
                DragUpdate?.Invoke(GlobalPosition + Size / 2f);
                AcceptEvent();
            }
        }
    }
}
