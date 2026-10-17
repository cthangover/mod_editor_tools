using System;
using System.Collections.Generic;
using Cthangover.Core.UI.Theming;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Core.UI.Tool.ScenarioEditor
{
    /// <summary>
    /// Left-hand tree of every mod's scenario files, grouped by mod and
    /// subdirectory, with a search field that keeps only matching files (and
    /// expands their groups). Raises <see cref="FileSelected"/> with the
    /// scenario key on picks.
    /// <para/>
    /// Built from <c>scenario_file_tree_panel.tscn</c>: the tscn builder creates
    /// the script node through its constructor before the child nodes exist,
    /// so children are looked up lazily (<see cref="Bind"/>) — not in the
    /// constructor, which is what used to crash the whole editor on open.
    /// </summary>
    public partial class ScenarioFileTreePanel : VBoxContainer
    {
        private Tree _fileTree;
        private LineEdit _search;
        private List<ModScenarioTree> _mods = new();
        private readonly Dictionary<TreeItem, string> _itemToKey = new();
        private readonly Dictionary<string, TreeItem> _keyToItem = new();
        private bool _isProgrammaticSelection;
        private string _selectedKey;

        public event Action<string> FileSelected;

        /// <summary>Binds child nodes and fills the tree once the panel enters the tree.</summary>
        public override void _Ready()
        {
            Reload();
        }

        /// <summary>Re-scans the mods' scenario files and rebuilds the tree.</summary>
        public void Reload()
        {
            Bind();
            _mods = ScenarioFileService.ScanScenarioFiles();
            Populate();
        }

        private void Bind()
        {
            if (_fileTree != null)
                return;

            _fileTree = GetNode<Tree>("FileTree");
            _fileTree.CellSelected += OnCellSelected;
            _search = GetNode<LineEdit>("Search");
            _search.PlaceholderText = TranslationServer.Translate("tools/scenario_editor/search_files");
            _search.TextChanged += _ => Populate();
        }

        private void Populate()
        {
            _fileTree.Clear();
            _itemToKey.Clear();
            _keyToItem.Clear();

            var filter = _search.Text.Trim();
            var root = _fileTree.CreateItem();

            foreach (var mod in _mods)
            {
                TreeItem modItem = null;
                foreach (var group in mod.Groups)
                {
                    TreeItem groupItem = null;
                    foreach (var file in group.Files)
                    {
                        if (filter.Length > 0 && file.RelativePath.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        if (modItem == null)
                        {
                            modItem = _fileTree.CreateItem(root);
                            modItem.SetText(0, mod.ModId);
                            modItem.SetSelectable(0, false);
                            modItem.SetCustomColor(0, UiTokens.AccentBright);
                        }

                        if (groupItem == null)
                        {
                            if (string.IsNullOrEmpty(group.DirectoryPath))
                            {
                                groupItem = modItem;
                            }
                            else
                            {
                                groupItem = _fileTree.CreateItem(modItem);
                                groupItem.SetText(0, group.DirectoryPath + "/");
                                groupItem.SetSelectable(0, false);
                                groupItem.SetCustomColor(0, UiTokens.Muted);
                                groupItem.Collapsed = filter.Length == 0;
                            }
                        }

                        var fileItem = _fileTree.CreateItem(groupItem);
                        fileItem.SetText(0, file.Name);
                        fileItem.SetTooltipText(0, ScenarioFileService.GetDisplayPath(file.ModId, file.RelativePath));
                        _itemToKey[fileItem] = file.Key;
                        _keyToItem[file.Key] = fileItem;
                    }
                }
            }

            if (_selectedKey != null && _keyToItem.TryGetValue(_selectedKey, out var selected))
                SelectSilently(selected);
        }

        /// <summary>Selects the tree row of a scenario key and raises <see cref="FileSelected"/>.</summary>
        public void SelectFile(string key)
        {
            Bind();
            _selectedKey = key;
            if (!_keyToItem.TryGetValue(key, out var item))
            {
                _search.Text = string.Empty;
                Populate();
                if (!_keyToItem.TryGetValue(key, out item))
                    return;
            }

            SelectSilently(item);
            FileSelected?.Invoke(key);
        }

        private void SelectSilently(TreeItem item)
        {
            for (var parent = item.GetParent(); parent != null; parent = parent.GetParent())
                parent.Collapsed = false;

            _isProgrammaticSelection = true;
            item.Select(0);
            _fileTree.ScrollToItem(item);
            _isProgrammaticSelection = false;
        }

        private void OnCellSelected()
        {
            if (_isProgrammaticSelection)
                return;

            var item = _fileTree.GetSelected();
            if (item == null || !_itemToKey.TryGetValue(item, out var key))
                return;

            _selectedKey = key;
            FileSelected?.Invoke(key);
        }
    }
}
