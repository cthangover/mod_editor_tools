using System;
using System.Collections.Generic;
using Cthangover.Core.UI.Theming;
using Godot;

namespace Cthangover.Tools.Common
{
    /// <summary>
    /// Search field + list used wherever an editor picks from a long catalogue
    /// (backgrounds, icons, files, definitions). Items are identified by a
    /// stable id, not by row index: filtering reorders and hides rows, so the
    /// list keeps the id↔row mapping itself and raises <see cref="Chosen"/> /
    /// <see cref="Activated"/> with the id. <see cref="Select"/> is silent so
    /// windows can mirror their model into the list without feedback loops.
    /// </summary>
    public partial class FilteredList : VBoxContainer
    {
        // Scroll bar, item padding and a little text room on the right.
        private const int IconBoxMargin = 40;

        private readonly LineEdit _search;
        private readonly ItemList _list;
        private readonly List<(string Id, string Text, Texture2D Icon)> _items = new();
        private readonly List<string> _visibleIds = new();
        private string _selectedId;
        private bool _silent;
        private int _fitSide;

        /// <summary>Raised when the user selects a row (click or keyboard).</summary>
        public event Action<string> Chosen;

        /// <summary>Raised on double-click / Enter.</summary>
        public event Action<string> Activated;

        /// <summary>Currently selected id, or <c>null</c>.</summary>
        public string SelectedId => _selectedId;

        /// <summary>Creates the list; <paramref name="minHeight"/> is the list part's minimum height.</summary>
        public FilteredList(float minHeight = 140f, string placeholderKey = "tools/common/search")
        {
            AddThemeConstantOverride("separation", UiTokens.Space1);

            _search = new LineEdit
            {
                PlaceholderText = TranslationServer.Translate(placeholderKey),
                ClearButtonEnabled = true
            };
            _search.TextChanged += _ => Rebuild();
            AddChild(_search);

            _list = new ItemList
            {
                CustomMinimumSize = new Vector2(0, minHeight),
                SizeFlagsVertical = SizeFlags.ExpandFill,
                AllowReselect = true
            };
            _list.ItemSelected += OnItemSelected;
            _list.ItemActivated += index => Activated?.Invoke(IdAt((int)index));
            AddChild(_list);
        }

        /// <summary>Turns rows into an icon grid of <paramref name="iconSize"/> px thumbnails.</summary>
        public void UseIconGrid(int iconSize)
        {
            _list.FixedIconSize = new Vector2I(iconSize, iconSize);
            _list.IconMode = ItemList.IconModeEnum.Top;
            _list.MaxColumns = 0;
            _list.SameColumnWidth = true;
            _list.FixedColumnWidth = iconSize + 24;
        }

        /// <summary>
        /// Scales row icons to fit a box at most <paramref name="maxSide"/> px high and never wider than the
        /// list (minus room for the scroll bar): pictures of any size — a 1024×1920 portrait as well as a
        /// 32 px icon — keep their aspect and the row stays inside the panel. The box follows the list
        /// width, so narrowing the panel shrinks the pictures further.
        /// </summary>
        public void FitIcons(int maxSide)
        {
            _fitSide = maxSide;
            _list.Resized += UpdateIconBox;
            UpdateIconBox();
        }

        /// <summary>Replaces all items; keeps the current selection when its id is still present.</summary>
        public void SetItems(IEnumerable<(string Id, string Text, Texture2D Icon)> items)
        {
            _items.Clear();
            _items.AddRange(items);
            Rebuild();
        }

        /// <summary>Replaces all items with text-only rows where text equals id.</summary>
        public void SetItems(IEnumerable<string> ids)
        {
            _items.Clear();
            foreach (var id in ids)
                _items.Add((id, id, null));
            Rebuild();
        }

        /// <summary>Selects the row with <paramref name="id"/> (null clears) without raising events.</summary>
        public void Select(string id)
        {
            _selectedId = id;
            _silent = true;
            _list.DeselectAll();
            var index = _visibleIds.IndexOf(id);
            if (index >= 0)
            {
                _list.Select(index);
                _list.EnsureCurrentIsVisible();
            }
            _silent = false;
        }

        /// <summary>Number of items before filtering.</summary>
        public int Count => _items.Count;

        private void Rebuild()
        {
            var filter = _search.Text.Trim();
            _silent = true;
            _list.Clear();
            _visibleIds.Clear();

            foreach (var item in _items)
            {
                if (filter.Length > 0 && item.Text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
                    && item.Id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var index = item.Icon != null ? _list.AddItem(item.Text, item.Icon) : _list.AddItem(item.Text);
                _list.SetItemTooltip(index, item.Id);
                _visibleIds.Add(item.Id);
            }

            _silent = false;
            Select(_selectedId);
        }

        private void OnItemSelected(long index)
        {
            if (_silent)
                return;

            _selectedId = IdAt((int)index);
            Chosen?.Invoke(_selectedId);
        }

        private void UpdateIconBox()
        {
            var width = (int)_list.Size.X - IconBoxMargin;
            _list.FixedIconSize = new Vector2I(Math.Clamp(width, 16, _fitSide), _fitSide);
        }

        private string IdAt(int index) => index >= 0 && index < _visibleIds.Count ? _visibleIds[index] : null;
    }
}
