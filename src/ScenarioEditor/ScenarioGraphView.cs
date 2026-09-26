using System;
using System.Collections.Generic;
using System.Linq;
using Cthangover.Core.UI.Theming;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Core.UI.Tool.ScenarioEditor
{
    public partial class ScenarioGraphView : HSplitContainer
    {
        private GraphEdit _graph;
        private RichTextLabel _infoLabel;
        private Dictionary<string, GraphNode> _nodes;
        private Dictionary<string, SceneGraphNode> _lookup;

        public event Action<string> ScenarioFileRequested;

        public ScenarioGraphView()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            SizeFlagsVertical = SizeFlags.ExpandFill;

            _graph = new GraphEdit
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                RightDisconnects = false,
                ConnectionLinesThickness = 2f,
                ConnectionLinesAntialiased = true,
                MinimapEnabled = true,
                ShowGrid = true,
                SnappingEnabled = true,
                SnappingDistance = 20
            };
            _graph.ConnectionRequest += OnConnectionRequest;
            _graph.DisconnectionRequest += OnDisconnectionRequest;
            _graph.NodeSelected += OnNodeSelected;
            _graph.NodeDeselected += OnNodeDeselected;
            AddChild(_graph);

            var panel = new VBoxContainer { CustomMinimumSize = new Vector2(250, 0) };
            panel.SizeFlagsVertical = SizeFlags.ExpandFill;
            AddChild(panel);

            panel.AddChild(new Label
            {
                Text = TranslationServer.Translate("tools/scenario_editor/graph_select_node"),
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            });

            _infoLabel = new RichTextLabel
            {
                BbcodeEnabled = true,
                FitContent = true,
                ScrollFollowing = true,
                SizeFlagsVertical = SizeFlags.ExpandFill
            };
            _infoLabel.MetaClicked += OnLinkClicked;
            panel.AddChild(_infoLabel);
        }

        public void Populate(SceneGraphData data)
        {
            var savedPositions = SaveNodePositions();
            ClearGraph();

            _nodes = new Dictionary<string, GraphNode>();
            _lookup = new Dictionary<string, SceneGraphNode>();

            int x = 40, y = 40;
            foreach (var pos in savedPositions.Values)
            {
                int bottom = (int)pos.Y + 160 + 20;
                if (bottom > y)
                    y = bottom;
            }

            foreach (var nodeData in data.Nodes)
            {
                var key = nodeData.SceneName.ToLowerInvariant();
                if (_nodes.ContainsKey(key))
                    continue;

                var bgCount = nodeData.Backgrounds?.Count ?? 0;
                var firstBg = bgCount > 0 ? nodeData.Backgrounds[0] : "";

                var position = savedPositions.TryGetValue(key, out var saved)
                    ? saved
                    : new Vector2(x, y);

                var gn = new GraphNode
                {
                    Title = nodeData.SceneName,
                    Size = new Vector2(240, 90),
                    PositionOffset = position
                };

                gn.AddChild(new Label
                {
                    Text = $"Mod: {nodeData.ModId}\nScenarios: {nodeData.ScenarioCount}" +
                           (bgCount > 0 ? $"\nBg: {firstBg}" +
                           (bgCount > 1 ? $" +{bgCount - 1}" : "") : "")
                });

                var color = UiTokens.White;
                if (nodeData.IsStartScene)
                {
                    color = UiTokens.Success;
                    gn.AddThemeStyleboxOverride("titlebar", CreateTitlebarStyle(UiTokens.Alpha(UiTokens.Success, 0.3f)));
                }
                else if (nodeData.HasErrors)
                {
                    color = UiTokens.DangerText;
                    gn.AddThemeStyleboxOverride("titlebar", CreateTitlebarStyle(UiTokens.Alpha(UiTokens.Danger, 0.5f)));
                }
                gn.SetSlot(0, true, 0, UiTokens.White, true, 0, color);

                _graph.AddChild(gn);
                _nodes[key] = gn;
                _lookup[key] = nodeData;

                if (!savedPositions.ContainsKey(key))
                {
                    x += 280;
                    if (x > 1000) { x = 40; y += 160; }
                }
            }

            foreach (var edge in data.Edges)
            {
                var srcK = edge.FromScene.ToLowerInvariant();
                var dstK = edge.ToScene.ToLowerInvariant();
                if (!_nodes.ContainsKey(srcK) || !_nodes.ContainsKey(dstK))
                    continue;
                _graph.ConnectNode(_nodes[srcK].Name, 0, _nodes[dstK].Name, 0);
            }
        }

        private Dictionary<string, Vector2> SaveNodePositions()
        {
            var positions = new Dictionary<string, Vector2>();
            if (_nodes == null)
                return positions;
            foreach (var kv in _nodes)
                positions[kv.Key] = kv.Value.PositionOffset;
            return positions;
        }

        private static StyleBoxFlat CreateTitlebarStyle(Color bgColor)
        {
            var style = UiKit.Frame(UiTokens.RadiusMd, bgColor);
            style.CornerRadiusBottomLeft = 0;
            style.CornerRadiusBottomRight = 0;
            return style;
        }

        private void ClearGraph()
        {
            foreach (var c in _graph.GetChildren())
                if (c is GraphNode gn)
                    gn.QueueFree();
            _infoLabel.Text = "";
            _nodes?.Clear();
            _lookup?.Clear();
        }

        private void OnConnectionRequest(StringName fromNode, long fromPort, StringName toNode, long toPort) { }

        private void OnDisconnectionRequest(StringName fromNode, long fromPort, StringName toNode, long toPort) { }

        private void OnNodeSelected(Node n)
        {
            if (n is not GraphNode gn)
                return;
            var key = gn.Title.ToString().ToLowerInvariant();
            if (!_lookup.TryGetValue(key, out var nodeData))
                return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Scene: [b]{nodeData.SceneName}[/b]");
            sb.AppendLine($"Mod: {nodeData.ModId}");
            if (nodeData.Backgrounds.Count > 0)
                sb.AppendLine($"Backgrounds: {string.Join(", ", nodeData.Backgrounds)}");
            sb.AppendLine();
            sb.AppendLine($"Scenarios ({nodeData.Scenarios.Count}):");

            foreach (var sc in nodeData.Scenarios.OrderBy(s => s.Priority))
            {
                var url = Uri.EscapeDataString(sc.Key);
                sb.AppendLine($"  [{sc.Priority}] [url={url}]{sc.Name}[/url]");
            }

            _infoLabel.Text = sb.ToString();
        }

        private void OnNodeDeselected(Node n)
        {
            _infoLabel.Text = "";
        }

        private void OnLinkClicked(Variant meta)
        {
            var path = Uri.UnescapeDataString(meta.AsString());
            ScenarioFileRequested?.Invoke(path);
        }
    }
}
