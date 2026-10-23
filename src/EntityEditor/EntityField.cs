using System;
using System.Collections.Generic;

namespace Cthangover.Tools.EntityEditor
{
    /// <summary>
    /// One property of an entity as an <see cref="EntityEditorWindow"/> form shows
    /// it. A concrete editor lists its fields; properties no field names are
    /// shown and edited as raw JSON ("other fields"), so the form never hides or
    /// drops data a mod added.
    /// </summary>
    public class EntityField
    {
        /// <summary>JSON property name.</summary>
        public string Key { get; init; }

        /// <summary>Locale key of the label.</summary>
        public string LabelKey { get; init; }

        /// <summary>Locale key of the tooltip, or null.</summary>
        public string TooltipKey { get; init; }

        /// <summary>How the value is edited.</summary>
        public EntityFieldKind Kind { get; init; }

        /// <summary>Picture: the file group of the pictures (<c>items</c>, <c>characters</c>…).</summary>
        public string Group { get; init; }

        /// <summary>Choice, flags, list: the values offered (read when the form is built).</summary>
        public Func<IEnumerable<string>> Options { get; init; }

        /// <summary>Locale text: how a new entity's key is made from its id (<c>{0}</c> = id).</summary>
        public string KeyPattern { get; init; }
    }
}
