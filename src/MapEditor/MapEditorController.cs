using System;
using System.Collections.Generic;
using Cthangover.Core.UI.Tool;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Tools.MapEditor
{
    /// <summary>
    /// Data model for the map editor. Holds the zone list being edited, the
    /// current selection and all structural operations (add/remove zone,
    /// add/remove/move vertex, move marker and label, toggle label mode).
    /// Contains no UI and no runtime state: it edits raw authoring data that
    /// <see cref="MapFileService"/> serialises back into
    /// <c>mods/&lt;mod&gt;/map/*.json</c>, while the live game state belongs to
    /// <c>MapZoneService</c>.
    /// <para/>
    /// New zones get unique IDs automatically and a sensible default shape
    /// (a small square for regions, centre position for markers and labels),
    /// so an author can place an element first and refine it afterwards. All
    /// coordinates are normalised [0..1] and clamped by the canvas, not here.
    /// </summary>
    public class MapEditorController
    {
        private const float DefaultRegionHalfSize = 0.06f;
        private static readonly Vector2 DefaultCenter = new(0.5f, 0.5f);

        /// <summary>Zones currently being edited, in map file order.</summary>
        public List<MapZone> Zones { get; private set; } = new();

        /// <summary>Index of the selected zone, or -1 when nothing is selected.</summary>
        public int SelectedIndex { get; private set; } = -1;

        /// <summary>The selected zone, or <c>null</c> when the selection is empty.</summary>
        public MapZone Selected => SelectedIndex >= 0 && SelectedIndex < Zones.Count ? Zones[SelectedIndex] : null;

        /// <summary>Replaces all zones with the contents of a map file JSON envelope.</summary>
        public void FromJson(string json)
        {
            Zones = MapFileService.ParseZones(json);
            SelectedIndex = Zones.Count > 0 ? 0 : -1;
        }

        /// <summary>Serialises the current zone list into the map file JSON envelope.</summary>
        public string ToJson() => MapFileService.BuildJson(Zones);

        /// <summary>Drops all zones and the selection.</summary>
        public void Clear()
        {
            Zones.Clear();
            SelectedIndex = -1;
        }

        /// <summary>Creates a rectangular region around the map centre and selects it.</summary>
        public MapZone AddRegion()
        {
            var zone = new MapZone
            {
                ID = MakeUniqueId("region"),
                Title = string.Empty,
                Points = new List<Vector2>
                {
                    DefaultCenter + new Vector2(-DefaultRegionHalfSize, -DefaultRegionHalfSize),
                    DefaultCenter + new Vector2(DefaultRegionHalfSize, -DefaultRegionHalfSize),
                    DefaultCenter + new Vector2(DefaultRegionHalfSize, DefaultRegionHalfSize),
                    DefaultCenter + new Vector2(-DefaultRegionHalfSize, DefaultRegionHalfSize),
                },
            };

            return Append(zone);
        }

        /// <summary>
        /// Creates an icon marker at the map centre with no icon chosen yet and
        /// selects it. The author assigns <c>image</c> and <c>scene</c> in the
        /// sidebar.
        /// </summary>
        public MapZone AddMarker()
        {
            var zone = new MapZone
            {
                ID = MakeUniqueId("marker"),
                Title = string.Empty,
                ImagePos = DefaultCenter,
            };

            return Append(zone);
        }

        /// <summary>
        /// Creates a free-standing text label at the map centre and selects it.
        /// The label exists as soon as <c>label_pos</c> is set; <c>MapWidget</c>
        /// draws zones without an icon as inscriptions.
        /// </summary>
        public MapZone AddLabel()
        {
            var zone = new MapZone
            {
                ID = MakeUniqueId("label"),
                Title = string.Empty,
                LabelPos = DefaultCenter,
            };

            return Append(zone);
        }

        /// <summary>Removes the zone at the given index and adjusts the selection.</summary>
        public void RemoveAt(int index)
        {
            if (index < 0 || index >= Zones.Count)
                return;

            Zones.RemoveAt(index);

            if (Zones.Count == 0)
                SelectedIndex = -1;
            else if (SelectedIndex > index)
                SelectedIndex--;
            else if (SelectedIndex == index)
                SelectedIndex = Math.Min(index, Zones.Count - 1);
        }

        /// <summary>Selects the zone at the given index; out-of-range clears the selection.</summary>
        public void Select(int index)
        {
            SelectedIndex = index >= 0 && index < Zones.Count ? index : -1;
        }

        /// <summary>Appends a vertex to the selected region's polygon.</summary>
        public void AddVertex(Vector2 normalized)
        {
            var zone = Selected;
            if (zone == null)
                return;

            zone.Points ??= new List<Vector2>();
            zone.Points.Add(normalized);
        }

        /// <summary>Moves one vertex of the selected region.</summary>
        public void SetVertex(int index, Vector2 normalized)
        {
            var zone = Selected;
            if (zone?.Points == null || index < 0 || index >= zone.Points.Count)
                return;

            zone.Points[index] = normalized;
        }

        /// <summary>Removes one vertex of the selected region.</summary>
        public void RemoveVertex(int index)
        {
            var zone = Selected;
            if (zone?.Points == null || index < 0 || index >= zone.Points.Count)
                return;

            zone.Points.RemoveAt(index);
        }

        /// <summary>Moves the icon marker of the selected zone.</summary>
        public void SetImagePos(Vector2 normalized)
        {
            var zone = Selected;
            if (zone != null)
                zone.ImagePos = normalized;
        }

        /// <summary>Moves the text label of the selected zone.</summary>
        public void SetLabelPos(Vector2 normalized)
        {
            var zone = Selected;
            if (zone != null)
                zone.LabelPos = normalized;
        }

        /// <summary>
        /// Enables or disables the free-standing label of the selected zone.
        /// Enabling without a known position places it at the map centre.
        /// </summary>
        public void SetLabelEnabled(bool enabled)
        {
            var zone = Selected;
            if (zone == null)
                return;

            zone.LabelPos = enabled ? zone.LabelPos ?? DefaultCenter : null;
        }

        /// <summary>ID set of every zone in the list, for duplicate validation.</summary>
        public HashSet<string> UsedIds()
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var zone in Zones)
            {
                if (!string.IsNullOrEmpty(zone.ID))
                    ids.Add(zone.ID);
            }
            return ids;
        }

        /// <summary>
        /// Builds an ID that is not used yet by appending a numeric suffix
        /// (<c>region</c>, <c>region_2</c>, <c>region_3</c>, ...).
        /// </summary>
        public string MakeUniqueId(string baseId)
        {
            var used = UsedIds();
            if (!used.Contains(baseId))
                return baseId;

            var suffix = 2;
            while (used.Contains($"{baseId}_{suffix}"))
                suffix++;

            return $"{baseId}_{suffix}";
        }

        private MapZone Append(MapZone zone)
        {
            Zones.Add(zone);
            SelectedIndex = Zones.Count - 1;
            return zone;
        }
    }
}
