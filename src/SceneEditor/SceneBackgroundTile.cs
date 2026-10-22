using System;
using Cthangover.Core.UI.Theming;
using Cthangover.Mods.Interface.Theming;
using Cthangover.Tools.Common;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Tools.SceneEditor
{
    /// <summary>
    /// One background of the edited scene in <see cref="SceneEditorWindow"/>: a thumbnail
    /// (click shows it in the preview), the background id and the mod whose scene file
    /// adds it, and a remove button only for backgrounds of the file being edited —
    /// another mod's background can only be hidden by editing that mod.
    /// </summary>
    public partial class SceneBackgroundTile : PanelContainer
    {
        private readonly string _background;
        private readonly Action<string> _picked;
        private readonly Action<string> _removed;

        /// <summary>Builds the tile; <paramref name="removable"/> shows the ✕ button, <paramref name="selected"/> the accent frame.</summary>
        public SceneBackgroundTile(string background, string modId, bool removable, bool selected,
            Action<string> picked, Action<string> removed)
        {
            _background = background;
            _picked = picked;
            _removed = removed;
            AddThemeStyleboxOverride("panel", UiKit.Frame(UiTokens.RadiusSm, UiTokens.Surface1,
                selected ? UiTokens.Accent : UiTokens.Line));

            var column = new VBoxContainer();
            column.AddThemeConstantOverride("separation", UiTokens.Space1);
            AddChild(column);

            var thumb = new TextureButton
            {
                TextureNormal = ModResourceService.LoadBackgroundTexture(background),
                IgnoreTextureSize = true,
                StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = SceneEditorWindow.ThumbSize,
                TooltipText = background
            };
            thumb.Pressed += OnPicked;
            column.AddChild(thumb);

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", UiTokens.Space1);
            column.AddChild(row);
            var caption = ToolUi.Hint(background + " · " + modId);
            caption.TooltipText = background + " (" + modId + ")";
            caption.MouseFilter = MouseFilterEnum.Pass;
            caption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            caption.CustomMinimumSize = new Vector2(SceneEditorWindow.ThumbSize.X - 32, 0);
            caption.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            row.AddChild(caption);
            if (removable)
            {
                var remove = UiKit.IconButton("✕", ToolUi.Tr("tools/scene_editor/remove_background"));
                remove.Pressed += OnRemoved;
                row.AddChild(remove);
            }
        }

        private void OnPicked() => _picked?.Invoke(_background);

        private void OnRemoved() => _removed?.Invoke(_background);
    }
}
