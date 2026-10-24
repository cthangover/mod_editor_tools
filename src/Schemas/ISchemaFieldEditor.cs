namespace Cthangover.Tools.Schemas
{
    using Cthangover.Mods.Schemas;
    using System.Text.Json.Nodes;

    /// <summary>One field's editor in the data editor form: shows the field's JSON and gives it back edited.</summary>
    public interface ISchemaFieldEditor
    {
        /// <summary>The described field.</summary>
        FieldSchema Field { get; }

        /// <summary>Shows <paramref name="node"/> (null: the field is absent).</summary>
        void Load(JsonNode node);

        /// <summary>The edited value, or null to remove the key.</summary>
        JsonNode Save();
    }
}
