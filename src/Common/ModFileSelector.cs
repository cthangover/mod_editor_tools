using System;
using System.Collections.Generic;
using System.Linq;
using Cthangover.Core.UI.Theming;
using Cthangover.Mods.Interface.Theming;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Tools.Common
{
    /// <summary>
    /// Toolbar group "mod ▾ file ▾ ＋" for editors that own one kind of file
    /// inside a mod folder (<c>map/*.json</c>, <c>interactives/*.json</c>).
    /// Only writable (folder-backed) mods are offered, and mods that already
    /// contain such files come first, so the author lands on real data instead
    /// of an arbitrary engine mod with nothing to edit.
    /// <para/>
    /// The selector never loads anything itself: it raises
    /// <see cref="FileRequested"/> and the window decides (usually after an
    /// "unsaved changes" confirmation). When the window declines, it calls
    /// <see cref="Show"/> with the file still open to roll the dropdowns back.
    /// A <c>null</c> path means "this mod has no such file yet".
    /// </summary>
    public partial class ModFileSelector : HBoxContainer
    {
        private readonly string _directory;
        private readonly string _extension;
        private readonly OptionButton _modSelect;
        private readonly OptionButton _fileSelect;
        private readonly List<string> _mods = new();
        private readonly List<string> _files = new();
        private bool _silent;

        /// <summary>Raised with (modId, mod-relative path or null) when the author picks or creates a file.</summary>
        public event Action<string, string> FileRequested;

        /// <summary>Mod shown in the selector.</summary>
        public string CurrentMod => _modSelect.Selected >= 0 && _modSelect.Selected < _mods.Count ? _mods[_modSelect.Selected] : null;

        /// <summary>File shown in the selector, or <c>null</c>.</summary>
        public string CurrentPath => _fileSelect.Selected >= 0 && _fileSelect.Selected < _files.Count ? _files[_fileSelect.Selected] : null;

        /// <summary>Creates the selector for files in <paramref name="directory"/> with <paramref name="extension"/>.</summary>
        public ModFileSelector(string directory, string extension)
        {
            _directory = directory;
            _extension = extension;
            AddThemeConstantOverride("separation", UiTokens.Space2);

            _modSelect = new OptionButton { CustomMinimumSize = new Vector2(170, 0), TooltipText = ToolUi.Tr("tools/common/mod_tip") };
            _modSelect.ItemSelected += _ => OnModPicked();
            AddChild(_modSelect);

            _fileSelect = new OptionButton { CustomMinimumSize = new Vector2(220, 0), TooltipText = ToolUi.Tr("tools/common/file_tip") };
            _fileSelect.ItemSelected += _ => OnFilePicked();
            AddChild(_fileSelect);

            var newBtn = UiKit.IconButton("+", ToolUi.Tr("tools/common/new_file"));
            newBtn.Pressed += OnNewPressed;
            AddChild(newBtn);
        }

        /// <summary>Re-reads mods and files and requests the first file of the first mod that has any.</summary>
        public void Refresh()
        {
            var writable = ModFileService.ListWritableMods();
            _mods.Clear();
            _mods.AddRange(writable.Where(HasFiles));
            _mods.AddRange(writable.Where(m => !HasFiles(m)));

            _silent = true;
            _modSelect.Clear();
            foreach (var mod in _mods)
                _modSelect.AddItem(HasFiles(mod) ? mod : mod + "  ·");
            _silent = false;

            if (_mods.Count == 0)
            {
                FileRequested?.Invoke(null, null);
                return;
            }

            _modSelect.Select(0);
            FillFiles(_mods[0]);
            FileRequested?.Invoke(_mods[0], CurrentPath);
        }

        /// <summary>Silently shows <paramref name="modId"/>/<paramref name="path"/> (a not-yet-saved path is added).</summary>
        public void Show(string modId, string path)
        {
            _silent = true;
            var modIndex = _mods.IndexOf(modId);
            if (modIndex >= 0)
            {
                _modSelect.Select(modIndex);
                FillFiles(modId);
                if (path != null && !_files.Contains(path))
                {
                    _files.Add(path);
                    _fileSelect.AddItem(ShortName(path));
                }
                _fileSelect.Select(path != null ? _files.IndexOf(path) : -1);
            }
            _silent = false;
        }

        private bool HasFiles(string modId) => ModFileService.ListFiles(modId, _directory, _extension).Count > 0;

        private void FillFiles(string modId)
        {
            _files.Clear();
            _files.AddRange(ModFileService.ListFiles(modId, _directory, _extension));
            var silent = _silent;
            _silent = true;
            _fileSelect.Clear();
            foreach (var file in _files)
                _fileSelect.AddItem(ShortName(file));
            _fileSelect.Disabled = _files.Count == 0;
            if (_files.Count > 0)
                _fileSelect.Select(0);
            _silent = silent;
        }

        private string ShortName(string path)
        {
            var prefix = _directory + "/";
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? path.Substring(prefix.Length) : path;
        }

        private void OnModPicked()
        {
            if (_silent)
                return;
            var mod = CurrentMod;
            FillFiles(mod);
            FileRequested?.Invoke(mod, CurrentPath);
        }

        private void OnFilePicked()
        {
            if (!_silent)
                FileRequested?.Invoke(CurrentMod, CurrentPath);
        }

        private void OnNewPressed()
        {
            if (CurrentMod == null)
                return;

            var dialog = new ConfirmationDialog { Title = ToolUi.Tr("tools/common/new_file") };
            var column = new VBoxContainer();
            column.AddChild(UiKit.Hint(string.Format(ToolUi.Tr("tools/common/new_file_hint"), CurrentMod, _directory, _extension)));
            var nameEdit = new LineEdit { PlaceholderText = "new_file", CustomMinimumSize = new Vector2(320, 0) };
            column.AddChild(nameEdit);
            dialog.AddChild(column);
            dialog.RegisterTextEnter(nameEdit);
            dialog.Confirmed += () =>
            {
                var name = SanitizeName(nameEdit.Text);
                dialog.QueueFree();
                if (name.Length == 0)
                    return;
                FileRequested?.Invoke(CurrentMod, _directory + "/" + name + _extension);
            };
            dialog.Canceled += () => dialog.QueueFree();
            AddChild(dialog);
            dialog.PopupCentered();
            nameEdit.GrabFocus();
        }

        private string SanitizeName(string raw)
        {
            var name = (raw ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
            if (name.EndsWith(_extension, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - _extension.Length);
            var chars = name.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '/').ToArray();
            return new string(chars);
        }
    }
}
