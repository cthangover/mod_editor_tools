using System.Globalization;
using Godot;

namespace Cthangover.Core.UI.Tool.SceneBuilder
{
    /// <summary>
    /// Inverse of <see cref="TscnValueFormatter"/>: parses raw .tscn property
    /// literals (as stored in the file, e.g. "0.5", "Vector2(160, 50)",
    /// "\"Go\"") into display values for inspector widgets. Returns false for
    /// anything it cannot represent — object references like ExtResource("..."),
    /// sub-resources — so callers fall back to live-node values instead of
    /// showing garbage. Pure string math, safe outside the engine runtime.
    /// </summary>
    public static class TscnLiteralReader
    {
        /// <summary>Parses a bare .tscn float literal; constructors and references return false.</summary>
        public static bool TryReadFloat(string raw, out double value)
        {
            value = 0d;
            if (raw == null || raw.Contains('('))
                return false;
            return double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>Parses a bare .tscn integer literal; floats and constructors return false.</summary>
        public static bool TryReadLong(string raw, out long value)
        {
            value = 0;
            if (raw == null || raw.Contains('.') || raw.Contains('('))
                return false;
            return long.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>Parses a .tscn boolean literal (true/false).</summary>
        public static bool TryReadBool(string raw, out bool value)
        {
            var t = raw?.Trim();
            if (t == "true") { value = true; return true; }
            if (t == "false") { value = false; return true; }
            value = false;
            return false;
        }

        /// <summary>Reads a quoted .tscn string literal; null when the raw text is not a quoted string.</summary>
        public static string ReadString(string raw)
        {
            if (raw == null)
                return null;
            var t = raw.Trim();
            if (t.Length < 2 || t[0] != '"' || t[t.Length - 1] != '"')
                return null;

            var body = t.Substring(1, t.Length - 2);
            return body
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t")
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\");
        }

        /// <summary>Parses a <c>Vector2(x, y)</c> constructor literal.</summary>
        public static bool TryReadVector2(string raw, out Vector2 value)
        {
            value = Vector2.Zero;
            if (!TryReadConstructor(raw, "Vector2", out var parts) || parts.Length != 2)
                return false;
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                return false;
            value = new Vector2((float)x, (float)y);
            return true;
        }

        /// <summary>Parses a <c>Color(r, g, b[, a])</c> constructor literal.</summary>
        public static bool TryReadColor(string raw, out Color value)
        {
            value = Colors.White;
            if (!TryReadConstructor(raw, "Color", out var parts) || parts.Length is not (3 or 4))
                return false;
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var g) ||
                !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
                return false;
            var a = 1d;
            if (parts.Length == 4 && !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out a))
                return false;
            value = new Color((float)r, (float)g, (float)b, (float)a);
            return true;
        }

        private static bool TryReadConstructor(string raw, string typeName, out string[] args)
        {
            args = null;
            if (raw == null)
                return false;
            var t = raw.Trim();
            var prefix = typeName + "(";
            if (!t.StartsWith(prefix, System.StringComparison.Ordinal) || !t.EndsWith(")", System.StringComparison.Ordinal))
                return false;

            var body = t.Substring(prefix.Length, t.Length - prefix.Length - 1);
            args = body.Split(',');
            for (var i = 0; i < args.Length; i++)
                args[i] = args[i].Trim();
            return true;
        }
    }
}
