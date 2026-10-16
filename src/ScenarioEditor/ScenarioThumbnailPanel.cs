using System.Linq;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Core.UI.Tool.ScenarioEditor
{
    /// <summary>Row of background thumbnails referenced by the scenario in the active text tab.</summary>
    public partial class ScenarioThumbnailPanel : ScrollContainer
    {
        private HBoxContainer _thumbContainer;

        public ScenarioThumbnailPanel()
        {
            _thumbContainer = GetNode<HBoxContainer>("ThumbContainer");
        }

        /// <summary>Loads the background thumbnails referenced by the given scenario text (missing ones render as placeholders).</summary>
        public void Load(string text)
        {
            Clear();

            if (string.IsNullOrEmpty(text))
                return;

            var refs = ScenarioFileService.ExtractReferences(text);

            if (refs.BackgroundRefs.Count == 0)
            {
                _thumbContainer.AddChild(new Label
                {
                    Text = TranslationServer.Translate("tools/scenario_editor/no_backgrounds")
                });
                return;
            }

            foreach (var id in refs.BackgroundRefs.Distinct())
            {
                var tex = ModResourceService.LoadBackgroundTexture(id);

                if (tex != null)
                {
                    _thumbContainer.AddChild(new TextureRect
                    {
                        Texture = tex,
                        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                        CustomMinimumSize = new Vector2(120, 90),
                        TooltipText = id
                    });
                }
                else
                {
                    _thumbContainer.AddChild(new Label
                    {
                        Text = $"[? {id}]",
                        TooltipText = $"Not found: {id}"
                    });
                }
            }
        }

        /// <summary>Removes every thumbnail from the row.</summary>
        public new void Clear()
        {
            foreach (var c in _thumbContainer.GetChildren())
                c.QueueFree();
        }
    }
}
