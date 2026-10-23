using System;
using System.Collections.Generic;

namespace Cthangover.Tools.Services
{
    /// <summary>
    /// Reading and changing one key of a <c>.properties</c> text the way
    /// <c>LocaleLoader</c> reads it: <c>key = value</c> per line, <c>#</c> comments,
    /// no escapes and no multi-line values (a line break in a value becomes a
    /// space). Everything else in the text — comments, order, other keys, the
    /// line endings — stays byte for byte, so an editor save shows up in a diff
    /// as the one changed line.
    /// </summary>
    public static class PropertiesText
    {
        /// <summary>The value of <paramref name="key"/>, or null when the text does not declare it (the last line wins, as in the loader).</summary>
        public static string Get(string text, string key)
        {
            string found = null;
            foreach (var line in Lines(text))
            {
                if (KeyOf(line) == key)
                    found = line.Substring(line.IndexOf('=') + 1).Trim();
            }
            return found;
        }

        /// <summary>All keys declared in the text.</summary>
        public static HashSet<string> Keys(string text)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in Lines(text))
            {
                var key = KeyOf(line);
                if (key != null)
                    keys.Add(key);
            }
            return keys;
        }

        /// <summary>
        /// The text with <paramref name="key"/> set to <paramref name="value"/>: every
        /// line of the key is rewritten in place, or a <c>key = value</c> line is
        /// appended (after a line break the text may lack). Line endings follow the text.
        /// </summary>
        public static string Set(string text, string key, string value)
        {
            text ??= string.Empty;
            value = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            var newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var lines = text.Split('\n');
            var replaced = false;
            for (var i = 0; i < lines.Length; i++)
            {
                var hasCr = lines[i].EndsWith("\r");
                if (KeyOf(hasCr ? lines[i][..^1] : lines[i]) != key)
                    continue;
                lines[i] = $"{key} = {value}" + (hasCr ? "\r" : string.Empty);
                replaced = true;
            }
            if (replaced)
                return string.Join("\n", lines);
            var head = text.Length == 0 || text.EndsWith("\n") ? text : text + newline;
            return head + $"{key} = {value}" + newline;
        }

        private static IEnumerable<string> Lines(string text) => (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');

        private static string KeyOf(string rawLine)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
                return null;
            var eq = line.IndexOf('=');
            return eq <= 0 ? null : line.Substring(0, eq).Trim();
        }
    }
}
