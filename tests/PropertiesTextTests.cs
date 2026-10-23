namespace Cthangover.Tools.Services.Tests
{
    using Cthangover.Core.Mods.Testing;

    /// <summary>Changing one key of a .properties text without touching the rest.</summary>
    public class PropertiesTextTests
    {
        private const string Text = "# items\nitems/bread/name = Хлеб\nitems/bread/desc=Свежий\n\nitems/cake/name = Торт\n";

        [ModTest("properties_text/get_reads_like_the_loader")]
        static void GetReadsLikeTheLoader(ModTestContext ctx)
        {
            ModAssert.Equal("Хлеб", PropertiesText.Get(Text, "items/bread/name"), "spaces around '=' are trimmed");
            ModAssert.Equal("Свежий", PropertiesText.Get(Text, "items/bread/desc"), "no spaces work too");
            ModAssert.Null(PropertiesText.Get(Text, "items/pie/name"), "a missing key");
            ModAssert.Null(PropertiesText.Get(null, "x"), "no text");
            ModAssert.Equal("b", PropertiesText.Get("x = a\nx = b\n", "x"), "the last line wins");
            ModAssert.Equal(3, PropertiesText.Keys(Text).Count, "three keys, the comment is not one");
        }

        [ModTest("properties_text/set_changes_one_line")]
        static void SetChangesOneLine(ModTestContext ctx)
        {
            ModAssert.Equal(Text.Replace("= Хлеб", "= Булка"), PropertiesText.Set(Text, "items/bread/name", "Булка"), "only that line changes");
            ModAssert.Equal(Text + "items/pie/name = Пирог\n", PropertiesText.Set(Text, "items/pie/name", "Пирог"), "a new key is appended");
            ModAssert.Equal("a = 1\nb = 2\n", PropertiesText.Set("a = 1", "b", "2"), "a missing final line break is added first");
            ModAssert.Equal("a = 1\r\nb = 2\r\n", PropertiesText.Set("a = 1\r\n", "b", "2"), "CRLF text stays CRLF");
            ModAssert.Equal("a = x\r\n", PropertiesText.Set("a = 1\r\n", "a", "x"), "a CRLF line is rewritten with its CR");
            ModAssert.Equal("a = one two\n", PropertiesText.Set("", "a", "one\ntwo"), "a line break in a value becomes a space");
        }
    }
}
