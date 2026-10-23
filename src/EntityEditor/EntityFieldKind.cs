namespace Cthangover.Tools.EntityEditor
{
    /// <summary>How an <see cref="EntityField"/> is shown and stored.</summary>
    public enum EntityFieldKind
    {
        /// <summary>One line of text.</summary>
        Text,
        /// <summary>A locale key with its text in every language of the mod.</summary>
        LocaleText,
        /// <summary>A whole number.</summary>
        Integer,
        /// <summary>A picture path in a file group, with a preview and a picker.</summary>
        Picture,
        /// <summary>One value of a list (values outside the list stay possible).</summary>
        Choice,
        /// <summary>Several names of a list written as <c>"A,B"</c> (an enum with flags).</summary>
        Flags,
        /// <summary>A JSON array of strings picked from a list.</summary>
        List
    }
}
