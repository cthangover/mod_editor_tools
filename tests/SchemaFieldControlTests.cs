namespace Cthangover.Tools.Tests
{
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Mods.Schemas;
    using Cthangover.Tools.Schemas;
    using Godot;

    /// <summary>JSON schema fields preserve structured values through their highlighted multiline editor.</summary>
    public class SchemaFieldControlTests
    {
        [ModTest("schema/json_field_roundtrip")]
        static void JsonFieldRoundtrip(ModTestContext ctx)
        {
            var parent = new VBoxContainer();
            ((SceneTree)Engine.GetMainLoop()).Root.AddChild(parent);
            try
            {
                var field = new SchemaFieldControl(parent, new FieldSchema { Key = "Extra", Type = "json" }, null);
                ModAssert.True(field.Editor is CodeEdit, "JSON field has a multiline code editor");
                var code = (CodeEdit)field.Editor;
                ModAssert.True(code.SyntaxHighlighter != null, "JSON field has syntax highlighting");
                var original = new JsonObject
                {
                    ["text"] = "Unicode text: \u041f\u0440\u0438\u0432\u0435\u0442",
                    ["active"] = true,
                    ["nested"] = new JsonArray(1, null, 3),
                };
                field.Value = JsonSerializer.Serialize(original, new JsonSerializerOptions { WriteIndented = true });
                var restored = JsonNode.Parse(field.Value);
                ModAssert.Equal(original["text"].GetValue<string>(), restored["text"].GetValue<string>(), "text survives the field");
                ModAssert.True(restored["active"].GetValue<bool>(), "boolean survives the field");
                ModAssert.Equal(3, restored["nested"].AsArray().Count, "array survives the field");
                ModAssert.True(restored["nested"][1] == null, "null survives the field");
                code.Text = "{\n  \"number\": 42\n}";
                ModAssert.Equal(42, JsonNode.Parse(field.Value)["number"].GetValue<int>(), "edited JSON is returned by the form");
            }
            finally { parent.Free(); }
        }
    }
}
