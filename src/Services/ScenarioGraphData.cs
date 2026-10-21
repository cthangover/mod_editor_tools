using System.Collections.Generic;

namespace Cthangover.Tools.Services
{
    /// <summary>One scenario attached to a scene node (shown on the graph node and in its detail links).</summary>
    public sealed class ScenarioGraphLink
    {
        public string Name { get; set; }
        public int Priority { get; set; }
        public string Key { get; set; }
    }

    /// <summary>One scene of the cross-scenario graph: owning mod, background list, start-scene flag and its scenarios.</summary>
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

    /// <summary>Directed switch-target link between two scenes of the graph.</summary>
    public sealed class SceneGraphEdge
    {
        public string FromScene { get; set; }
        public string ToScene { get; set; }
    }

    /// <summary>Complete graph model consumed by <c>ScenarioGraphView</c>.</summary>
    public sealed class SceneGraphData
    {
        public List<SceneGraphNode> Nodes { get; set; } = new();
        public List<SceneGraphEdge> Edges { get; set; } = new();
    }
}
