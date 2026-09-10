using System.Collections.Generic;
using Cthangover.Core.Scenes.TscnParsing;

namespace Cthangover.Core.UI.Tool.SceneBuilder
{
    /// <summary>
    /// Targeted line edits of raw .tscn source without a full re-serialization:
    /// the file is tokenized with <see cref="TscnLexer"/> (which reports 1-based
    /// line numbers), the affected <c>[node]</c> sections are located by their
    /// Godot address path (via <see cref="TscnNodeAddressing"/>, shared with
    /// <see cref="TscnFileProps"/>), and only the involved lines are touched.
    /// Everything outside them — comments, blank lines, formatting — survives.
    /// Beyond property upsert/removal it can insert a whole new node section
    /// right after its parent's block (parent-before-child order is what the
    /// runtime builder relies on) and delete a node subtree together with any
    /// dangling [connection] entries. Newly inserted properties follow Godot's
    /// layout convention (<c>layout_mode</c> → anchors → grow → offsets → rest)
    /// so the builder applies them in a valid sequence.
    /// </summary>
    public static class TscnTextSurgery
    {
        /// <summary>
        /// Inserts or replaces one property line inside the given node's section.
        /// nodePath uses .tscn addressing relative to the scene root ("", "Panel", "Panel/Btn");
        /// "" addresses the root node section itself. Returns false when the section
        /// does not exist in this file (e.g. override sections for instanced sub-scenes).
        /// </summary>
        public static bool TryUpsertProperty(string text, string nodePath, string key, string tscnValue, out string newText)
        {
            newText = text;
            if (text == null || string.IsNullOrEmpty(key))
                return false;

            var lines = SplitLines(text, out var crlf);
            var tokens = new TscnLexer().Tokenize(text);
            var blocks = CollectBlocks(tokens);

            if (!TryFindBlock(blocks, nodePath, out var block) || !block.IsNode)
                return false;

            var replaceLine = -1;
            var insertAfterLine = block.Start;
            var group = GroupOrder(key);

            foreach (var token in tokens)
            {
                if (token.Type != TscnTokenType.Property || token.LineNumber <= block.Start || token.LineNumber > block.End)
                    continue;
                if (token.PropertyKey == key)
                {
                    replaceLine = token.LineNumber;
                    break;
                }
                if (GroupOrder(token.PropertyKey) <= group)
                    insertAfterLine = token.LineNumber;
            }

            var newLine = key + " = " + tscnValue;
            if (replaceLine >= 0)
                lines[replaceLine - 1] = newLine;
            else
                lines.Insert(insertAfterLine, newLine);

            newText = JoinLines(lines, crlf);
            return true;
        }

        /// <summary>
        /// Removes one property line from the given node's section. Returns false
        /// when the section or the property line is not present.
        /// </summary>
        public static bool TryRemoveProperty(string text, string nodePath, string key, out string newText)
        {
            newText = text;
            if (text == null || string.IsNullOrEmpty(key))
                return false;

            var lines = SplitLines(text, out var crlf);
            var tokens = new TscnLexer().Tokenize(text);
            var blocks = CollectBlocks(tokens);

            if (!TryFindBlock(blocks, nodePath, out var block) || !block.IsNode)
                return false;

            foreach (var token in tokens)
            {
                if (token.Type != TscnTokenType.Property || token.LineNumber <= block.Start || token.LineNumber > block.End)
                    continue;
                if (token.PropertyKey != key)
                    continue;
                lines.RemoveAt(token.LineNumber - 1);
                newText = JoinLines(lines, crlf);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Inserts a complete <c>[node]</c> section directly after its parent's
        /// block so the runtime builder meets the parent first. parentPath "" adds
        /// a child of the scene root (parent="."). Returns false when the parent
        /// section is absent from this file or name/type contain illegal characters.
        /// </summary>
        public static bool TryInsertNodeSection(string text, string parentPath, string name, string type,
            List<KeyValuePair<string, string>> properties, out string newText)
        {
            newText = text;
            if (text == null || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type))
                return false;
            if (name.Contains('"') || name.Contains('[') || type.Contains('"') || type.Contains('['))
                return false;

            var lines = SplitLines(text, out var crlf);
            var blocks = CollectBlocks(new TscnLexer().Tokenize(text));

            if (!TryFindBlock(blocks, parentPath ?? "", out var parentBlock) || !parentBlock.IsNode)
                return false;

            var parentAttr = string.IsNullOrEmpty(parentPath) ? "." : parentPath;
            var newBlock = new List<string> { "[node name=\"" + name + "\" type=\"" + type + "\" parent=\"" + parentAttr + "\"]" };
            if (properties != null)
            {
                foreach (var pair in properties)
                    newBlock.Add(pair.Key + " = " + pair.Value);
            }
            newBlock.Add("");

            // 1-based End line occupies index End-1; inserting at index End places
            // the new block between the parent's last line and whatever follows.
            lines.InsertRange(parentBlock.End, newBlock);

            newText = JoinLines(lines, crlf);
            return true;
        }

        /// <summary>
        /// Deletes the node's own section, every descendant section (path prefix)
        /// and [connection] headers pointing into the subtree. Trailing blank lines
        /// of each removed range are swallowed to avoid blank-line buildup.
        /// Returns false when no matching section exists.
        /// </summary>
        public static bool TryRemoveNodeSubtree(string text, string nodePath, out string newText)
        {
            newText = text;
            if (text == null || string.IsNullOrEmpty(nodePath))
                return false;

            var lines = SplitLines(text, out var crlf);
            var blocks = CollectBlocks(new TscnLexer().Tokenize(text));
            var subtreePrefix = nodePath + "/";

            // (startIndex, endExclusiveIndex) removal ranges in descending order later.
            var ranges = new List<(int StartIdx, int EndIdx)>();
            var matched = false;

            foreach (var block in blocks)
            {
                var inSubtree = false;
                if (block.IsNode && block.Path != null &&
                    (block.Path == nodePath || block.Path.StartsWith(subtreePrefix, System.StringComparison.Ordinal)))
                    inSubtree = true;
                if (block.IsConnection && block.From != null &&
                    (block.From == nodePath || block.From.StartsWith(subtreePrefix, System.StringComparison.Ordinal)))
                    inSubtree = true;

                if (!inSubtree)
                    continue;

                matched = true;
                var startIdx = block.Start - 1;
                var endIdx = block.End; // 0-based index of the first line AFTER the block
                while (endIdx < lines.Count && lines[endIdx].Trim().Length == 0)
                    endIdx++;
                ranges.Add((startIdx, endIdx));
            }

            if (!matched)
                return false;

            ranges.Sort((a, b) => b.StartIdx.CompareTo(a.StartIdx));
            foreach (var range in ranges)
                lines.RemoveRange(range.StartIdx, range.EndIdx - range.StartIdx);

            newText = JoinLines(lines, crlf);
            return true;
        }

        private struct SectionInfo
        {
            public bool IsNode;
            public bool IsConnection;
            public string Path;
            public string From;
            public int Start;
            public int End;
        }

        /// <summary>Walks tokens once and produces every section block with its inclusive [Start..End] 1-based line span.</summary>
        private static List<SectionInfo> CollectBlocks(List<TscnToken> tokens)
        {
            var result = new List<SectionInfo>();
            TscnToken currentHeader = null;
            var lastLine = -1;

            for (var i = 0; i <= tokens.Count; i++)
            {
                var token = i < tokens.Count ? tokens[i] : null;
                if (token == null || token.Type == TscnTokenType.SectionHeader)
                {
                    if (currentHeader != null)
                    {
                        var attrs = currentHeader.SectionAttrs ?? new Dictionary<string, string>();
                        var info = new SectionInfo { Start = currentHeader.LineNumber, End = lastLine };
                        if (currentHeader.SectionName == "node")
                        {
                            info.IsNode = true;
                            info.Path = TscnNodeAddressing.ComputePath(attrs);
                        }
                        else if (currentHeader.SectionName == "connection")
                        {
                            info.IsConnection = true;
                            attrs.TryGetValue("from", out info.From);
                        }
                        result.Add(info);
                    }
                    if (token == null)
                        break;
                    currentHeader = token;
                    lastLine = token.LineNumber;
                }
                else
                {
                    lastLine = token.LineNumber;
                }
            }

            return result;
        }

        private static bool TryFindBlock(List<SectionInfo> blocks, string nodePath, out SectionInfo block)
        {
            block = default;
            foreach (var candidate in blocks)
            {
                if (candidate.Path != null && candidate.Path == nodePath)
                {
                    block = candidate;
                    return true;
                }
            }
            return false;
        }

        private static int GroupOrder(string key)
        {
            if (key == "layout_mode")
                return 0;
            if (key == "anchors_preset")
                return 1;
            if (key.StartsWith("anchor_", System.StringComparison.Ordinal))
                return 2;
            if (key.StartsWith("grow_", System.StringComparison.Ordinal))
                return 3;
            if (key.StartsWith("offset_", System.StringComparison.Ordinal))
                return 4;
            return 5;
        }

        private static List<string> SplitLines(string text, out bool crlf)
        {
            crlf = text.Contains("\r\n");
            var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            return new List<string>(normalized.Split('\n'));
        }

        private static string JoinLines(List<string> lines, bool crlf)
        {
            var separator = crlf ? "\r\n" : "\n";
            return string.Join(separator, lines);
        }
    }
}
