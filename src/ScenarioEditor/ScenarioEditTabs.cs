using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cthangover.Tools.Services;
using Cthangover.Core.Utils;
using Godot;

namespace Cthangover.Core.UI.Tool.ScenarioEditor
{
    /// <summary>State of one open scenario file tab: identity, saved text and dirty flag.</summary>
    public sealed class TabData
    {
        public string Key;
        public string ModId;
        public string RelativePath;
        public string FileName;
        public string SavedText;
        public bool IsDirty;
    }

    /// <summary>
    /// Tabbed editor area of the scenario editor: a permanent graph tab plus
    /// one text tab per opened .scenario file, with dirty tracking and a
    /// monospace syntax-highlighted code editor.
    /// </summary>
    public partial class ScenarioEditTabs : VBoxContainer
    {
        private const int GraphTabIndex = 0;

        private TabBar _tabBar;
        private TextEdit _textEdit;
        private ScenarioGraphView _graphView;
        private Font _monospaceFont;

        private readonly List<TabData> _tabs = new();
        private int _activeTabIndex = -1;
        private bool _suppressTextChanged;

        public TextEdit CodeEditor => _textEdit;
        public string CurrentText => _textEdit.Text;
        public string CurrentKey =>
            _activeTabIndex >= 0 && _activeTabIndex < _tabs.Count ? _tabs[_activeTabIndex].Key : null;
        public string CurrentModId =>
            _activeTabIndex >= 0 && _activeTabIndex < _tabs.Count ? _tabs[_activeTabIndex].ModId : null;
        public string CurrentRelativePath =>
            _activeTabIndex >= 0 && _activeTabIndex < _tabs.Count ? _tabs[_activeTabIndex].RelativePath : null;
        public bool HasActiveFile => _activeTabIndex >= 0 && _activeTabIndex < _tabs.Count;
        public bool IsCurrentDirty =>
            _activeTabIndex >= 0 && _activeTabIndex < _tabs.Count && _tabs[_activeTabIndex].IsDirty;

        public event Action<string> TextTabSelected;
        public event Action GraphTabSelected;
        public event Action<bool> DirtyChanged;
        public event Action<string> GraphFileRequested;

        public ScenarioEditTabs(ScenarioGraphView graphView, Font monospaceFont)
        {
            _graphView = graphView;
            _monospaceFont = monospaceFont;

            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            SizeFlagsVertical = SizeFlags.ExpandFill;

            _tabBar = new TabBar();
            _tabBar.TabCloseDisplayPolicy = TabBar.CloseButtonDisplayPolicy.ShowActiveOnly;
            _tabBar.TabSelected += OnTabSelected;
            _tabBar.TabClosePressed += OnTabClosePressed;
            _tabBar.AddTab(TranslationServer.Translate("tools/scenario_editor/tab_graph"));
            AddChild(_tabBar);

            _textEdit = new TextEdit
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                Editable = true,
                SyntaxHighlighter = ScenarioSyntaxService.CreateHighlighter(),
                WrapMode = TextEdit.LineWrappingMode.None
            };
            _textEdit.AddThemeFontOverride("font", _monospaceFont);
            _textEdit.TextChanged += OnTextChanged;
            AddChild(_textEdit);

            _graphView.ScenarioFileRequested += OnGraphScenarioFileRequested;
            AddChild(_graphView);
        }

        /// <summary>Opens the scenario in a new tab, or just activates its existing tab.</summary>
        public void OpenFile(string key)
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (_tabs[i].Key == key)
                {
                    SwitchToTextTab(i);
                    return;
                }
            }

            if (!ScenarioFileService.TryParseKey(key, out var modId, out var relativePath))
                return;

            try
            {
                var text = ScenarioFileService.ReadAllText(modId, relativePath);
                if (text == null)
                {
                    GameLogger.Log("SCENARIO_EDITOR", $"Failed to read scenario '{key}'", LogLevel.Error);
                    return;
                }

                var tab = new TabData
                {
                    Key = key,
                    ModId = modId,
                    RelativePath = relativePath,
                    FileName = Path.GetFileNameWithoutExtension(relativePath),
                    SavedText = text,
                    IsDirty = false
                };

                _tabs.Add(tab);
                _tabBar.AddTab(tab.FileName);

                SwitchToTextTab(_tabs.Count - 1);
            }
            catch (Exception ex)
            {
                GameLogger.Log("SCENARIO_EDITOR", $"Failed to load file: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Marks the active tab clean, remembering the editor text as the saved baseline.</summary>
        public void SaveCurrentFile()
        {
            if (_activeTabIndex < 0 || _activeTabIndex >= _tabs.Count)
                return;

            var tab = _tabs[_activeTabIndex];
            tab.SavedText = _textEdit.Text;
            if (tab.IsDirty)
            {
                tab.IsDirty = false;
                UpdateTabTitle(_activeTabIndex);
                UpdateGlobalDirty();
            }
        }

        /// <summary>Replaces the editor text without firing the text-changed handler.</summary>
        public void SetTextSilent(string text)
        {
            _suppressTextChanged = true;
            _textEdit.Text = text;
            _suppressTextChanged = false;
        }

        /// <summary>Switches to the graph tab when no file tabs are open.</summary>
        public void SetViewToGraph()
        {
            if (_tabs.Count > 0)
                return;

            _tabBar.CurrentTab = GraphTabIndex;
            _textEdit.Visible = false;
            _graphView.Visible = true;
            GraphTabSelected?.Invoke();
        }

        /// <summary>Rebuilds the graph from the current scenario files, if the graph tab is visible.</summary>
        public void RefreshGraph()
        {
            if (_graphView.Visible)
            {
                var data = ScenarioFileService.BuildSceneGraph();
                _graphView.Populate(data);
            }
        }

        /// <summary>Returns false when any tab still has unsaved changes.</summary>
        public bool ConfirmCloseAll()
        {
            for (int i = _tabs.Count - 1; i >= 0; i--)
            {
                if (_tabs[i].IsDirty)
                    return false;
            }
            return true;
        }

        private void OnTabSelected(long index)
        {
            if (index == GraphTabIndex)
            {
                SaveActiveTextEditContent();
                _activeTabIndex = -1;
                _textEdit.Visible = false;
                _graphView.Visible = true;
                GraphTabSelected?.Invoke();
            }
            else
            {
                _graphView.Visible = false;
                _textEdit.Visible = true;
                SwitchToTextTab((int)index - 1);
            }
        }

        private void OnTabClosePressed(long index)
        {
            if (index == GraphTabIndex)
                return;
            CloseTextTab((int)index - 1);
        }

        private void SwitchToTextTab(int index)
        {
            if (index < 0 || index >= _tabs.Count || index == _activeTabIndex)
                return;

            if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
                _tabs[_activeTabIndex].SavedText = _textEdit.Text;

            _activeTabIndex = index;
            _tabBar.CurrentTab = index + 1;

            var tab = _tabs[index];

            SetTextSilent(tab.SavedText);

            TextTabSelected?.Invoke(tab.SavedText);
        }

        private void CloseTextTab(int index)
        {
            if (index < 0 || index >= _tabs.Count)
                return;

            if (_tabs[index].IsDirty)
            {
                var dialog = new ConfirmationDialog();
                dialog.Title = TranslationServer.Translate("tools/scenario_editor/unsaved_title");
                dialog.DialogText = TranslationServer.Translate("tools/scenario_editor/unsaved_switch_text");
                int capturedIndex = index;
                dialog.Confirmed += () =>
                {
                    dialog.QueueFree();
                    DoCloseTextTab(capturedIndex);
                };
                dialog.Canceled += () => dialog.QueueFree();
                AddChild(dialog);
                dialog.PopupCentered();
                return;
            }

            DoCloseTextTab(index);
        }

        private void DoCloseTextTab(int index)
        {
            _tabs.RemoveAt(index);
            _tabBar.RemoveTab(index + 1);

            if (_tabs.Count == 0)
            {
                _activeTabIndex = -1;
                SetTextSilent("");
                TextTabSelected?.Invoke("");
                UpdateGlobalDirty();
                return;
            }

            if (index == _activeTabIndex)
            {
                var newIndex = Math.Min(index, _tabs.Count - 1);
                _activeTabIndex = -1;
                SwitchToTextTab(newIndex);
            }
            else if (index < _activeTabIndex)
            {
                _activeTabIndex--;
                _tabBar.CurrentTab = _activeTabIndex + 1;
            }

            UpdateGlobalDirty();
        }

        private void OnTextChanged()
        {
            if (_suppressTextChanged || _activeTabIndex < 0 || _activeTabIndex >= _tabs.Count)
                return;

            var tab = _tabs[_activeTabIndex];
            if (!tab.IsDirty)
            {
                tab.IsDirty = true;
                UpdateTabTitle(_activeTabIndex);
                UpdateGlobalDirty();
            }
        }

        private void UpdateTabTitle(int index)
        {
            if (index < 0 || index >= _tabs.Count)
                return;
            var tab = _tabs[index];
            _tabBar.SetTabTitle(index + 1, tab.IsDirty ? tab.FileName + " *" : tab.FileName);
        }

        private void UpdateGlobalDirty()
        {
            bool anyDirty = _tabs.Any(t => t.IsDirty);
            DirtyChanged?.Invoke(anyDirty);
        }

        private void SaveActiveTextEditContent()
        {
            if (_activeTabIndex >= 0 && _activeTabIndex < _tabs.Count)
                _tabs[_activeTabIndex].SavedText = _textEdit.Text;
        }

        private void OnGraphScenarioFileRequested(string filePath)
        {
            GraphFileRequested?.Invoke(filePath);
        }
    }
}
