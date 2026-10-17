using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Cthangover.Core.Interactive;
using Cthangover.Core.Mods;
using Cthangover.Core.Utils;
using Godot;

namespace Cthangover.Tools.InteractiveEditor
{
    /// <summary>
    /// Data model of the interactive editor: every definition of one
    /// <c>interactives/*.json</c> file plus the selection. Definitions are
    /// edited in place (the same objects the game deserialises), so the file
    /// round-trips without a parallel field-by-field copy that could forget a
    /// property. <see cref="Ensure"/> fills the optional sub-objects the editor
    /// needs (hit area, highlight, actions) before a definition is shown.
    /// </summary>
    public class InteractiveEditorController
    {
        /// <summary>Collider kinds understood by <c>InteractiveObject</c>.</summary>
        public static readonly string[] HitTypes = { "rect", "circle", "polygon" };

        /// <summary>Render layers understood by the interactive layer host.</summary>
        public static readonly string[] Layers = { "foreground", "background", "ui" };

        /// <summary>Definitions of the open file, in file order.</summary>
        public List<InteractiveDefinition> Items { get; private set; } = new();

        /// <summary>Index of the selected definition, or -1.</summary>
        public int SelectedIndex { get; private set; } = -1;

        /// <summary>The selected definition, or <c>null</c>.</summary>
        public InteractiveDefinition Selected => SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;

        /// <summary>Replaces the items with the contents of a <c>{"Items": [...]}</c> file. Throws on malformed JSON.</summary>
        public void FromJson(string json)
        {
            var data = string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<FileData<InteractiveDefinition>>(json, JsonOptionsProvider.ReadOptions);
            Items = data?.Items ?? new List<InteractiveDefinition>();
            foreach (var item in Items)
                Ensure(item);
            SelectedIndex = Items.Count > 0 ? 0 : -1;
        }

        /// <summary>Serialises all items into the standard envelope (indented, nulls omitted).</summary>
        public string ToJson()
        {
            var data = new FileData<InteractiveDefinition> { Items = Items };
            return JsonSerializer.Serialize(data, JsonOptionsProvider.WriteIndentedIgnoreNullOptions);
        }

        /// <summary>Drops all items.</summary>
        public void Clear()
        {
            Items = new List<InteractiveDefinition>();
            SelectedIndex = -1;
        }

        /// <summary>Selects by index; out of range clears the selection.</summary>
        public void Select(int index)
        {
            SelectedIndex = index >= 0 && index < Items.Count ? index : -1;
        }

        /// <summary>Appends a new clickable rect in the middle of the background and selects it.</summary>
        public InteractiveDefinition Add()
        {
            var def = new InteractiveDefinition
            {
                ID = MakeUniqueId("interactive"),
                Cursor = "PointingHand",
                HitArea = new HitAreaDefinition { Type = "rect", X = 0.45f, Y = 0.45f, Width = 0.1f, Height = 0.1f }
            };
            Ensure(def);
            Items.Add(def);
            SelectedIndex = Items.Count - 1;
            return def;
        }

        /// <summary>Appends a deep copy of the selected definition with a fresh ID, slightly offset.</summary>
        public InteractiveDefinition DuplicateSelected()
        {
            var source = Selected;
            if (source == null)
                return null;

            var json = JsonSerializer.Serialize(source, JsonOptionsProvider.WriteCompactOptions);
            var copy = JsonSerializer.Deserialize<InteractiveDefinition>(json, JsonOptionsProvider.ReadOptions);
            copy.ID = MakeUniqueId(source.ID);
            Ensure(copy);
            Offset(copy.HitArea, new Vector2(0.02f, 0.02f));
            Items.Add(copy);
            SelectedIndex = Items.Count - 1;
            return copy;
        }

        /// <summary>Removes the selected definition and keeps a sensible selection.</summary>
        public void RemoveSelected()
        {
            if (Selected == null)
                return;

            Items.RemoveAt(SelectedIndex);
            SelectedIndex = Items.Count == 0 ? -1 : Math.Min(SelectedIndex, Items.Count - 1);
        }

        /// <summary>
        /// Changes the collider kind of a definition, converting the current
        /// shape so it stays roughly where it was (rect ↔ circle by bounds,
        /// polygon from the bounds' corners).
        /// </summary>
        public static void SetHitType(InteractiveDefinition def, string type)
        {
            var hit = def.HitArea;
            if (hit.Type == type)
                return;

            var bounds = Bounds(hit);
            hit.Type = type;
            switch (type)
            {
                case "circle":
                    hit.X = bounds.GetCenter().X;
                    hit.Y = bounds.GetCenter().Y;
                    hit.Radius = Mathf.Max(0.01f, Mathf.Min(bounds.Size.X, bounds.Size.Y) / 2f);
                    break;
                case "polygon":
                    hit.Vertices = new[]
                    {
                        bounds.Position,
                        new Vector2(bounds.End.X, bounds.Position.Y),
                        bounds.End,
                        new Vector2(bounds.Position.X, bounds.End.Y)
                    };
                    break;
                default:
                    hit.X = bounds.Position.X;
                    hit.Y = bounds.Position.Y;
                    hit.Width = bounds.Size.X;
                    hit.Height = bounds.Size.Y;
                    break;
            }
        }

        /// <summary>
        /// Normalised bounding box of a hit area (circle radius taken as a
        /// fraction of both axes — an approximation good enough for
        /// conversions and duplication offsets).
        /// </summary>
        public static Rect2 Bounds(HitAreaDefinition hit)
        {
            switch (hit?.Type)
            {
                case "circle":
                    return new Rect2(hit.X - hit.Radius, hit.Y - hit.Radius, hit.Radius * 2f, hit.Radius * 2f);
                case "polygon" when hit.Vertices is { Length: > 0 }:
                    var min = hit.Vertices[0];
                    var max = hit.Vertices[0];
                    foreach (var v in hit.Vertices)
                    {
                        min = new Vector2(Mathf.Min(min.X, v.X), Mathf.Min(min.Y, v.Y));
                        max = new Vector2(Mathf.Max(max.X, v.X), Mathf.Max(max.Y, v.Y));
                    }
                    return new Rect2(min, max - min);
                case null:
                    return new Rect2(0.45f, 0.45f, 0.1f, 0.1f);
                default:
                    return new Rect2(hit.X, hit.Y, hit.Width, hit.Height);
            }
        }

        /// <summary>Makes sure the optional parts the editor binds to exist.</summary>
        public static void Ensure(InteractiveDefinition def)
        {
            def.HitArea ??= new HitAreaDefinition();
            def.HitArea.Type = HitTypes.Contains(def.HitArea.Type) ? def.HitArea.Type : "rect";
            def.Highlight ??= new HighlightDefinition();
            def.Actions ??= new InteractiveActionDefinition();
        }

        /// <summary>ID not used by another item: <c>base</c>, <c>base_2</c>, <c>base_3</c>…</summary>
        public string MakeUniqueId(string baseId)
        {
            var used = new HashSet<string>(Items.Select(i => i.ID ?? string.Empty), StringComparer.OrdinalIgnoreCase);
            if (!used.Contains(baseId))
                return baseId;

            var suffix = 2;
            while (used.Contains($"{baseId}_{suffix}"))
                suffix++;
            return $"{baseId}_{suffix}";
        }

        private static void Offset(HitAreaDefinition hit, Vector2 delta)
        {
            hit.X += delta.X;
            hit.Y += delta.Y;
            if (hit.Vertices != null)
                hit.Vertices = hit.Vertices.Select(v => v + delta).ToArray();
        }
    }
}
