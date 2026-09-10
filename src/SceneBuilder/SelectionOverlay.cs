using System;
using System.Collections.Generic;
using Cthangover.Core.UI;
using Godot;

namespace Cthangover.Core.UI.Tool.SceneBuilder
{
    /// <summary>
    /// Godot-editor-style manipulation overlay drawn over the scene builder
    /// preview: 8 resize markers (corners + edges, editing offset_* respecting
    /// anchors) and 4 draggable anchor points with crosshair lines (editing
    /// anchor_* while preserving the visual rectangle). One input surface owns
    /// the whole interaction — markers are purely visual (input-ignore) and the
    /// overlay itself hit-tests them in its <c>GuiInput</c> handler — because
    /// per-marker Controls cannot capture motion outside their small rects and
    /// virtual overrides (_Process/_GuiInput) do not dispatch in mod assemblies.
    /// Live edits go straight onto the node during the drag; the accumulated
    /// key/value batch is emitted once on mouse release via
    /// <see cref="BatchCommitted"/> so text write-back happens in one pass.
    /// Coordinates map between the fixed-size SubViewport canvas and this
    /// overlay through the stretch scale of the hosting SubViewportContainer.
    /// </summary>
    public partial class SelectionOverlay : ModWidget
    {
        /// <summary>Left press that did not grab any marker: (position in viewport canvas coordinates).</summary>
        public event Action<Vector2> PickRequested;

        /// <summary>Raised once per finished drag: (node, ordered key/value batch to persist).</summary>
        public event Action<Node, List<KeyValuePair<string, Variant>>> BatchCommitted;

        private const float HandleHalf = 5f;
        private const float HandleInset = 8f;
        private const float AnchorDotHalf = 3.5f;
        private const float AnchorLineLength = 44f;
        private const float AnchorHitRadius = 10f;
        private const float HandleHitPadding = 5f;
        private const float BorderThickness = 1.5f;

        // Resize marker indices: 0=TL corner, 1=top edge, 2=TR, 3=left, 4=right, 5=BL, 6=bottom, 7=BR.
        // Anchor dot indices: 0=TL, 1=TR, 2=BL, 3=BR of the anchor quad.
        private const int ResizeMarkerCount = 8;
        private const int AnchorDotCount = 4;

        private enum DragKind { None, Resize, Anchor }

        private struct Marker
        {
            public Rect2 Rect;
            public bool IsAnchor;
            public int Index;
        }

        private SubViewport _viewport;
        private Control _containerArea;
        private Control _selected;
        private readonly List<Marker> _markers = new();
        private readonly List<ColorRect> _borderRects = new();
        private readonly List<ColorRect> _handleRects = new();
        private readonly List<ColorRect> _anchorLines = new();
        private readonly List<ColorRect> _anchorDots = new();

        private DragKind _drag = DragKind.None;
        private int _dragIndex;
        private readonly Dictionary<string, double> _pendingValues = new();

        /// <summary>Binds the overlay to the preview pipeline; call once before use.</summary>
        public void Setup(SubViewport viewport, Control containerArea)
        {
            _viewport = viewport;
            _containerArea = containerArea;
            _containerArea.Resized += Refresh;
            Refresh();
        }

        /// <summary>Selects the node to manipulate; hides markers for null/non-manipulable nodes.</summary>
        public void Track(Control selected)
        {
            _selected = selected;
            _drag = DragKind.None;
            _pendingValues.Clear();
            Refresh();
        }

        protected override void Construct()
        {
            MouseFilter = Control.MouseFilterEnum.Stop;
            Resized += Refresh;

            for (var i = 0; i < 4; i++)
                _borderRects.Add(CreateMarker(new Color(1f, 0.85f, 0.35f, 0.95f)));
            for (var i = 0; i < ResizeMarkerCount; i++)
                _handleRects.Add(CreateMarker(new Color(1f, 0.62f, 0.1f, 1f)));
            for (var i = 0; i < AnchorDotCount * 2; i++)
                _anchorLines.Add(CreateMarker(new Color(0.35f, 0.75f, 1f, 0.55f)));
            for (var i = 0; i < AnchorDotCount; i++)
                _anchorDots.Add(CreateMarker(new Color(0.45f, 0.85f, 1f, 1f)));

            GuiInput += OnOverlayGui;
        }

        private ColorRect CreateMarker(Color color)
        {
            var rect = new ColorRect { Color = color, MouseFilter = Control.MouseFilterEnum.Ignore };
            AddChild(rect);
            return rect;
        }

        /// <summary>Recomputes all marker geometry from the tracked node's current state.</summary>
        public void Refresh()
        {
            var control = _selected as Control;
            var parent = control?.GetParent() as Control;
            var manipulable = control != null
                && GodotObject.IsInstanceValid(control)
                && parent != null
                && !(parent is Container);

            if (!manipulable || _viewport == null || _containerArea == null)
            {
                SetVisibleAll(false);
                _markers.Clear();
                return;
            }

            UpdateMarkers(control);
            UpdateMarkerControls();
        }

        private void UpdateMarkers(Control control)
        {
            _markers.Clear();
            var rect = CanvasRectToOverlay(control.GetGlobalRect());

            for (var i = 0; i < ResizeMarkerCount; i++)
            {
                var center = GetHandleCenter(rect, i);
                _markers.Add(new Marker
                {
                    Rect = new Rect2(center - new Vector2(HandleHalf + HandleHitPadding, HandleHalf + HandleHitPadding),
                        new Vector2(HandleHalf + HandleHitPadding, HandleHalf + HandleHitPadding) * 2f),
                    IsAnchor = false,
                    Index = i
                });
            }

            var parent = (Control)control.GetParent();
            var parentOrigin = parent.GetGlobalRect().Position;
            for (var i = 0; i < AnchorDotCount; i++)
            {
                var anchorPoint = GetAnchorCorner(parentOrigin, parent.Size,
                    control.AnchorLeft, control.AnchorTop, control.AnchorRight, control.AnchorBottom, i);
                var center = CanvasToOverlay(anchorPoint);
                _markers.Add(new Marker
                {
                    Rect = new Rect2(center - new Vector2(AnchorHitRadius, AnchorHitRadius),
                        new Vector2(AnchorHitRadius, AnchorHitRadius) * 2f),
                    IsAnchor = true,
                    Index = i
                });
            }
        }

        private void UpdateMarkerControls()
        {
            var anyVisible = _markers.Count > 0;
            var rect = anyVisible ? CanvasRectToOverlay(((Control)_selected).GetGlobalRect()) : new Rect2();

            for (var i = 0; i < _borderRects.Count; i++)
            {
                var bar = _borderRects[i];
                bar.Visible = anyVisible;
                switch (i)
                {
                    case 0: bar.Position = rect.Position - new Vector2(BorderThickness, BorderThickness); bar.Size = new Vector2(rect.Size.X + BorderThickness * 2f, BorderThickness); break;
                    case 1: bar.Position = new Vector2(rect.Position.X - BorderThickness, rect.End.Y); bar.Size = new Vector2(rect.Size.X + BorderThickness * 2f, BorderThickness); break;
                    case 2: bar.Position = new Vector2(rect.Position.X - BorderThickness, rect.Position.Y); bar.Size = new Vector2(BorderThickness, rect.Size.Y); break;
                    case 3: bar.Position = new Vector2(rect.End.X, rect.Position.Y); bar.Size = new Vector2(BorderThickness, rect.Size.Y); break;
                }
            }

            for (var i = 0; i < _handleRects.Count; i++)
            {
                var handle = _handleRects[i];
                handle.Visible = anyVisible;
                if (anyVisible)
                    handle.Position = GetHandleCenter(rect, i) - new Vector2(HandleHalf, HandleHalf);
                handle.Size = new Vector2(HandleHalf * 2f, HandleHalf * 2f);
            }

            for (var i = 0; i < AnchorDotCount; i++)
            {
                var markerIdx = ResizeMarkerCount + i;
                var has = anyVisible && markerIdx < _markers.Count;
                var dot = _anchorDots[i];
                var hLine = _anchorLines[i * 2];
                var vLine = _anchorLines[i * 2 + 1];
                dot.Visible = has;
                hLine.Visible = has;
                vLine.Visible = has;
                if (!has)
                    continue;

                var center = _markers[markerIdx].Rect.GetCenter();
                dot.Position = center - new Vector2(AnchorDotHalf, AnchorDotHalf);
                dot.Size = new Vector2(AnchorDotHalf * 2f, AnchorDotHalf * 2f);
                hLine.Position = new Vector2(center.X - AnchorLineLength / 2f, center.Y - 0.5f);
                hLine.Size = new Vector2(AnchorLineLength, 1f);
                vLine.Position = new Vector2(center.X - 0.5f, center.Y - AnchorLineLength / 2f);
                vLine.Size = new Vector2(1f, AnchorLineLength);
            }
        }

        private void SetVisibleAll(bool visible)
        {
            foreach (var rect in _borderRects) rect.Visible = visible;
            foreach (var rect in _handleRects) rect.Visible = visible;
            foreach (var rect in _anchorLines) rect.Visible = visible;
            foreach (var rect in _anchorDots) rect.Visible = visible;
        }

        private static Vector2 GetHandleCenter(Rect2 rect, int index)
        {
            var inset = HandleHalf + 3f;
            switch (index)
            {
                case 0: return rect.Position + new Vector2(inset, inset);
                case 1: return new Vector2(rect.GetCenter().X, rect.Position.Y);
                case 2: return new Vector2(rect.End.X - inset, rect.Position.Y + inset);
                case 3: return new Vector2(rect.Position.X, rect.GetCenter().Y);
                case 4: return new Vector2(rect.End.X, rect.GetCenter().Y);
                case 5: return new Vector2(rect.Position.X + inset, rect.End.Y - inset);
                case 6: return new Vector2(rect.GetCenter().X, rect.End.Y);
                default: return rect.End - new Vector2(inset, inset);
            }
        }

        private static Vector2 GetAnchorCorner(Vector2 parentOrigin, Vector2 parentSize,
            float aLeft, float aTop, float aRight, float aBottom, int index)
        {
            var x = index % 2 == 0 ? aLeft : aRight;
            var y = index < 2 ? aTop : aBottom;
            return parentOrigin + new Vector2(x * parentSize.X, y * parentSize.Y);
        }

        private void OnOverlayGui(InputEvent evt)
        {
            if (evt is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed)
                    OnPress(mb.Position);
                else if (_drag != DragKind.None)
                    FinishDrag();
                return;
            }

            if (evt is InputEventMouseMotion motion)
            {
                if (_drag != DragKind.None)
                {
                    ApplyDrag(motion.Position);
                    Refresh();
                }
                else
                {
                    UpdateHoverCursor(motion.Position);
                }
            }
        }

        private void OnPress(Vector2 localPoint)
        {
            // Anchor dots win over coinciding resize markers (full-rect nodes keep
            // their corners resizable because resize centers sit inset).
            for (var i = 0; i < _markers.Count; i++)
            {
                if (!_markers[i].IsAnchor || !_markers[i].Rect.HasPoint(localPoint))
                    continue;
                BeginDrag(DragKind.Anchor, _markers[i].Index);
                AcceptEvent();
                return;
            }

            for (var i = 0; i < _markers.Count; i++)
            {
                if (_markers[i].IsAnchor || !_markers[i].Rect.HasPoint(localPoint))
                    continue;
                BeginDrag(DragKind.Resize, _markers[i].Index);
                AcceptEvent();
                return;
            }

            PickRequested?.Invoke(LocalToCanvas(localPoint));
        }

        private void BeginDrag(DragKind kind, int index)
        {
            _drag = kind;
            _dragIndex = index;
            _pendingValues.Clear();
        }

        private void ApplyDrag(Vector2 localPoint)
        {
            if (!(_selected is Control control) || !(control.GetParent() is Control parent))
                return;

            var mouseInParent = LocalToCanvas(localPoint) - parent.GetGlobalRect().Position;
            var parentSize = parent.Size;

            if (_drag == DragKind.Resize)
            {
                ApplyResizeEdge(control, parentSize, mouseInParent, _dragIndex);
                return;
            }

            ApplyAnchorDrag(control, parentSize, mouseInParent, _dragIndex);
        }

        private void ApplyResizeEdge(Control control, Vector2 parentSize, Vector2 mouseInParent, int index)
        {
            // Bit layout of the index: 0=TL,1=T,2=TR,3=L,4=R,5=BL,6=B,7=BR.
            var topSide = index == 0 || index == 1 || index == 2;
            var bottomSide = index == 5 || index == 6 || index == 7;
            var leftSide = index == 0 || index == 3 || index == 5;
            var rightSide = index == 2 || index == 4 || index == 7;

            if (topSide)
                StorePair(control, "offset_top", mouseInParent.Y - control.AnchorTop * parentSize.Y);
            if (bottomSide)
                StorePair(control, "offset_bottom", mouseInParent.Y - control.AnchorBottom * parentSize.Y);
            if (leftSide)
                StorePair(control, "offset_left", mouseInParent.X - control.AnchorLeft * parentSize.X);
            if (rightSide)
                StorePair(control, "offset_right", mouseInParent.X - control.AnchorRight * parentSize.X);
        }

        private void ApplyAnchorDrag(Control control, Vector2 parentSize, Vector2 mouseInParent, int index)
        {
            var moveRight = index % 2 == 1;
            var moveBottom = index >= 2;

            var newAnchorX = Mathf.Clamp(mouseInParent.X / Mathf.Max(parentSize.X, 1f), 0f, 1f);
            var newAnchorY = Mathf.Clamp(mouseInParent.Y / Mathf.Max(parentSize.Y, 1f), 0f, 1f);

            // Preserve the visual rectangle: shift the matching offset by the
            // anchor delta so only the anchor moves, exactly like the Godot editor.
            var oldAnchorX = moveRight ? control.AnchorRight : control.AnchorLeft;
            var oldOffsetX = moveRight ? control.OffsetRight : control.OffsetLeft;
            var newOffsetX = oldOffsetX + (oldAnchorX - newAnchorX) * parentSize.X;

            var oldAnchorY = moveBottom ? control.AnchorBottom : control.AnchorTop;
            var oldOffsetY = moveBottom ? control.OffsetBottom : control.OffsetTop;
            var newOffsetY = oldOffsetY + (oldAnchorY - newAnchorY) * parentSize.Y;

            if (moveRight)
            {
                StorePair(control, "anchor_right", newAnchorX);
                StorePair(control, "offset_right", newOffsetX);
            }
            else
            {
                StorePair(control, "anchor_left", newAnchorX);
                StorePair(control, "offset_left", newOffsetX);
            }

            if (moveBottom)
            {
                StorePair(control, "anchor_bottom", newAnchorY);
                StorePair(control, "offset_bottom", newOffsetY);
            }
            else
            {
                StorePair(control, "anchor_top", newAnchorY);
                StorePair(control, "offset_top", newOffsetY);
            }
        }

        private void StorePair(Control control, string key, double value)
        {
            _pendingValues[key] = value;
            control.Set(key, value);
        }

        private void FinishDrag()
        {
            var node = _selected;
            _drag = DragKind.None;

            if (node == null || _pendingValues.Count == 0)
            {
                _pendingValues.Clear();
                return;
            }

            // Canonical tscn ordering: layout gate, anchors, offsets.
            var batch = new List<KeyValuePair<string, Variant>>();
            if (_pendingValues.ContainsKey("anchor_left") && ReadLayoutMode(node) != 1)
                batch.Add(new KeyValuePair<string, Variant>("layout_mode", Variant.From(1L)));

            foreach (var key in new[] { "anchor_left", "anchor_top", "anchor_right", "anchor_bottom" })
                if (_pendingValues.TryGetValue(key, out var v))
                    batch.Add(new KeyValuePair<string, Variant>(key, Variant.From(v)));
            foreach (var key in new[] { "offset_left", "offset_top", "offset_right", "offset_bottom" })
                if (_pendingValues.TryGetValue(key, out var v))
                    batch.Add(new KeyValuePair<string, Variant>(key, Variant.From(v)));

            _pendingValues.Clear();
            BatchCommitted?.Invoke(node, batch);
        }

        private static long ReadLayoutMode(Node node)
        {
            var v = node.Get("layout_mode");
            if (v.VariantType == Variant.Type.Int)
                return v.AsInt64();
            if (v.VariantType == Variant.Type.Float)
                return (long)v.AsDouble();
            return 0;
        }

        private void UpdateHoverCursor(Vector2 localPoint)
        {
            for (var i = 0; i < _markers.Count; i++)
            {
                if (!_markers[i].Rect.HasPoint(localPoint))
                    continue;

                if (_markers[i].IsAnchor)
                {
                    MouseDefaultCursorShape = Control.CursorShape.Move;
                    return;
                }

                switch (_markers[i].Index)
                {
                    case 0:
                    case 7:
                        MouseDefaultCursorShape = Control.CursorShape.Fdiagsize;
                        return;
                    case 2:
                    case 5:
                        MouseDefaultCursorShape = Control.CursorShape.Bdiagsize;
                        return;
                    case 1:
                    case 6:
                        MouseDefaultCursorShape = Control.CursorShape.Vsize;
                        return;
                    default:
                        MouseDefaultCursorShape = Control.CursorShape.Hsize;
                        return;
                }
            }

            MouseDefaultCursorShape = Control.CursorShape.Arrow;
        }

        // ── coordinate mapping ──────────────────────────────────────────

        private Vector2 ComputeStretchScale()
        {
            var viewSize = _viewport.Size;
            return new Vector2(
                viewSize.X > 0 ? _containerArea.Size.X / viewSize.X : 1f,
                viewSize.Y > 0 ? _containerArea.Size.Y / viewSize.Y : 1f);
        }

        private Vector2 CanvasToOverlay(Vector2 canvasPoint)
        {
            return canvasPoint * ComputeStretchScale();
        }

        private Rect2 CanvasRectToOverlay(Rect2 canvasRect)
        {
            var scale = ComputeStretchScale();
            return new Rect2(canvasRect.Position * scale, canvasRect.Size * scale);
        }

        private Vector2 LocalToCanvas(Vector2 localPoint)
        {
            var scale = ComputeStretchScale();
            return new Vector2(
                scale.X > 0.0001f ? localPoint.X / scale.X : localPoint.X,
                scale.Y > 0.0001f ? localPoint.Y / scale.Y : localPoint.Y);
        }
    }
}
