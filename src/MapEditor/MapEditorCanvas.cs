using System;
using System.Collections.Generic;
using Cthangover.Core.Mods.Resolvers;
using Cthangover.Core.UI.Theming;
using Cthangover.Core.UI.Tool;
using Godot;

namespace Cthangover.Tools.MapEditor
{
    /// <summary>
    /// Interactive preview of the edited map: draws the background with the
    /// exact aspect-preserving layout used by <c>MapWidget</c>, overlays every
    /// zone polygon, marker and text label, and hosts the draggable handles.
    /// Implements the two editor modes:
    /// <list type="bullet">
    /// <item><b>select</b> — clicking picks the topmost zone (polygon first,
    /// then marker/label dots); handles move vertices and positions;</item>
    /// <item><b>draw</b> — clicking appends polygon vertices to the selected
    /// region and double-click finishes the polygon and returns to select
    /// mode.</item>
    /// </list>
    /// Right-click deletes a polygon vertex in either mode: the vertex nearest
    /// to the pointer. While drawing, a click that misses every vertex removes
    /// the last one ("undo point"); in select mode only a targeted vertex is
    /// deleted.
    /// The canvas owns no data: it reads and mutates
    /// <see cref="MapEditorController"/> and raises <see cref="Changed"/> so the
    /// window can refresh the sidebar and JSON preview. Drawing is connected
    /// through the "draw" signal and input through the GuiInput event, because
    /// virtual method overrides are unreliable in mod assemblies.
    /// <para/>
    /// Zone fills and outlines are painted through <see cref="MapZoneStyle"/> —
    /// the same definition the game's <c>MapZonesLayer</c> and the window's
    /// game-like preview use — so a zone the author sees here and the same zone
    /// on the live map are guaranteed to look identical.
    /// </summary>
    public partial class MapEditorCanvas : Control
    {
        /// <summary>Mode constant: clicking selects zones and drags handles.</summary>
        public const string ModeSelect = "select";

        /// <summary>Mode constant: clicking appends polygon vertices.</summary>
        public const string ModeDraw = "draw";

        private static readonly Color VertexColor = MapZoneStyle.VertexHandle;
        private static readonly Color MarkerColor = MapZoneStyle.MarkerHandle;
        private static readonly Color LabelColor = MapZoneStyle.LabelHandle;

        private readonly List<MapEditorHandle> _handles = new();

        /// <summary>Data being edited; set once by the window.</summary>
        public MapEditorController Controller;

        /// <summary>Reference background; <c>null</c> falls back to a flat rectangle.</summary>
        public Texture2D Background;

        /// <summary>Current interaction mode, one of <see cref="ModeSelect"/> / <see cref="ModeDraw"/>.</summary>
        public string Mode { get; private set; } = ModeSelect;

        /// <summary>Raised whenever the canvas mutates controller data (drag, draw, pick).</summary>
        public event Action Changed;

        /// <summary>Raised with the newly picked zone index (-1 for empty space).</summary>
        public event Action<int> ZonePicked;

        public MapEditorCanvas()
        {
            MouseFilter = MouseFilterEnum.Stop;
            ClipContents = true;
            MapZoneStyle.Prepare(this);
            GuiInput += OnGuiInput;
            MouseExited += OnMouseExited;
            Resized += OnResized;
            Connect("draw", Callable.From((Action)DrawAll));
        }

        /// <summary>Switches the interaction mode and refreshes the handles.</summary>
        public void SetMode(string mode)
        {
            Mode = mode == ModeDraw ? ModeDraw : ModeSelect;
            RebuildHandles();
            QueueRedraw();
        }

        /// <summary>Assigns the background texture and rebuilds the layout.</summary>
        public void SetBackground(Texture2D texture)
        {
            Background = texture;
            RebuildHandles();
            QueueRedraw();
        }

        /// <summary>Recreates the handles from the current selection. Call after structural changes.</summary>
        public void RebuildHandles()
        {
            foreach (var handle in _handles)
                handle.QueueFree();
            _handles.Clear();

            var zone = Controller?.Selected;
            if (zone == null)
                return;

            var rect = GetBackgroundRect();
            if (rect.Size.X <= 0f || rect.Size.Y <= 0f)
                return;

            if (zone.Points != null)
            {
                for (var i = 0; i < zone.Points.Count; i++)
                    AddHandle(rect, zone.Points[i], VertexColor, i);
            }

            if (HasMarker(zone))
                AddHandle(rect, zone.ImagePos, MarkerColor, MapEditorHandle.MarkerHandle);

            if (zone.LabelPos.HasValue)
                AddHandle(rect, zone.LabelPos.Value, LabelColor, MapEditorHandle.LabelHandle);
        }

        /// <summary>
        /// Aspect-preserving rectangle the background occupies inside the
        /// canvas. Must stay identical to the runtime layout: the saved
        /// normalised coordinates mean nothing if the editor and the game
        /// disagree about this rectangle.
        /// </summary>
        public Rect2 GetBackgroundRect()
        {
            var controlSize = Size;
            if (controlSize.X <= 0f || controlSize.Y <= 0f)
                return new Rect2();

            var textureSize = Background?.GetSize() ?? new Vector2(1920, 1080);
            if (textureSize.X <= 0f || textureSize.Y <= 0f)
                textureSize = new Vector2(1920, 1080);

            var scale = Mathf.Min(controlSize.X / textureSize.X, controlSize.Y / textureSize.Y);
            var size = textureSize * scale;
            return new Rect2((controlSize - size) / 2f, size);
        }

        private void AddHandle(Rect2 rect, Vector2 normalized, Color color, int index)
        {
            var handle = new MapEditorHandle(color, index);
            handle.DragUpdate = center => OnHandleDrag(index, center);
            handle.DragEnd = () => Changed?.Invoke();
            handle.RightClicked = () => RemoveVertexHandle(index);
            AddChild(handle);
            handle.GlobalPosition = GlobalPosition + rect.Position + Clamp01(normalized) * rect.Size - handle.Size / 2f;
            _handles.Add(handle);
        }

        private void OnHandleDrag(int index, Vector2 globalCenter)
        {
            if (Controller == null)
                return;

            var rect = GetBackgroundRect();
            if (rect.Size.X <= 0f || rect.Size.Y <= 0f)
                return;

            var local = globalCenter - GlobalPosition - rect.Position;
            var normalized = Clamp01(new Vector2(local.X / rect.Size.X, local.Y / rect.Size.Y));

            if (index == MapEditorHandle.MarkerHandle)
                Controller.SetImagePos(normalized);
            else if (index == MapEditorHandle.LabelHandle)
                Controller.SetLabelPos(normalized);
            else
                Controller.SetVertex(index, normalized);

            QueueRedraw();
            Changed?.Invoke();
        }

        private void OnGuiInput(InputEvent @event)
        {
            if (Controller == null || @event is not InputEventMouseButton mb || !mb.Pressed)
                return;

            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (Mode == ModeDraw)
                {
                    Controller.AddVertex(Clamp01(PixelToNorm(mb.Position)));
                    if (mb.DoubleClick)
                        Mode = ModeSelect;
                    RebuildHandles();
                    Changed?.Invoke();
                    QueueRedraw();
                }
                else
                {
                    var hit = HitTest(mb.Position);
                    if (hit != Controller.SelectedIndex)
                    {
                        Controller.Select(hit);
                        RebuildHandles();
                        ZonePicked?.Invoke(hit);
                        Changed?.Invoke();
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
            QueueRedraw();
        }

        private void OnResized()
        {
            RebuildHandles();
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
                if (HasMarker(zone) && (local - ToPixel(zone.ImagePos, rect)).Length() <= 24f)
                    return i;
                if (zone.LabelPos.HasValue && (local - ToPixel(zone.LabelPos.Value, rect)).Length() <= 24f)
                    return i;
            }

            return -1;
        }

        private void DrawAll()
        {
            var rect = GetBackgroundRect();
            if (rect.Size.X <= 0f || rect.Size.Y <= 0f)
                return;

            if (Background != null)
                DrawTextureRect(Background, rect, false);
            else
                DrawRect(rect, UiTokens.Surface2);

            var zones = Controller?.Zones;
            if (zones == null)
                return;

            for (var i = 0; i < zones.Count; i++)
            {
                var selected = i == Controller.SelectedIndex;
                DrawZone(zones[i], rect, selected);
            }
        }

        private void DrawZone(MapZone zone, Rect2 rect, bool selected)
        {
            if (zone.Points != null && zone.Points.Count >= 2)
                MapZoneStyle.Draw(this, ToPixels(zone.Points, rect), selected, false, zone.Locked);

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

        private Vector2 PixelToNorm(Vector2 local)
        {
            var rect = GetBackgroundRect();
            if (rect.Size.X <= 0f || rect.Size.Y <= 0f)
                return Vector2.Zero;

            return new Vector2((local.X - rect.Position.X) / rect.Size.X, (local.Y - rect.Position.Y) / rect.Size.Y);
        }

        private static Vector2 Clamp01(Vector2 value)
        {
            return new Vector2(Mathf.Clamp(value.X, 0f, 1f), Mathf.Clamp(value.Y, 0f, 1f));
        }

        /// <summary>
        /// A zone is marker-capable when it has an icon, or when it is still a
        /// bare marker placeholder — no polygon and no text label. The
        /// placeholder keeps a freshly created marker visible and draggable
        /// before an icon is picked; once the zone grows a region or becomes a
        /// pure inscription, the marker is only shown when an icon exists.
        /// </summary>
        private static bool HasMarker(MapZone zone)
        {
            if (!string.IsNullOrEmpty(zone.Image))
                return true;

            return (zone.Points == null || zone.Points.Count == 0) && !zone.LabelPos.HasValue;
        }

        /// <summary>
        /// Right-click handler: removes the polygon vertex nearest to the
        /// pointer. While drawing, a click that misses every vertex removes the
        /// last one instead, which makes right-click a natural "undo point"; in
        /// select mode only a targeted vertex is deleted, so a stray click
        /// cannot destroy geometry.
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
            if (index < 0)
                return;

            Controller.RemoveVertex(index);
            RebuildHandles();
            Changed?.Invoke();
            QueueRedraw();
        }

        private static int FindNearestVertex(MapZone zone, Vector2 local, Rect2 rect)
        {
            var best = -1;
            var bestDistance = 24f;

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

        /// <summary>
        /// Deletes a vertex from its handle's right-click. Marker and label
        /// handles (negative indexes) are not vertices and are ignored.
        /// </summary>
        private void RemoveVertexHandle(int index)
        {
            if (index < 0 || Controller == null)
                return;

            Controller.RemoveVertex(index);
            RebuildHandles();
            Changed?.Invoke();
            QueueRedraw();
        }
    }
}
