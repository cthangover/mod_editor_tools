using System;
using Cthangover.Core.UI.Theming;
using Cthangover.Tools.Common;
using Godot;

namespace Cthangover.Core.UI.Tool.LightEditor
{
    /// <summary>
    /// Night-time preview of a scene with its static lights. The background is
    /// drawn through the game's <c>timed_sprite</c> shader (set as this
    /// control's material), so the preview lights the picture exactly like the
    /// game; the lights' positions are pushed to the shader in this canvas'
    /// coordinates and their radii scaled from the 1920-px game width to the
    /// drawn background width. Radius rings and handles live in a separate
    /// child overlay — anything drawn by this control itself would be shaded.
    /// <para/>
    /// A click on a handle selects that light and drags it; a double-click on
    /// empty background adds a light there.
    /// </summary>
    public partial class LightEditorCanvas : BackgroundCanvas
    {
        private readonly Control _overlay;

        /// <summary>Data being edited; set once by the window.</summary>
        public LightEditorController Controller;

        /// <summary>Preview material (may be null when the shader is missing).</summary>
        public ShaderMaterial PreviewMaterial
        {
            get => Material as ShaderMaterial;
            set => Material = value;
        }

        /// <summary>Raised when a light moved or was added on the canvas.</summary>
        public event Action Changed;

        /// <summary>Raised when the selection changed on the canvas.</summary>
        public event Action SelectionChanged;

        public LightEditorCanvas()
        {
            _overlay = new Control { MouseFilter = MouseFilterEnum.Ignore };
            AddChild(_overlay);
            _overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _overlay.Connect("draw", Callable.From((Action)DrawRings));
            GuiInput += OnGuiInput;
        }

        /// <summary>One handle per light, coloured like the light.</summary>
        public override void RebuildHandles()
        {
            ClearHandles();
            if (Controller == null || GetBackgroundRect().Size.X <= 0f)
                return;

            for (var i = 0; i < Controller.Lights.Count; i++)
            {
                var index = i;
                var light = Controller.Lights[i];
                var handle = AddHandle(new Vector2(light.X, light.Y), light.ToColor(), i, p =>
                {
                    light.X = p.X;
                    light.Y = p.Y;
                    Refresh();
                    Changed?.Invoke();
                }, index == Controller.SelectedIndex ? 20f : 16f);
                handle.Clicked = () => SelectLight(index);
            }

            Refresh();
        }

        /// <summary>Redraws the preview and re-uploads the shader uniforms.</summary>
        public void Refresh()
        {
            QueueRedraw();
            _overlay.QueueRedraw();
        }

        private void SelectLight(int index)
        {
            if (index == Controller.SelectedIndex)
                return;

            Controller.Select(index);
            SelectionChanged?.Invoke();
            CallDeferred(nameof(RebuildHandles));
        }

        private void OnGuiInput(InputEvent @event)
        {
            if (Controller == null || @event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
                return;

            if (mb.DoubleClick && Controller.Add(Clamp01(LocalToNorm(mb.Position))) != null)
            {
                RebuildHandles();
                Changed?.Invoke();
                SelectionChanged?.Invoke();
            }

            AcceptEvent();
        }

        protected override void DrawContent(Rect2 rect)
        {
            // The shader compares against world_pos (MODEL_MATRIX * VERTEX), i.e.
            // canvas coordinates of the window, not this control's local ones.
            Controller?.UpdatePreview(PreviewMaterial, n => GlobalPosition + NormToLocal(n), rect.Size.X / LightEditorController.GameWidth);
        }

        private void DrawRings()
        {
            var rect = GetBackgroundRect();
            if (Controller == null || rect.Size.X <= 0f)
                return;

            var scale = rect.Size.X / LightEditorController.GameWidth;
            for (var i = 0; i < Controller.Lights.Count; i++)
            {
                var light = Controller.Lights[i];
                var selected = i == Controller.SelectedIndex;
                var centre = NormToLocal(new Vector2(light.X, light.Y));
                var color = selected ? UiTokens.AccentBright : UiTokens.Alpha(light.ToColor(), 0.45f);
                _overlay.DrawArc(centre, light.Radius * scale, 0f, Mathf.Tau, 64, color, selected ? 1.5f : 1f, true);
            }
        }
    }
}
