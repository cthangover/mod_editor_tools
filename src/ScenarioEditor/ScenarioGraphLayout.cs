using System;
using System.Collections.Generic;
using System.Linq;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Core.UI.Tool.ScenarioEditor
{
    /// <summary>
    /// Layered left-to-right layout of the scene graph: a scene's column is
    /// its distance (in scene switches) from the start scenes — the
    /// <c>start_scene</c> node, or every scene nothing switches to when there
    /// is none — so the story reads from left to right. Scenes no start
    /// reaches are stacked in one extra column on the right. Inside a column
    /// scenes are sorted by name, which keeps the picture stable between
    /// rebuilds.
    /// </summary>
    public static class ScenarioGraphLayout
    {
        private const float ColumnWidth = 300f;
        private const float RowHeight = 130f;
        private const float Margin = 40f;

        /// <summary>Canvas position per lower-cased scene name.</summary>
        public static Dictionary<string, Vector2> Compute(SceneGraphData data)
        {
            var names = data.Nodes.Select(n => n.SceneName.ToLowerInvariant()).Distinct().ToList();
            var outgoing = names.ToDictionary(n => n, _ => new List<string>());
            var incoming = new HashSet<string>();
            foreach (var edge in data.Edges)
            {
                var from = edge.FromScene.ToLowerInvariant();
                var to = edge.ToScene.ToLowerInvariant();
                if (!outgoing.ContainsKey(from) || !outgoing.ContainsKey(to) || from == to)
                    continue;
                outgoing[from].Add(to);
                incoming.Add(to);
            }

            var starts = data.Nodes.Where(n => n.IsStartScene).Select(n => n.SceneName.ToLowerInvariant()).ToList();
            if (starts.Count == 0)
                starts = names.Where(n => !incoming.Contains(n)).ToList();

            var depth = new Dictionary<string, int>();
            var queue = new Queue<string>();
            foreach (var start in starts)
            {
                depth[start] = 0;
                queue.Enqueue(start);
            }
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var next in outgoing[current])
                {
                    if (depth.ContainsKey(next))
                        continue;
                    depth[next] = depth[current] + 1;
                    queue.Enqueue(next);
                }
            }

            var unreachableColumn = depth.Count > 0 ? depth.Values.Max() + 1 : 0;
            var result = new Dictionary<string, Vector2>();
            foreach (var column in names.GroupBy(n => depth.TryGetValue(n, out var d) ? d : unreachableColumn).OrderBy(g => g.Key))
            {
                var row = 0;
                foreach (var name in column.OrderBy(n => n, StringComparer.Ordinal))
                    result[name] = new Vector2(Margin + column.Key * ColumnWidth, Margin + row++ * RowHeight);
            }
            return result;
        }
    }
}
