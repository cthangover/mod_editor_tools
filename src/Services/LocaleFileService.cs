using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cthangover.Core.Localization;
using Godot;

namespace Cthangover.Tools.Services
{
    /// <summary>
    /// Texts of one mod's <c>locale/*.properties</c> for the editors that show a
    /// localized name instead of its key. A language's files are
    /// <c>&lt;lang&gt;.properties</c> and <c>&lt;anything&gt;_&lt;lang&gt;.properties</c>,
    /// as <c>LocaleLoader</c> reads them.
    /// <para/>
    /// A write goes into the file of the language that already declares the key;
    /// a new key goes into <c>&lt;lang&gt;.properties</c>, else the first non-scenario
    /// file of the language, else a new <c>locale/&lt;lang&gt;.properties</c>.
    /// Scenario string files (<c>scenarios_*</c>) never get entity texts.
    /// </summary>
    public static class LocaleFileService
    {
        private const string Dir = "locale";

        /// <summary>Languages the mod has texts for, plus the game's current one; the current one first.</summary>
        public static List<string> Languages(string modId)
        {
            var current = LanguageCode.Normalize(TranslationServer.GetLocale()) ?? "ru";
            var languages = new List<string> { current };
            foreach (var file in ModFileService.ListFiles(modId, Dir, ".properties"))
            {
                var language = LanguageOf(file);
                if (language != null && !languages.Contains(language))
                    languages.Add(language);
            }
            return languages;
        }

        /// <summary>The mod's text of <paramref name="key"/> in <paramref name="language"/>, or null.</summary>
        public static string Read(string modId, string language, string key)
        {
            string found = null;
            foreach (var file in FilesOf(modId, language))
                found = PropertiesText.Get(ModFileService.ReadText(modId, file), key) ?? found;
            return found;
        }

        /// <summary>Sets the text of <paramref name="key"/> in <paramref name="language"/>; false when the mod is not writable.</summary>
        public static bool Write(string modId, string language, string key, string value)
        {
            var files = FilesOf(modId, language);
            var target = files.FirstOrDefault(f => PropertiesText.Keys(ModFileService.ReadText(modId, f)).Contains(key))
                ?? files.FirstOrDefault(f => Path.GetFileName(f) == language + ".properties")
                ?? files.FirstOrDefault(f => !Path.GetFileName(f).StartsWith("scenarios_", StringComparison.Ordinal))
                ?? $"{Dir}/{language}.properties";
            return ModFileService.WriteText(modId, target, PropertiesText.Set(ModFileService.ReadText(modId, target), key, value));
        }

        /// <summary>Reloads the game's texts of the current language, so a saved name shows at once.</summary>
        public static void ReloadGame() => LocaleLoader.LoadLocale(TranslationServer.GetLocale());

        private static List<string> FilesOf(string modId, string language)
            => ModFileService.ListFiles(modId, Dir, ".properties").Where(f => LanguageOf(f) == language && Path.GetDirectoryName(f)?.Replace('\\', '/') == Dir).ToList();

        private static string LanguageOf(string file)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var language = name.Substring(name.LastIndexOf('_') + 1);
            return LanguageCode.Supported.Contains(language) ? language : null;
        }
    }
}
