using System;
using System.Collections.Generic;
using Cthangover.Core.UI.Theming;
using Godot;

namespace Cthangover.Tools.Common
{
    /// <summary>
    /// Base of the editors that place things over a scene background (map
    /// zones, interactive colliders, lights). Draws the background with the
    /// same aspect-preserving "fit" layout the game uses and converts between
    /// canvas pixels and the normalised [0..1] coordinates stored in the data.
    /// Getting this rectangle wrong is what made saved positions drift: the
    /// data means "fraction of the background", not "fraction of the panel".
    /// <para/>
    /// Subclasses paint their overlay in <see cref="DrawContent"/> and rebuild
    /// their <see cref="DragHandle"/>s in <see cref="RebuildHandles"/>, which is
    /// also called whenever the canvas is resized or the background changes.
    /// </summary>
    public abstract partial class BackgroundCanvas : Control
    {
        private static readonly Vector2 FallbackSize = new(1920, 1024);

        /// <summary>Live handles of the current selection.</summary>
        protected readonly List<DragHandle> _handles = new();

        /// <summary>Reference background; <c>null</c> draws a flat placeholder.</summary>
        public Texture2D Background { get; private set; }

        protected BackgroundCanvas()
        {
            MouseFilter = MouseFilterEnum.Stop;
            ClipContents = true;
            Resized += OnResized;
            Connect("draw", Callable.From((Action)DrawAll));
        }

        /// <summary>Assigns the background texture and relayouts handles.</summary>
        public void SetBackground(Texture2D texture)
        {
            Background = texture;
            RebuildHandles();
            QueueRedraw();
        }

        /// <summary>Recreates the handles from the current selection.</summary>
        public abstract void RebuildHandles();

        /// <summary>Paints the overlay above the background inside <paramref name="rect"/>.</summary>
        protected abstract void DrawContent(Rect2 rect);

        /// <summary>
        /// Aspect-preserving rectangle the background occupies inside the
        /// canvas — identical to the runtime "fit" layout.
        /// </summary>
        public Rect2 GetBackgroundRect()
        {
            var controlSize = Size;
            if (controlSize.X <= 0f || controlSize.Y <= 0f)
                return new Rect2();

            var textureSize = Background?.GetSize() ?? FallbackSize;
            if (textureSize.X <= 0f || textureSize.Y <= 0f)
                textureSize = FallbackSize;

            var scale = Mathf.Min(controlSize.X / textureSize.X, controlSize.Y / textureSize.Y);
            var size = textureSize * scale;
            return new Rect2((controlSize - size) / 2f, size);
        }

        /// <summary>Canvas-local pixel of a normalised point.</summary>
        public Vector2 NormToLocal(Vector2 normalized)
        {
            var rect = GetBackgroundRect();
            return rect.Position + normalized * rect.Size;
        }

        /// <summary>Normalised point of a canvas-local pixel (not clamped).</summary>
        public Vector2 LocalToNorm(Vector2 local)
        {
            var rect = GetBackgroundRect();
            if (rect.Size.X <= 0f || rect.Size.Y <= 0f)
                return Vector2.Zero;
            return (local - rect.Position) / rect.Size;
        }

        /// <summary>Clamps both axes into [0..1].</summary>
        public static Vector2 Clamp01(Vector2 value)
        {
            return new Vector2(Mathf.Clamp(value.X, 0f, 1f), Mathf.Clamp(value.Y, 0f, 1f));
        }

        /// <summary>Frees every handle.</summary>
        protected void ClearHandles()
        {
            foreach (var handle in _handles)
                handle.QueueFree();
            _handles.Clear();
        }

        /// <summary>
        /// Adds a handle centred on a normalised point. The handle enters the
        /// tree before it is positioned (setting a global position on a
        /// detached Control lands it in the wrong place). <paramref name="drag"/>
        /// receives the clamped normalised point while dragging.
        /// </summary>
        protected DragHandle AddHandle(Vector2 normalized, Color color, int index, Action<Vector2> drag, float diameter = 14f)
        {
            var handle = new DragHandle(color, index, diameter);
            handle.DragUpdate = globalCenter => drag(Clamp01(LocalToNorm(globalCenter - GlobalPosition)));
            AddChild(handle);
            handle.CenterAt(GlobalPosition + NormToLocal(Clamp01(normalized)));
            _handles.Add(handle);
            return handle;
        }

        private void OnResized()
        {
            RebuildHandles();
            QueueRedraw();
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

            DrawContent(rect);
        }
    }
}
