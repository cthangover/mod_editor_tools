using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cthangover.Tools.Services;
using Cthangover.Core.Utils;
using Godot;

namespace Cthangover.Core.UI.Tool.ScenarioEditor
{
    public sealed class TabData
    {
        public string FilePath;
        public string FileName;
        public string SavedText;
        public bool IsDirty;
    }

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
        public string CurrentAbsolutePath =>
            _activeTabIndex >= 0 && _activeTabIndex < _tabs.Count ? _tabs[_activeTabIndex].FilePath : null;
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

        public void OpenFile(string absolutePath)
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (_tabs[i].FilePath == absolutePath)
                {
                    SwitchToTextTab(i);
                    return;
                }
            }

            try
            {
                var text = ScenarioFileService.ReadAllText(absolutePath);
                var tab = new TabData
                {
                    FilePath = absolutePath,
                    FileName = Path.GetFileNameWithoutExtension(absolutePath),
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

        public void SetTextSilent(string text)
        {
            _suppressTextChanged = true;
            _textEdit.Text = text;
            _suppressTextChanged = false;
        }

        public void SetViewToGraph()
        {
            if (_tabs.Count > 0)
                return;

            _tabBar.CurrentTab = GraphTabIndex;
            _textEdit.Visible = false;
            _graphView.Visible = true;
            GraphTabSelected?.Invoke();
        }

        public void RefreshGraph()
        {
            if (_graphView.Visible)
            {
                var data = ScenarioFileService.BuildSceneGraph();
                _graphView.Populate(data);
            }
        }

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
