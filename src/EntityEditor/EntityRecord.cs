using System.Text.Json.Nodes;

namespace Cthangover.Tools.EntityEditor
{
    /// <summary>One entity of an <see cref="EntityDocument"/>: its JSON object inside the <c>Items</c> array of its file.</summary>
    public class EntityRecord
    {
        /// <summary>Mod-relative path of the file holding the entity.</summary>
        public string File { get; }

        /// <summary>The entity itself; unknown properties are kept as they are.</summary>
        public JsonObject Data { get; }

        /// <summary>The entity's <c>Id</c> (empty when missing).</summary>
        public string Id => Data["Id"]?.ToString() ?? string.Empty;

        /// <summary>Wraps an entity of a file.</summary>
        public EntityRecord(string file, JsonObject data)
        {
            File = file;
            Data = data;
        }
    }
}
