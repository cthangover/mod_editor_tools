namespace Cthangover.Tools.Schemas
{
    using System;
    using Cthangover.Mods.Schemas;
    using Godot;

    /// <summary>
    /// Picks the editor of a schema field by its type: a card list for <c>list</c> and
    /// <c>values</c>, a key/value table for <c>map</c>, texts per language for <c>locale</c>,
    /// a single control for everything else. Shared by the data editors (their "fields of
    /// other mods" block) and the Data Editor window, so a field looks the same in both.
    /// </summary>
    public static class SchemaFieldEditors
    {
        /// <summary>Adds the editor of <paramref name="field"/> to <paramref name="parent"/>; <paramref name="changed"/> runs on every edit.</summary>
        public static ISchemaFieldEditor Create(Container parent, FieldSchema field, Action changed)
        {
            switch (field.Type?.ToLowerInvariant())
            {
                case "list":
                case "values":
                    return new ListFieldEditor(parent, field, changed);
                case "map":
                    return new MapFieldEditor(parent, field, changed);
                case "locale":
                    return new LocaleFieldEditor(parent, field, changed);
                default:
                    return new ScalarFieldEditor(parent, field, changed);
            }
        }
    }
}
