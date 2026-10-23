using System;
using System.Linq;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Tools.Common;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Tools.EntityEditor
{
    /// <summary>
    /// Grid of every picture of a file group with a search, for a picture field
    /// of an entity form: a double click or OK picks one. The pictures are read
    /// when the dialog opens, so a file just dropped into a mod folder shows up
    /// (after the game re-reads its files: the editor's Save does that).
    /// </summary>
    public partial class PicturePickerDialog : AcceptDialog
    {
        private readonly FilteredList list;

        /// <summary>A picture was picked: its id in the group.</summary>
        public event Action<string> Picked;

        /// <summary>Builds the dialog for the pictures of <paramref name="group"/>.</summary>
        public PicturePickerDialog(string group, string current)
        {
            Theme = ToolTheme.Get();
            Title = string.Format(ToolUi.Tr("tools/entity_editor/picture_pick_title"), group);
            MinSize = new Vector2I(640, 520);
            list = new FilteredList(420f);
            list.UseIconGrid(64);
            list.SetItems(ModResourceService.GetGroupImageIds(group)
                .Select(id => (id, id.Substring(id.LastIndexOf('/') + 1), ModResourceService.LoadGroupImage(group, id))));
            list.Select(current);
            list.Activated += OnActivated;
            AddChild(list);
            Confirmed += OnConfirmed;
            CloseRequested += QueueFree;
            Canceled += QueueFree;
        }

        private void OnActivated(string id)
        {
            if (id == null)
                return;
            Picked?.Invoke(id);
            Hide();
            QueueFree();
        }

        private void OnConfirmed()
        {
            if (list.SelectedId != null)
                Picked?.Invoke(list.SelectedId);
            QueueFree();
        }
    }
}
