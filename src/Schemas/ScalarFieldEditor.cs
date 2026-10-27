namespace Cthangover.Tools.Schemas
{
    using Cthangover.Mods.Schemas;
    using SchemaFieldDefinition = Cthangover.Mods.Schemas.FieldSchema;
    using SchemaJsonValues = Cthangover.Mods.Schemas.SchemaJson;
    using System;
    using System.Text.Json.Nodes;
    using Godot;

    /// <summary>A data editor form field with a single value: a <see cref="SchemaFieldControl"/> plus the JSON conversion.</summary>
    public class ScalarFieldEditor : ISchemaFieldEditor
    {
        private readonly SchemaFieldControl control;

        /// <summary>Adds the row for <paramref name="field"/> to <paramref name="parent"/>.</summary>
        public ScalarFieldEditor(Container parent, SchemaFieldDefinition field, Action changed)
        {
            control = new SchemaFieldControl(parent, field, changed);
        }

        /// <inheritdoc />
        public SchemaFieldDefinition Field => control.Field;

        /// <inheritdoc />
        public void Load(JsonNode node) => control.Value = SchemaJsonValues.ToText(node);

        /// <inheritdoc />
        public JsonNode Save() => SchemaJsonValues.ToNode(control.Field, control.Value);
    }
}
