using System;
using System.Linq;
using Cthangover.Core.Interactive;
using Cthangover.Core.UI.Theming;
using Cthangover.Tools.Common;
using Godot;

namespace Cthangover.Tools.InteractiveEditor
{
    /// <summary>
    /// Preview of the interactives of one file over a scene background. Every
    /// collider is drawn (others faint, the selected one filled), so the author
    /// sees how objects of the same scene sit next to each other; clicking a
    /// collider selects it. The selected collider gets handles:
    /// <list type="bullet">
    /// <item>rect — top-left and bottom-right corners plus a centre handle
    /// that moves the whole rect;</item>
    /// <item>circle — centre and a radius handle;</item>
    /// <item>polygon — one handle per vertex (right-click deletes it); a click
    /// on empty background appends a vertex.</item>
    /// </list>
    /// Geometry is in the same normalised background space as
    /// <c>InteractiveObject</c>: x/y/width/height are fractions of the
    /// background size, a circle radius is a fraction of its shorter side.
    /// </summary>
    public partial class InteractiveEditorCanvas : BackgroundCanvas
    {
        private const int TopLeft = 0;
        private const int BottomRight = 1;
        private const int Move = 2;
        private const int RadiusHandle = 1;
        private const float MinExtent = 0.005f;

        /// <summary>Data being edited; set once by the window.</summary>
        public InteractiveEditorController Controller;

        /// <summary>Raised whenever geometry of the selected item changes.</summary>
        public event Action Changed;

        /// <summary>Raised when a click on the canvas selects another item (-1: none).</summary>
        public event Action<int> ItemPicked;

        public InteractiveEditorCanvas()
        {
            GuiInput += OnGuiInput;
        }

        /// <summary>Recreates the handles of the selected item's collider.</summary>
        public override void RebuildHandles()
        {
            ClearHandles();

            var hit = Controller?.Selected?.HitArea;
            if (hit == null || GetBackgroundRect().Size.X <= 0f)
                return;

            switch (hit.Type)
            {
                case "circle":
                    AddHandle(new Vector2(hit.X, hit.Y), UiTokens.Info, Move, p => { hit.X = p.X; hit.Y = p.Y; Moved(); });
                    AddHandle(RadiusPoint(hit), UiTokens.Info, RadiusHandle, p =>
                    {
                        var shortSide = ShortSide();
                        var centre = NormToLocal(new Vector2(hit.X, hit.Y));
                        hit.Radius = Mathf.Max(MinExtent, (NormToLocal(p) - centre).Length() / shortSide);
                        Moved();
                    });
                    break;
                case "polygon":
                    var vertices = hit.Vertices ?? Array.Empty<Vector2>();
                    for (var i = 0; i < vertices.Length; i++)
                    {
                        var index = i;
                        var handle = AddHandle(vertices[i], UiTokens.Accent, i, p => { hit.Vertices[index] = p; Moved(); });
                        handle.RightClicked = () => RemoveVertex(index);
                    }
                    break;
                default:
                    AddHandle(new Vector2(hit.X, hit.Y), UiTokens.Success, TopLeft, p =>
                    {
                        var right = hit.X + hit.Width;
                        var bottom = hit.Y + hit.Height;
                        hit.X = Mathf.Min(p.X, right - MinExtent);
                        hit.Y = Mathf.Min(p.Y, bottom - MinExtent);
                        hit.Width = right - hit.X;
                        hit.Height = bottom - hit.Y;
                        Moved();
                    });
                    AddHandle(new Vector2(hit.X + hit.Width, hit.Y + hit.Height), UiTokens.Success, BottomRight, p =>
                    {
                        hit.Width = Mathf.Max(MinExtent, p.X - hit.X);
                        hit.Height = Mathf.Max(MinExtent, p.Y - hit.Y);
                        Moved();
                    });
                    AddHandle(new Vector2(hit.X + hit.Width / 2f, hit.Y + hit.Height / 2f), UiTokens.AccentBright, Move, p =>
                    {
                        hit.X = Mathf.Clamp(p.X - hit.Width / 2f, 0f, 1f - hit.Width);
                        hit.Y = Mathf.Clamp(p.Y - hit.Height / 2f, 0f, 1f - hit.Height);
                        Moved();
                    }, 12f);
                    break;
            }

            foreach (var handle in _handles)
                handle.DragEnd = () => { RebuildHandles(); Changed?.Invoke(); };
        }

        /// <summary>Keeps the other handles glued to the shape while one is dragged.</summary>
        private void Moved()
        {
            var hit = Controller.Selected.HitArea;
            foreach (var handle in _handles)
            {
                Vector2? target = hit.Type switch
                {
                    "circle" => handle.Index == Move ? new Vector2(hit.X, hit.Y) : RadiusPoint(hit),
                    "polygon" => null,
                    _ => handle.Index switch
                    {
                        TopLeft => new Vector2(hit.X, hit.Y),
                        BottomRight => new Vector2(hit.X + hit.Width, hit.Y + hit.Height),
                        _ => new Vector2(hit.X + hit.Width / 2f, hit.Y + hit.Height / 2f)
                    }
                };
                if (target.HasValue)
                    handle.CenterAt(GlobalPosition + NormToLocal(target.Value));
            }

            QueueRedraw();
            Changed?.Invoke();
        }

        private Vector2 RadiusPoint(HitAreaDefinition hit)
        {
            var rect = GetBackgroundRect();
            var offset = rect.Size.X > 0f ? hit.Radius * ShortSide() / rect.Size.X : 0f;
            return new Vector2(hit.X + offset, hit.Y);
        }

        private float ShortSide()
        {
            var rect = GetBackgroundRect();
            return Mathf.Max(1f, Mathf.Min(rect.Size.X, rect.Size.Y));
        }

        private void RemoveVertex(int index)
        {
            var hit = Controller.Selected?.HitArea;
            if (hit?.Vertices == null || index >= hit.Vertices.Length)
                return;

            hit.Vertices = hit.Vertices.Where((_, i) => i != index).ToArray();
            RebuildHandles();
            QueueRedraw();
            Changed?.Invoke();
        }

        private void OnGuiInput(InputEvent @event)
        {
            if (Controller == null || @event is not InputEventMouseButton mb || !mb.Pressed || mb.ButtonIndex != MouseButton.Left)
                return;

            var norm = LocalToNorm(mb.Position);
            var hitIndex = HitTest(norm);
            var selected = Controller.Selected;

            if (hitIndex >= 0 && hitIndex != Controller.SelectedIndex)
            {
                Controller.Select(hitIndex);
                RebuildHandles();
                QueueRedraw();
                ItemPicked?.Invoke(hitIndex);
            }
            else if (hitIndex < 0 && selected?.HitArea.Type == "polygon")
            {
                var vertices = selected.HitArea.Vertices ?? Array.Empty<Vector2>();
                selected.HitArea.Vertices = vertices.Append(Clamp01(norm)).ToArray();
                RebuildHandles();
                QueueRedraw();
                Changed?.Invoke();
            }

            AcceptEvent();
        }

        /// <summary>Topmost item whose collider contains the normalised point, or -1.</summary>
        private int HitTest(Vector2 norm)
        {
            var rect = GetBackgroundRect();
            var local = NormToLocal(norm);
            for (var i = Controller.Items.Count - 1; i >= 0; i--)
            {
                var hit = Controller.Items[i].HitArea;
                if (hit == null)
                    continue;

                var inside = hit.Type switch
                {
                    "circle" => (local - NormToLocal(new Vector2(hit.X, hit.Y))).Length() <= hit.Radius * Mathf.Min(rect.Size.X, rect.Size.Y),
                    "polygon" => hit.Vertices is { Length: >= 3 } && Geometry2D.IsPointInPolygon(norm, hit.Vertices),
                    _ => new Rect2(hit.X, hit.Y, hit.Width, hit.Height).HasPoint(norm)
                };
                if (inside)
                    return i;
            }
            return -1;
        }

        protected override void DrawContent(Rect2 rect)
        {
            if (Controller == null)
                return;

            for (var i = 0; i < Controller.Items.Count; i++)
                DrawCollider(Controller.Items[i], rect, i == Controller.SelectedIndex);
        }

        private void DrawCollider(InteractiveDefinition def, Rect2 rect, bool selected)
        {
            var hit = def.HitArea;
            if (hit == null)
                return;

            var line = selected ? UiTokens.AccentBright : UiTokens.Alpha(UiTokens.Ink, 0.55f);
            var fill = selected ? UiTokens.Alpha(UiTokens.Accent, 0.22f) : UiTokens.Alpha(UiTokens.Ink, 0.06f);
            Vector2 labelAt;

            switch (hit.Type)
            {
                case "circle":
                    var centre = NormToLocal(new Vector2(hit.X, hit.Y));
                    var radius = hit.Radius * Mathf.Min(rect.Size.X, rect.Size.Y);
                    DrawCircle(centre, radius, fill);
                    DrawArc(centre, radius, 0f, Mathf.Tau, 48, line, selected ? 2f : 1f, true);
                    labelAt = centre + new Vector2(-radius, -radius);
                    break;
                case "polygon":
                    var vertices = hit.Vertices ?? Array.Empty<Vector2>();
                    var points = vertices.Select(NormToLocal).ToArray();
                    if (points.Length >= 3 && Geometry2D.TriangulatePolygon(points).Length > 0)
                        DrawColoredPolygon(points, fill);
                    if (points.Length >= 2)
                        DrawPolyline(points.Append(points[0]).ToArray(), line, selected ? 2f : 1f, true);
                    labelAt = points.Length > 0 ? points[0] : rect.Position;
                    break;
                default:
                    var box = new Rect2(NormToLocal(new Vector2(hit.X, hit.Y)), new Vector2(hit.Width, hit.Height) * rect.Size);
                    DrawRect(box, fill);
                    DrawRect(box, line, false, selected ? 2f : 1f);
                    labelAt = box.Position;
                    break;
            }

            var font = GetThemeDefaultFont();
            var size = UiTokens.FontSmall;
            DrawString(font, labelAt + new Vector2(4f, -4f), def.ID ?? "?", HorizontalAlignment.Left, -1, size,
                selected ? UiTokens.AccentBright : UiTokens.Body);
        }
    }
}
