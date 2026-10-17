using System;
using System.Collections.Generic;
using System.Text.Json;
using Cthangover.Core.Mods.Resolvers;
using Cthangover.Core.UI.Theming;
using Cthangover.Core.Utils;
using Cthangover.Mods.Time;
using Godot;

namespace Cthangover.Core.UI.Tool.LightEditor
{
    /// <summary>
    /// Data of the light editor: the static lights of one scenario and their
    /// place in the scenario text. Static lights live in a <c>light_set</c>
    /// line (<c>light_set [{"x":…,"y":…,"radius":…,"influence":…,"color":…}]</c>)
    /// that <c>UiLightController.SetStaticLights</c> reads at runtime: x/y are
    /// fractions of the viewport, radius is in viewport pixels at the game
    /// resolution. The controller parses that line, edits the list and writes
    /// the line back (or inserts one right after the scenario header), and
    /// feeds the <c>timed_sprite</c> shader of the preview the same way the
    /// game does, so the preview shows the real night lighting.
    /// </summary>
    public class LightEditorController
    {
        /// <summary>Static light slots the shader has (slot 0 is the dynamic light).</summary>
        public const int MaxLights = 10;

        /// <summary>Viewport width the stored radii refer to.</summary>
        public const float GameWidth = 1920f;

        private const string Command = "light_set";
        private const int ShaderSlots = 11;
        private static readonly Vector2 Offscreen = new(-1000f, -1000f);

        /// <summary>The edited lights, in file order.</summary>
        public List<LightDef> Lights { get; private set; } = new();

        /// <summary>Index of the selected light, or -1.</summary>
        public int SelectedIndex { get; private set; } = -1;

        /// <summary>The selected light, or <c>null</c>.</summary>
        public LightDef Selected => SelectedIndex >= 0 && SelectedIndex < Lights.Count ? Lights[SelectedIndex] : null;

        /// <summary>Selects by index; out of range clears the selection.</summary>
        public void Select(int index) => SelectedIndex = index >= 0 && index < Lights.Count ? index : -1;

        /// <summary>Adds a warm light at the given normalised point and selects it; null when all slots are used.</summary>
        public LightDef Add(Vector2 position)
        {
            if (Lights.Count >= MaxLights)
                return null;

            var light = new LightDef { X = position.X, Y = position.Y, Radius = 400f, Influence = 1f, ColorHex = "#ffcc66" };
            Lights.Add(light);
            SelectedIndex = Lights.Count - 1;
            return light;
        }

        /// <summary>Removes the selected light.</summary>
        public void RemoveSelected()
        {
            if (Selected == null)
                return;
            Lights.RemoveAt(SelectedIndex);
            SelectedIndex = Math.Min(SelectedIndex, Lights.Count - 1);
        }

        /// <summary>Compact JSON array exactly as written into the <c>light_set</c> line.</summary>
        public string ToJson() => JsonSerializer.Serialize(Lights, JsonOptionsProvider.WriteCompactOptions);

        /// <summary>The scenario line for the current lights.</summary>
        public string ToScenarioLine() => Command + " " + ToJson();

        /// <summary>
        /// Loads the lights of the first <c>light_set</c> line of a scenario.
        /// Returns <c>false</c> when the scenario has none (the list is then
        /// empty and saving inserts a new line). Throws on malformed JSON.
        /// </summary>
        public bool LoadFromScenario(string text)
        {
            Lights = new List<LightDef>();
            SelectedIndex = -1;

            var index = FindLine(text, out var lines);
            if (index < 0)
                return false;

            var json = ArgumentOf(lines[index]);
            Lights = JsonSerializer.Deserialize<List<LightDef>>(json, JsonOptionsProvider.ReadOptions) ?? new List<LightDef>();
            SelectedIndex = Lights.Count > 0 ? 0 : -1;
            return true;
        }

        /// <summary>
        /// Returns the scenario text with its <c>light_set</c> line replaced by
        /// the current lights, or — when it has none — with a new line inserted
        /// right after the <c>---</c> header separator (at the top otherwise).
        /// </summary>
        public string WriteToScenario(string text)
        {
            var index = FindLine(text, out var lines);
            var newline = text.Contains("\r\n") ? "\r\n" : "\n";
            if (index >= 0)
            {
                var indent = lines[index].Substring(0, lines[index].Length - lines[index].TrimStart().Length);
                lines[index] = indent + ToScenarioLine();
            }
            else
            {
                var separator = lines.FindIndex(l => l.Trim() == "---");
                lines.Insert(separator + 1, ToScenarioLine());
            }

            return string.Join(newline, lines);
        }

        /// <summary>True when the scenario text contains a <c>light_set</c> line.</summary>
        public static bool HasLights(string text) => FindLine(text, out _) >= 0;

        private static int FindLine(string text, out List<string> lines)
        {
            lines = new List<string>((text ?? string.Empty).Replace("\r\n", "\n").Split('\n'));
            return lines.FindIndex(l => l.TrimStart().StartsWith(Command + " ", StringComparison.Ordinal));
        }

        /// <summary>Argument of a <c>light_set</c> line; the old quoted form (<c>"[{\"x\":…}]"</c>) is unescaped.</summary>
        private static string ArgumentOf(string line)
        {
            var arg = line.Trim().Substring(Command.Length).Trim();
            if (arg.Length >= 2 && arg[0] == '"' && arg[arg.Length - 1] == '"')
                arg = arg.Substring(1, arg.Length - 2).Replace("\\\"", "\"");
            return arg;
        }

        // ── shader preview ─────────────────────────────────────────

        /// <summary>
        /// Material of the preview: the game's <c>timed_sprite</c> shader at
        /// night weights. <c>null</c> when the shader is not installed.
        /// </summary>
        public static ShaderMaterial CreatePreviewMaterial()
        {
            var shader = Shaders.Resolve("timed_sprite");
            if (shader == null)
                return null;

            var material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter("time_weights", new Vector4(0, 0, 0, 1));
            material.SetShaderParameter("use_time", true);
            return material;
        }

        /// <summary>Binds the background and its optional depth/albedo masks to the preview material.</summary>
        public static void SetPreviewTextures(ShaderMaterial material, Texture2D depth, Texture2D albedo)
        {
            if (material == null)
                return;
            material.SetShaderParameter("depth_mask", depth);
            material.SetShaderParameter("albedo_map", albedo);
        }

        /// <summary>
        /// Uploads the lights into the shader uniforms. <paramref name="toCanvas"/>
        /// maps a normalised position into the preview's canvas coordinates
        /// (the space of the shader's <c>world_pos</c>); <paramref name="radiusScale"/>
        /// shrinks game-resolution radii to the preview size.
        /// </summary>
        public void UpdatePreview(ShaderMaterial material, Func<Vector2, Vector2> toCanvas, float radiusScale)
        {
            if (material == null)
                return;

            var positions = new Godot.Collections.Array<Vector2>();
            var colors = new Godot.Collections.Array<Color>();
            var radii = new Godot.Collections.Array<float>();
            var influence = new Godot.Collections.Array<float>();

            // Slot 0 is the dynamic (cursor) light at runtime; keep it off.
            positions.Add(Offscreen);
            colors.Add(UiTokens.White);
            radii.Add(0f);
            influence.Add(0f);

            var count = Math.Min(Lights.Count, MaxLights);
            for (var i = 0; i < ShaderSlots - 1; i++)
            {
                var on = i < count;
                positions.Add(on ? toCanvas(new Vector2(Lights[i].X, Lights[i].Y)) : Offscreen);
                colors.Add(on ? Lights[i].ToColor() : UiTokens.White);
                radii.Add(on ? Lights[i].Radius : 0f);
                influence.Add(on ? Lights[i].Influence : 0f);
            }

            material.SetShaderParameter("light_count", count + 1);
            material.SetShaderParameter("light_positions", positions);
            material.SetShaderParameter("light_colors", colors);
            material.SetShaderParameter("light_radii", radii);
            material.SetShaderParameter("light_influence", influence);
            material.SetShaderParameter("light_radius_scale", radiusScale);
        }
    }
}
