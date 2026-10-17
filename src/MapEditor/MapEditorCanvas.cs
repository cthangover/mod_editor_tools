using System;
using System.Collections.Generic;
using Cthangover.Core.Mods.Resolvers;
using Cthangover.Core.UI.Theming;
using Cthangover.Mods.Map;
using Cthangover.Tools.Common;
using Godot;

namespace Cthangover.Tools.MapEditor
{
    /// <summary>
    /// Interactive preview of the edited map: the background (laid out by
    /// <see cref="BackgroundCanvas"/> exactly like <c>MapWidget</c>), every zone
    /// polygon, marker and text label, and the draggable handles of the
    /// selected zone. Two modes:
    /// <list type="bullet">
    /// <item><b>select</b> — a click picks the topmost zone (polygon first,
    /// then marker/label dots); handles move vertices and positions;</item>
    /// <item><b>draw</b> — clicks append polygon vertices to the selected
    /// region while a dashed rubber band shows the next edge; a double-click
    /// (or Enter/Esc via the window) finishes the polygon.</item>
    /// </list>
    /// Right-click deletes the polygon vertex nearest to the pointer; while
    /// drawing, a click that misses every vertex removes the last one ("undo
    /// point"), in select mode only a targeted vertex is deleted.
    /// <para/>
    /// The canvas owns no data: it reads and mutates
    /// <see cref="MapEditorController"/> and raises <see cref="Changed"/> so
    /// the window can refresh the sidebar and JSON. Zone fills and outlines are
    /// painted through <see cref="MapZoneStyle"/> — the definition the game's
    /// <c>MapZonesLayer</c> uses — so the editor and the live map look the same.
    /// </summary>
    public partial class MapEditorCanvas : BackgroundCanvas
    {
        /// <summary>Mode constant: clicking selects zones and drags handles.</summary>
        public const string ModeSelect = "select";

        /// <summary>Mode constant: clicking appends polygon vertices.</summary>
        public const string ModeDraw = "draw";

        /// <summary>Handle index of the marker position.</summary>
        public const int MarkerHandle = -2;

        /// <summary>Handle index of the label position.</summary>
        public const int LabelHandle = -3;

        private const float PickRadius = 24f;

        private static readonly Color VertexColor = MapZoneStyle.VertexHandle;
        private static readonly Color MarkerColor = MapZoneStyle.MarkerHandle;
        private static readonly Color LabelColor = MapZoneStyle.LabelHandle;

        /// <summary>Data being edited; set once by the window.</summary>
        public MapEditorController Controller;

        /// <summary>Current interaction mode, one of <see cref="ModeSelect"/> / <see cref="ModeDraw"/>.</summary>
        public string Mode { get; private set; } = ModeSelect;

        /// <summary>Raised whenever the canvas mutates controller data (drag, draw, delete).</summary>
        public event Action Changed;

        /// <summary>Raised with the newly picked zone index (-1 for empty space).</summary>
        public event Action<int> ZonePicked;

        /// <summary>Raised when the canvas leaves draw mode on its own (double-click finishing a polygon).</summary>
        public event Action ModeChanged;

        private Vector2? _pointer;

        public MapEditorCanvas()
        {
            MapZoneStyle.Prepare(this);
            GuiInput += OnGuiInput;
            MouseExited += OnMouseExited;
        }

        /// <summary>Switches the interaction mode and refreshes the handles.</summary>
        public void SetMode(string mode)
        {
            Mode = mode == ModeDraw ? ModeDraw : ModeSelect;
            MouseDefaultCursorShape = Mode == ModeDraw ? CursorShape.Cross : CursorShape.Arrow;
            RebuildHandles();
            QueueRedraw();
        }

        /// <summary>Leaves draw mode (double-click, Enter, Esc) and tells the window.</summary>
        public void FinishDrawing()
        {
            if (Mode != ModeDraw)
                return;

            SetMode(ModeSelect);
            _pointer = null;
            ModeChanged?.Invoke();
        }

        /// <summary>Recreates the handles from the current selection. Call after structural changes.</summary>
        public override void RebuildHandles()
        {
            ClearHandles();

            var zone = Controller?.Selected;
            if (zone == null || GetBackgroundRect().Size.X <= 0f)
                return;

            if (zone.Points != null)
            {
                for (var i = 0; i < zone.Points.Count; i++)
                {
                    var index = i;
                    var handle = AddHandle(zone.Points[i], VertexColor, i, p => OnHandleDrag(index, p));
                    handle.RightClicked = () => RemoveVertexAt(index);
                    handle.DragEnd = () => Changed?.Invoke();
                }
            }

            if (HasMarker(zone))
                AddHandle(zone.ImagePos, MarkerColor, MarkerHandle, p => OnHandleDrag(MarkerHandle, p), 18f).DragEnd = () => Changed?.Invoke();

            if (zone.LabelPos.HasValue)
                AddHandle(zone.LabelPos.Value, LabelColor, LabelHandle, p => OnHandleDrag(LabelHandle, p), 18f).DragEnd = () => Changed?.Invoke();
        }

        private void OnHandleDrag(int index, Vector2 normalized)
        {
            if (Controller == null)
                return;

            if (index == MarkerHandle)
                Controller.SetImagePos(normalized);
            else if (index == LabelHandle)
                Controller.SetLabelPos(normalized);
            else
                Controller.SetVertex(index, normalized);

            QueueRedraw();
            Changed?.Invoke();
        }

        private void OnGuiInput(InputEvent @event)
        {
            if (Controller == null)
                return;

            if (@event is InputEventMouseMotion motion)
            {
                if (Mode == ModeDraw)
                {
                    _pointer = motion.Position;
                    QueueRedraw();
                }
                return;
            }

            if (@event is not InputEventMouseButton mb || !mb.Pressed)
                return;

            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (Mode == ModeDraw)
                {
                    // The first click of a double-click already placed the
                    // vertex; the double-click itself only finishes the polygon.
                    if (mb.DoubleClick)
                    {
                        FinishDrawing();
                    }
                    else
                    {
                        Controller.AddVertex(Clamp01(LocalToNorm(mb.Position)));
                        RebuildHandles();
                        Changed?.Invoke();
                        QueueRedraw();
                    }
                }
                else
                {
                    var hit = HitTest(mb.Position);
                    if (hit != Controller.SelectedIndex)
                    {
                        Controller.Select(hit);
                        RebuildHandles();
                        ZonePicked?.Invoke(hit);
                        QueueRedraw();
                    }
                }

                AcceptEvent();
            }
            else if (mb.ButtonIndex == MouseButton.Right)
            {
                RemoveVertexAtPointer(mb.Position, Mode == ModeDraw);
                AcceptEvent();
            }
        }

        private void OnMouseExited()
        {
            _pointer = null;
            QueueRedraw();
        }

        /// <summary>Topmost zone under a canvas-local point: polygons win over marker/label dots.</summary>
        private int HitTest(Vector2 local)
        {
            var zones = Controller.Zones;
            var rect = GetBackgroundRect();

            for (var i = zones.Count - 1; i >= 0; i--)
            {
                var points = zones[i].Points;
                if (points != null && points.Count >= 3 && Geometry2D.IsPointInPolygon(local, ToPixels(points, rect)))
                    return i;
            }

            for (var i = zones.Count - 1; i >= 0; i--)
            {
                var zone = zones[i];
                if (HasMarker(zone) && (local - ToPixel(zone.ImagePos, rect)).Length() <= PickRadius)
                    return i;
                if (zone.LabelPos.HasValue && (local - ToPixel(zone.LabelPos.Value, rect)).Length() <= PickRadius)
                    return i;
            }

            return -1;
        }

        protected override void DrawContent(Rect2 rect)
        {
            var zones = Controller?.Zones;
            if (zones == null)
                return;

            for (var i = 0; i < zones.Count; i++)
                DrawZone(zones[i], rect, i == Controller.SelectedIndex);

            DrawRubberBand(rect);
        }

        /// <summary>
        /// In draw mode shows where the next vertex goes: a dashed segment from
        /// the last vertex to the pointer and back to the first one, so the
        /// author sees the polygon closing before clicking.
        /// </summary>
        private void DrawRubberBand(Rect2 rect)
        {
            var points = Controller.Selected?.Points;
            if (Mode != ModeDraw || !_pointer.HasValue || points == null || points.Count == 0)
                return;

            var pointer = _pointer.Value;
            DrawDashedLine(ToPixel(points[points.Count - 1], rect), pointer, UiTokens.AccentBright, 1.5f, 6f);
            if (points.Count >= 2)
                DrawDashedLine(pointer, ToPixel(points[0], rect), UiTokens.AccentLine, 1f, 6f);
        }

        private void DrawZone(MapZone zone, Rect2 rect, bool selected)
        {
            if (zone.Points != null && zone.Points.Count >= 2)
            {
                var locked = MapZoneRules.IsLocked(zone, !zone.Locked);
                MapZoneStyle.Draw(this, ToPixels(zone.Points, rect), selected, false, locked, zone.Hidden);
            }

            if (HasMarker(zone))
                DrawMarker(zone, ToPixel(zone.ImagePos, rect), selected);

            if (zone.LabelPos.HasValue)
                DrawLabelPill(zone, ToPixel(zone.LabelPos.Value, rect), selected);
        }

        /// <summary>
        /// Draws the marker emblem. A marker without an icon is rendered as a
        /// placeholder crosshair instead of disappearing, so a freshly created
        /// marker stays visible, selectable and draggable while the author is
        /// still choosing an icon.
        /// </summary>
        private void DrawMarker(MapZone zone, Vector2 center, bool selected)
        {
            var outline = selected ? UiTokens.Accent : UiTokens.AccentLine;
            var accent = selected ? UiTokens.Accent : MarkerColor;

            var icon = string.IsNullOrEmpty(zone.Image) ? null : Textures.Resolve(zone.Image);
            if (icon == null)
            {
                DrawCircle(center, 18f, UiTokens.Alpha(UiTokens.Surface0, 0.55f));
                DrawArc(center, 18f, 0f, Mathf.Tau, 32, accent, 2f, true);
                DrawLine(center - new Vector2(7f, 0f), center + new Vector2(7f, 0f), accent, 2f);
                DrawLine(center - new Vector2(0f, 7f), center + new Vector2(0f, 7f), accent, 2f);
                return;
            }

            DrawCircle(center, 18f, UiTokens.Alpha(UiTokens.White, 0.85f));
            DrawArc(center, 18f, 0f, Mathf.Tau, 32, outline, 1.5f, true);
            DrawTextureRect(icon, new Rect2(center - new Vector2(14f, 14f), new Vector2(28f, 28f)), false);
        }

        private void DrawLabelPill(MapZone zone, Vector2 center, bool selected)
        {
            var text = string.IsNullOrEmpty(zone.Title) ? zone.ID : TranslationServer.Translate(zone.Title).ToString();
            if (string.IsNullOrEmpty(text))
                text = zone.ID;

            var font = GetThemeDefaultFont();
            var fontSize = GetThemeDefaultFontSize();
            var textSize = font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize);
            var pill = new Rect2(center - new Vector2(textSize.X / 2f + 8f, textSize.Y / 2f + 3f), textSize + new Vector2(16f, 6f));

            DrawRect(pill, UiTokens.Alpha(UiTokens.Surface0, 0.85f));
            DrawRect(pill, selected ? UiTokens.Accent : UiTokens.Line, false, 1f);
            DrawString(font, new Vector2(pill.Position.X + 8f, pill.Position.Y + pill.Size.Y / 2f + fontSize / 3f),
                text, HorizontalAlignment.Left, -1, fontSize, UiTokens.Ink);
        }

        private static Vector2[] ToPixels(IReadOnlyList<Vector2> normalized, Rect2 rect)
        {
            var result = new Vector2[normalized.Count];
            for (var i = 0; i < result.Length; i++)
                result[i] = rect.Position + normalized[i] * rect.Size;
            return result;
        }

        private static Vector2 ToPixel(Vector2 normalized, Rect2 rect)
        {
            return rect.Position + normalized * rect.Size;
        }

        /// <summary>
        /// A zone is marker-capable when it has an icon, or when it is still a
        /// bare marker placeholder — no polygon and no text label. The
        /// placeholder keeps a freshly created marker visible and draggable
        /// before an icon is picked; once the zone grows a region or becomes a
        /// pure inscription, the marker is only shown when an icon exists. The
        /// region currently being drawn is excluded while it has no vertices.
        /// </summary>
        private bool HasMarker(MapZone zone)
        {
            if (!string.IsNullOrEmpty(zone.Image))
                return true;

            if (Mode == ModeDraw && zone == Controller?.Selected)
                return false;

            return (zone.Points == null || zone.Points.Count == 0) && !zone.LabelPos.HasValue;
        }

        /// <summary>
        /// Right-click on empty canvas: removes the polygon vertex nearest to
        /// the pointer. While drawing, a click that misses every vertex removes
        /// the last one instead, which makes right-click a natural "undo
        /// point"; in select mode only a targeted vertex is deleted, so a stray
        /// click cannot destroy geometry.
        /// </summary>
        private void RemoveVertexAtPointer(Vector2 local, bool fallbackToLast)
        {
            var zone = Controller?.Selected;
            if (zone?.Points == null || zone.Points.Count == 0)
                return;

            var rect = GetBackgroundRect();
            var index = FindNearestVertex(zone, local, rect);
            if (index < 0 && fallbackToLast)
                index = zone.Points.Count - 1;
            if (index >= 0)
                RemoveVertexAt(index);
        }

        private void RemoveVertexAt(int index)
        {
            if (Controller == null)
                return;

            Controller.RemoveVertex(index);
            RebuildHandles();
            Changed?.Invoke();
            QueueRedraw();
        }

        private static int FindNearestVertex(MapZone zone, Vector2 local, Rect2 rect)
        {
            var best = -1;
            var bestDistance = PickRadius;

            for (var i = 0; i < zone.Points.Count; i++)
            {
                var distance = (local - ToPixel(zone.Points[i], rect)).Length();
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }
    }
}
