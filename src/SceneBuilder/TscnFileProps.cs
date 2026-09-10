using System.Collections.Generic;
using Cthangover.Core.Scenes.TscnParsing;

namespace Cthangover.Core.UI.Tool.SceneBuilder
{
    /// <summary>
    /// Snapshot of the raw node properties currently stored in a .tscn source
    /// text: node address → property key → unparsed literal. The inspector uses
    /// it to show what the FILE contains rather than post-layout live values —
    /// after Godot lays out a control its position/size reflect computed layout,
    /// which previously made anchored nodes look like they had none. Rebuild via
    /// <see cref="Build"/> whenever the source text changes. Section addressing
    /// matches <see cref="TscnTextSurgery"/> through <see cref="TscnNodeAddressing"/>.
    /// </summary>
    public class TscnFileProps
    {
        private readonly Dictionary<string, Dictionary<string, string>> _nodes = new();

        /// <summary>Parses tscn source into the snapshot; empty snapshot for null/empty text.</summary>
        public static TscnFileProps Build(string text)
        {
            var result = new TscnFileProps();
            if (string.IsNullOrEmpty(text))
                return result;

            Dictionary<string, string> current = null;
            foreach (var token in new TscnLexer().Tokenize(text))
            {
                if (token.Type == TscnTokenType.SectionHeader)
                {
                    current = null;
                    if (token.SectionName == "node")
                    {
                        var path = TscnNodeAddressing.ComputePath(token.SectionAttrs);
                        if (path != null && !result._nodes.ContainsKey(path))
                        {
                            current = new Dictionary<string, string>();
                            result._nodes[path] = current;
                        }
                    }
                }
                else if (token.Type == TscnTokenType.Property && current != null)
                {
                    if (!current.ContainsKey(token.PropertyKey))
                        current[token.PropertyKey] = token.PropertyValue;
                }
            }

            return result;
        }

        /// <summary>Raw property literals of one node; null when the section is absent from the file.</summary>
        public IReadOnlyDictionary<string, string> GetNodeProperties(string nodePath)
        {
            return _nodes.TryGetValue(nodePath ?? "", out var props) ? props : null;
        }
    }
}
