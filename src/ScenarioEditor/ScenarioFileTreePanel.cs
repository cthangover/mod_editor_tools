using System;
using System.Collections.Generic;
using Cthangover.Core.UI.Theming;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Core.UI.Tool.ScenarioEditor
{
    public partial class ScenarioFileTreePanel : VBoxContainer
    {
        private Tree _fileTree;
        private readonly Dictionary<string, string> _fileItemToKey = new();
        private readonly Dictionary<string, TreeItem> _keyToTreeItem = new();
        private bool _isProgrammaticSelection;

        public event Action<string> FileSelected;

        public ScenarioFileTreePanel()
        {
            _fileTree = GetNode<Tree>("FileTree");
            _fileTree.CellSelected += OnCellSelected;

            Populate();
        }

        private void Populate()
        {
            _fileTree.Clear();
            _fileItemToKey.Clear();
            _keyToTreeItem.Clear();

            var root = _fileTree.CreateItem();
            var mods = ScenarioFileService.ScanScenarioFiles();

            foreach (var mod in mods)
            {
                var modItem = _fileTree.CreateItem(root);
                modItem.SetText(0, $"[{mod.ModId}]");
                modItem.SetSelectable(0, false);
                modItem.SetCustomColor(0, UiTokens.Info);

                foreach (var group in mod.Groups)
                {
                    TreeItem parentItem;
                    if (string.IsNullOrEmpty(group.DirectoryPath))
                    {
                        parentItem = modItem;
                    }
                    else
                    {
                        parentItem = _fileTree.CreateItem(modItem);
                        parentItem.SetText(0, group.DirectoryPath + "/");
                        parentItem.SetSelectable(0, false);
                    }

                    foreach (var file in group.Files)
                    {
                        var fileItem = _fileTree.CreateItem(parentItem);
                        fileItem.SetText(0, file.Name);
                        fileItem.SetMetadata(0, Variant.From("scenario"));
                        _fileItemToKey[GetItemKey(fileItem)] = file.Key;
                        _keyToTreeItem[file.Key] = fileItem;
                    }
                }
            }
        }

        private static string GetItemKey(TreeItem item)
        {
            return item.GetInstanceId().ToString();
        }

        public void SelectFile(string key)
        {
            if (!_keyToTreeItem.TryGetValue(key, out var item))
                return;

            _isProgrammaticSelection = true;
            item.Select(0);
            _isProgrammaticSelection = false;

            FileSelected?.Invoke(key);
        }

        private void OnCellSelected()
        {
            if (_isProgrammaticSelection)
                return;

            var item = _fileTree.GetSelected();
            if (item == null)
                return;

            var meta = item.GetMetadata(0);
            if (meta.VariantType != Variant.Type.String || meta.AsString() != "scenario")
                return;

            var itemKey = GetItemKey(item);
            if (!_fileItemToKey.TryGetValue(itemKey, out var fileKey))
                return;

            FileSelected?.Invoke(fileKey);
        }
    }
}
