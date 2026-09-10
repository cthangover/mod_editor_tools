using System.Globalization;
using Godot;

namespace Cthangover.Core.UI.Tool.SceneBuilder
{
    /// <summary>
    /// Serializes editor-side <see cref="Variant"/> values into .tscn literal syntax
    /// accepted by the runtime parser (<c>TscnPropertyParser</c>): floats always carry
    /// a decimal point, strings are quoted with Godot escape sequences, composite
    /// types use the <c>Type(a, b, ...)</c> constructor form. Returns null for values
    /// the text format cannot represent (non-finite floats, unsupported variants) so
    /// callers can skip the write-back instead of corrupting the scene file.
    /// </summary>
    public static class TscnValueFormatter
    {
        /// <summary>Formats a Variant as a .tscn property value; null when not representable.</summary>
        public static string Format(Variant value)
        {
            switch (value.VariantType)
            {
                case Variant.Type.Nil:
                    return "null";
                case Variant.Type.Bool:
                    return value.AsBool() ? "true" : "false";
                case Variant.Type.Int:
                    return value.AsInt64().ToString(CultureInfo.InvariantCulture);
                case Variant.Type.Float:
                    return FormatFloat(value.AsDouble());
                case Variant.Type.String:
                    return Quote(value.AsString());
                case Variant.Type.Vector2:
                {
                    var v = value.AsVector2();
                    var x = FormatFloat(v.X);
                    var y = FormatFloat(v.Y);
                    return x == null || y == null ? null : $"Vector2({x}, {y})";
                }
                case Variant.Type.Color:
                {
                    var c = value.AsColor();
                    var r = FormatFloat(c.R);
                    var g = FormatFloat(c.G);
                    var b = FormatFloat(c.B);
                    var a = FormatFloat(c.A);
                    return r == null || g == null || b == null || a == null ? null : $"Color({r}, {g}, {b}, {a})";
                }
                default:
                    return null;
            }
        }

        /// <summary>
        /// Formats a float in invariant culture with a guaranteed decimal point
        /// ("1" → "1.0"). Values pass through float precision first because every
        /// numeric property edited here originates from Godot float properties —
        /// that yields the short canonical form ("0.35" instead of the double
        /// expansion "0.34999999403953552"). Null for NaN/Infinity.
        /// </summary>
        public static string FormatFloat(double v)
        {
            var f = (float)v;
            if (!float.IsFinite(f))
                return null;

            var s = f.ToString("R", CultureInfo.InvariantCulture);
            if (!s.Contains('.') && !s.Contains('e') && !s.Contains('E'))
                s += ".0";
            return s;
        }

        private static string Quote(string s)
        {
            var escaped = s
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
            return "\"" + escaped + "\"";
        }
    }
}
