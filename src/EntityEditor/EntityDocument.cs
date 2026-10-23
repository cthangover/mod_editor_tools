using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cthangover.Tools.EntityEditor
{
    /// <summary>
    /// The entities of one kind (<c>"Kind": "item"</c>, <c>"character"</c>…) in one
    /// mod, as an editor changes them, free of any UI.
    /// <para/>
    /// Files are kept as JSON trees, not as typed DTOs: a form changes only the
    /// properties it knows, and everything else — fields of other mods' C#
    /// (<c>heal</c>, <c>verb</c>, <c>Charset</c>), loot tables, the property
    /// order — survives a save untouched. A file is written only when its text
    /// differs from what was loaded or last saved, so opening and saving does not
    /// reformat files nobody changed. New entities go to <see cref="NewFile"/>,
    /// created on the first save when the mod has no such file.
    /// <para/>
    /// Localized texts are part of the document too (<see cref="SetText"/>): the
    /// form shows a name in every language of the mod, and an undo step restores
    /// texts and data together. Undo works on whole states, as in the map editor.
    /// </summary>
    public class EntityDocument
    {
        private const int HistoryLimit = 200;

        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly Dictionary<string, JsonObject> files = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> savedText = new(StringComparer.Ordinal);
        private readonly List<EntityRecord> records = new();
        private readonly List<string> undo = new();
        private readonly List<string> redo = new();
        private Dictionary<string, string> texts = new(StringComparer.Ordinal);
        private Dictionary<string, string> savedTexts = new(StringComparer.Ordinal);

        /// <summary>Entity kind of the document.</summary>
        public string Kind { get; }

        /// <summary>File new entities are added to.</summary>
        public string NewFile { get; }

        /// <summary>The entities in file order (files sorted by path).</summary>
        public IReadOnlyList<EntityRecord> Records => records;

        /// <summary>True when there is a step to undo.</summary>
        public bool CanUndo => undo.Count > 0;

        /// <summary>True when there is a step to redo.</summary>
        public bool CanRedo => redo.Count > 0;

        private EntityDocument(string kind, string newFile)
        {
            Kind = kind;
            NewFile = newFile;
        }

        /// <summary>
        /// Builds the document from the mod's JSON files (path, text). Files of
        /// another kind, without a kind or with broken JSON are skipped; their
        /// paths are returned in <paramref name="skipped"/> only when they declare
        /// this kind but cannot be read, so the window can name them.
        /// </summary>
        public static EntityDocument Load(string kind, string newFile, IEnumerable<(string Path, string Text)> sources, out List<string> skipped)
        {
            var document = new EntityDocument(kind, newFile);
            skipped = new List<string>();
            foreach (var (path, text) in sources.OrderBy(s => s.Path, StringComparer.Ordinal))
            {
                JsonObject root;
                try
                {
                    root = JsonNode.Parse(text ?? string.Empty) as JsonObject;
                }
                catch (JsonException)
                {
                    if (text != null && text.Contains($"\"{kind}\""))
                        skipped.Add(path);
                    continue;
                }
                if (root == null || !string.Equals(root["Kind"]?.ToString(), kind, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (root["Items"] is not JsonArray)
                    root["Items"] = new JsonArray();
                document.files[path] = root;
                document.savedText[path] = Serialize(root);
            }
            document.Rebuild();
            return document;
        }

        /// <summary>Paths of the document's files.</summary>
        public IEnumerable<string> Files => files.Keys;

        /// <summary>Adds an entity to <see cref="NewFile"/> (the file is created when missing) and returns it.</summary>
        public EntityRecord Add(JsonObject entity)
        {
            if (!files.TryGetValue(NewFile, out var root))
            {
                root = new JsonObject { ["Kind"] = Kind, ["Items"] = new JsonArray() };
                files[NewFile] = root;
            }
            ((JsonArray)root["Items"]).Add(entity);
            Rebuild();
            return records.First(r => r.Data == entity);
        }

        /// <summary>A copy of <paramref name="record"/> with id <paramref name="newId"/>, placed right after it in its file.</summary>
        public EntityRecord Duplicate(EntityRecord record, string newId)
        {
            var copy = (JsonObject)record.Data.DeepClone();
            copy["Id"] = newId;
            var items = (JsonArray)files[record.File]["Items"];
            items.Insert(items.IndexOf(record.Data) + 1, copy);
            Rebuild();
            return records.First(r => r.Data == copy);
        }

        /// <summary>Removes an entity from its file (the file stays, maybe with no items).</summary>
        public void Remove(EntityRecord record)
        {
            ((JsonArray)files[record.File]["Items"]).Remove(record.Data);
            Rebuild();
        }

        /// <summary><paramref name="wanted"/> when no entity has it, else <c>wanted_2</c>, <c>wanted_3</c>…</summary>
        public string FreeId(string wanted)
        {
            var id = wanted;
            for (var number = 2; records.Any(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase)); number++)
                id = wanted + "_" + number;
            return id;
        }

        /// <summary>The record with this id, or null.</summary>
        public EntityRecord Find(string id) => records.FirstOrDefault(r => r.Id == id);

        /// <summary>A localized text the document holds (see <see cref="SetText"/>), or null when it was never set or loaded.</summary>
        public string GetText(string language, string key)
            => texts.TryGetValue(TextKey(language, key), out var value) ? value : null;

        /// <summary>Remembers the text the mod's files have now (loading, not an edit).</summary>
        public void LoadText(string language, string key, string value)
        {
            var textKey = TextKey(language, key);
            texts[textKey] = value;
            savedTexts[textKey] = value;
        }

        /// <summary>Changes a localized text; it is written on save.</summary>
        public void SetText(string language, string key, string value) => texts[TextKey(language, key)] = value ?? string.Empty;

        /// <summary>Remembers the current state as one undo step and forgets the redo branch.</summary>
        public void BeginChange()
        {
            undo.Add(Snapshot());
            if (undo.Count > HistoryLimit)
                undo.RemoveAt(0);
            redo.Clear();
        }

        /// <summary>Steps back; false when there is nothing to undo.</summary>
        public bool Undo() => Swap(undo, redo);

        /// <summary>Steps forward again; false when there is nothing to redo.</summary>
        public bool Redo() => Swap(redo, undo);

        /// <summary>Files whose text changed since loading or the last <see cref="MarkSaved"/>.</summary>
        public List<(string Path, string Text)> ChangedFiles()
            => files.Select(f => (f.Key, Serialize(f.Value)))
                .Where(f => !savedText.TryGetValue(f.Key, out var saved) || saved != f.Item2)
                .ToList();

        /// <summary>Texts changed since loading or the last <see cref="MarkSaved"/>: language, key, value.</summary>
        public List<(string Language, string Key, string Value)> ChangedTexts()
            => texts.Where(t => !savedTexts.TryGetValue(t.Key, out var saved) || saved != t.Value)
                .Select(t => (t.Key.Substring(0, t.Key.IndexOf('\n')), t.Key.Substring(t.Key.IndexOf('\n') + 1), t.Value))
                .ToList();

        /// <summary>The current files and texts are what is on disk now.</summary>
        public void MarkSaved()
        {
            foreach (var (path, text) in ChangedFiles())
                savedText[path] = text;
            savedTexts = new Dictionary<string, string>(texts, StringComparer.Ordinal);
        }

        /// <summary>Problems that would break the game's data, as locale keys with an argument; empty when it can be saved.</summary>
        public List<(string Key, string Argument)> Validate()
        {
            var problems = new List<(string, string)>();
            foreach (var record in records.Where(r => string.IsNullOrWhiteSpace(r.Id)))
                problems.Add(("tools/entity_editor/problem_id", record.File));
            foreach (var group in records.Where(r => !string.IsNullOrWhiteSpace(r.Id)).GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase))
            {
                if (group.Count() > 1)
                    problems.Add(("tools/entity_editor/problem_duplicate", group.Key));
            }
            return problems;
        }

        /// <summary>A file's text as the document writes it: two-space indented, letters unescaped, LF line breaks (the writer would use CRLF on Windows), a final one.</summary>
        public static string Serialize(JsonObject root) => root.ToJsonString(WriteOptions).Replace("\r\n", "\n") + "\n";

        private static string TextKey(string language, string key) => language + "\n" + key;

        private void Rebuild()
        {
            records.Clear();
            foreach (var file in files.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                foreach (var item in (JsonArray)file.Value["Items"])
                {
                    if (item is JsonObject entity)
                        records.Add(new EntityRecord(file.Key, entity));
                }
            }
        }

        private string Snapshot()
        {
            var state = new JsonObject
            {
                ["files"] = new JsonObject(files.Select(f => new KeyValuePair<string, JsonNode>(f.Key, f.Value.DeepClone()))),
                ["texts"] = new JsonObject(texts.Select(t => new KeyValuePair<string, JsonNode>(t.Key, t.Value)))
            };
            return state.ToJsonString();
        }

        private bool Swap(List<string> from, List<string> to)
        {
            if (from.Count == 0)
                return false;
            to.Add(Snapshot());
            var state = JsonNode.Parse(from[^1]).AsObject();
            from.RemoveAt(from.Count - 1);
            files.Clear();
            foreach (var file in state["files"].AsObject())
                files[file.Key] = file.Value.DeepClone().AsObject();
            texts = state["texts"].AsObject().ToDictionary(t => t.Key, t => t.Value?.ToString() ?? string.Empty, StringComparer.Ordinal);
            Rebuild();
            return true;
        }
    }
}
