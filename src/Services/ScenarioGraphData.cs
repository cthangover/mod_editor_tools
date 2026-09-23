using System.Collections.Generic;

namespace Cthangover.Tools.Services
{
    public sealed class ScenarioGraphLink
    {
        public string Name { get; set; }
        public int Priority { get; set; }
        public string Key { get; set; }
    }

    public sealed class SceneGraphNode
    {
        public string SceneName { get; set; }
        public string ModId { get; set; }
        public int ScenarioCount { get; set; }
        public List<string> Backgrounds { get; set; } = new();
        public bool IsStartScene { get; set; }
        public List<ScenarioGraphLink> Scenarios { get; set; } = new();
        public bool HasErrors { get; set; }
    }

    public sealed class SceneGraphEdge
    {
        public string FromScene { get; set; }
        public string ToScene { get; set; }
    }

    public sealed class SceneGraphData
    {
        public List<SceneGraphNode> Nodes { get; set; } = new();
        public List<SceneGraphEdge> Edges { get; set; } = new();
    }
}
