using System.Collections.Generic;
using Godot;

namespace Cthangover.Core.UI.Tool.SceneBuilder
{
    /// <summary>
    /// Curated property lists shown by the scene builder inspector, mirroring the
    /// Godot editor's most-used entries. Deliberately hardcoded instead of derived
    /// from <c>ClassDB</c>: the runtime tscn builder ignores theme overrides and
    /// metadata, so a fixed allow-list guarantees every visible property is both
    /// live-applied via <c>Set()</c> and faithfully written back to the file.
    /// Two shapes: a free Control gets the layout/rect/visibility/misc set; a
    /// Control managed by a Container ignores anchors/offsets entirely (the parent
    /// recomputes them every frame), so it gets container-relevant fields instead —
    /// otherwise the inspector would show editable-looking values that do nothing,
    /// which is exactly how anchored scenes looked "wrong" before file-truth init.
    /// </summary>
    public static class InspectorPropertyCatalog
    {
        /// <summary>Builds the ordered (categoryKey, properties) groups for the given node.</summary>
        public static List<(string CategoryKey, List<InspectableProperty> Properties)> Build(Node node, bool insideContainer)
        {
            var result = new List<(string, List<InspectableProperty>)>();

            if (node is Control)
            {
                if (insideContainer)
                {
                    result.Add(("tools/scene_builder/inspector_cat_container", BuildContainer()));
                }
                else
                {
                    result.Add(("tools/scene_builder/inspector_cat_layout", BuildLayout()));
                    result.Add(("tools/scene_builder/inspector_cat_rect", BuildRect()));
                }
                result.Add(("tools/scene_builder/inspector_cat_visibility", BuildControlVisibility()));
                result.Add(("tools/scene_builder/inspector_cat_misc", BuildMisc()));
            }
            else if (node is CanvasItem)
            {
                result.Add(("tools/scene_builder/inspector_cat_visibility", BuildCanvasItemVisibility()));
            }

            return result;
        }

        private static List<InspectableProperty> BuildContainer()
        {
            return new List<InspectableProperty>
            {
                Int("size_flags_horizontal", 0, 15),
                Int("size_flags_vertical", 0, 15),
                Vector2("custom_minimum_size", 0d, 16384d, 1d)
            };
        }

        private static List<InspectableProperty> BuildLayout()
        {
            return new List<InspectableProperty>
            {
                Enum("layout_mode", new long[] { 0, 1, 2, 3 },
                    new[] { "Position", "Anchors", "Container", "Uncontrolled" }),
                Float("anchor_left", -8d, 8d, 0.01d),
                Float("anchor_top", -8d, 8d, 0.01d),
                Float("anchor_right", -8d, 8d, 0.01d),
                Float("anchor_bottom", -8d, 8d, 0.01d),
                Enum("grow_horizontal", new long[] { 0, 1, 2 }, new[] { "Begin", "End", "Both" }),
                Enum("grow_vertical", new long[] { 0, 1, 2 }, new[] { "Begin", "End", "Both" }),
                Float("offset_left", -100000d, 100000d, 1d),
                Float("offset_top", -100000d, 100000d, 1d),
                Float("offset_right", -100000d, 100000d, 1d),
                Float("offset_bottom", -100000d, 100000d, 1d)
            };
        }

        private static List<InspectableProperty> BuildRect()
        {
            return new List<InspectableProperty>
            {
                Vector2("custom_minimum_size", 0d, 16384d, 1d),
                Vector2("position", -100000d, 100000d, 1d),
                Vector2("size", 0d, 100000d, 1d),
                Float("rotation", -36000d, 36000d, 1d),
                Vector2("scale", -64d, 64d, 0.05d),
                Vector2("pivot_offset", -100000d, 100000d, 1d)
            };
        }

        private static List<InspectableProperty> BuildControlVisibility()
        {
            return new List<InspectableProperty>
            {
                Bool("visible"),
                Color("modulate"),
                Color("self_modulate"),
                Bool("clip_contents")
            };
        }

        private static List<InspectableProperty> BuildCanvasItemVisibility()
        {
            return new List<InspectableProperty>
            {
                Bool("visible"),
                Color("modulate"),
                Color("self_modulate")
            };
        }

        private static List<InspectableProperty> BuildMisc()
        {
            return new List<InspectableProperty>
            {
                Enum("mouse_filter", new long[] { 0, 1, 2 }, new[] { "Stop", "Pass", "Ignore" }),
                Enum("focus_mode", new long[] { 0, 1, 2 }, new[] { "None", "Click", "All" }),
                Text("tooltip_text")
            };
        }

        private static InspectableProperty Float(string key, double min, double max, double step)
        {
            return new InspectableProperty { Key = key, Kind = InspectorPropertyKind.Numeric, Min = min, Max = max, Step = step };
        }

        private static InspectableProperty Int(string key, int min, int max)
        {
            return new InspectableProperty { Key = key, Kind = InspectorPropertyKind.Numeric, IsInteger = true, Min = min, Max = max, Step = 1d };
        }

        private static InspectableProperty Vector2(string key, double min, double max, double step)
        {
            return new InspectableProperty { Key = key, Kind = InspectorPropertyKind.Vector2, Min = min, Max = max, Step = step };
        }

        private static InspectableProperty Bool(string key)
        {
            return new InspectableProperty { Key = key, Kind = InspectorPropertyKind.Bool };
        }

        private static InspectableProperty Text(string key)
        {
            return new InspectableProperty { Key = key, Kind = InspectorPropertyKind.String };
        }

        private static InspectableProperty Color(string key)
        {
            return new InspectableProperty { Key = key, Kind = InspectorPropertyKind.Color };
        }

        private static InspectableProperty Enum(string key, long[] values, string[] labels)
        {
            return new InspectableProperty { Key = key, Kind = InspectorPropertyKind.Enum, IsInteger = true, EnumValues = values, EnumLabels = labels };
        }
    }
}
