namespace Cthangover.Tools.Tests.Windows
{
    using System;
    using System.IO;
    using System.Linq;
    using Cthangover.Mods.Rpg;
    using Cthangover.Tools.Common;
    using Cthangover.Tools.RpgTilesetEditor;
    using Cthangover.Tools.Services;
    using Godot;

    /// <summary>Edits a temporary tileset through the real window and verifies the saved and published data.</summary>
    public static class ToolWindowsAtlasExercise
    {
        private const string Mod = "test_defaults_and_patches";
        private const string FileName = "tilesets/tools_split_atlas.json";
        private const string TilesetId = "tools_split/atlas";
        private const string Fixture = "{\"Kind\":\"rpg_tileset\",\"Items\":[{\"Id\":\"tools_split/atlas\",\"Image\":\"village/objects\",\"SurfaceImage\":\"village/surfaces\",\"SurfaceSize\":128,\"TileSize\":32,\"Columns\":12,\"Tiles\":[{\"Index\":100,\"AtlasIndex\":0}],\"VisualGroups\":[]}]}";

        /// <summary>Selection, object anchors, collision, surfaces, undo/redo and Save use public controls only.</summary>
        public static void Run(Window window, Action<bool, string> check)
        {
            var original = ModFileService.ReadText(Mod, FileName);
            var filePath = ModFileService.GetWritablePath(Mod, FileName);
            var folderExisted = Directory.Exists(Path.GetDirectoryName(filePath));
            try
            {
                check(ModFileService.WriteText(Mod, FileName, Fixture), "atlas: temporary fixture is writable");
                var selector = window.FindChildren("*", "", true, false).OfType<ModFileSelector>().Single();
                selector.Refresh();
                var mods = selector.GetChildren().OfType<OptionButton>().First();
                var at = Enumerable.Range(0, mods.ItemCount).First(i => mods.GetItemText(i) == Mod);
                mods.Select(at);
                mods.EmitSignal(OptionButton.SignalName.ItemSelected, (long)at);
                check(selector.CurrentMod == Mod && selector.CurrentPath == FileName, "atlas: selected the test file");
                var view = window.FindChildren("*", "", true, false).OfType<RpgTilesetAtlasView>().Single();
                check(view.GridSize.X > 1, "atlas: image loaded");
                view.Select(new Rect2I(0, 0, 2, 1));
                Press(window, "tools/rpg_tileset_editor/make_tiles");
                var solid = window.FindChildren("*", "", true, false).OfType<CheckBox>().First();
                solid.ButtonPressed = true;
                Press(window, "tools/rpg_tileset_editor/make_object");
                var anchors = window.FindChildren("*", "", true, false).OfType<SpinBox>().ToArray();
                anchors[0].Value = 1.25;
                anchors[1].Value = 1.5;
                Press(window, "tools/rpg_tileset_editor/undo");
                check(anchors[1].Value == 1, "atlas: undo restores the object anchor");
                Press(window, "tools/rpg_tileset_editor/redo");
                check(anchors[1].Value == 1.5, "atlas: redo restores the edited anchor");
                var tabs = window.FindChildren("*", "", true, false).OfType<TabContainer>().Single();
                tabs.CurrentTab = 1;
                Press(window, "tools/rpg_tileset_editor/add_surface");
                Press(window, "tools/common/save");
                var saved = RpgTilesetDocument.Load(ModFileService.ReadText(Mod, FileName));
                check(saved.Tileset.VisualGroups.Count == 1 && saved.Tileset.VisualGroups[0].Pattern.Length == 2, "atlas: object and logical tile ids saved");
                check(saved.Tileset.VisualGroups[0].AnchorX == 1.25 && saved.Tileset.VisualGroups[0].AnchorY == 1.5, "atlas: anchors saved");
                check(saved.Tileset.TileOf(100).Solid, "atlas: collision saved");
                check(saved.Tileset.Tiles.Any(t => t.Surface == 0), "atlas: surface saved");
                var published = RpgTilesetFactory.Instance.Get(TilesetId);
                check(published.VisualGroups.Count == 1 && published.VisualGroups[0].AnchorY == 1.5, "atlas: runtime cache sees saved changes");
            }
            finally
            {
                if (original != null)
                    ModFileService.WriteText(Mod, FileName, original);
                else if (File.Exists(filePath))
                    File.Delete(filePath);
                var folder = Path.GetDirectoryName(filePath);
                if (!folderExisted && Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                    Directory.Delete(folder);
            }
        }

        private static void Press(Window window, string key)
            => window.FindChildren("*", "", true, false).OfType<Button>().First(b => b.Text == TranslationServer.Translate(key)).EmitSignal(BaseButton.SignalName.Pressed);
    }
}
