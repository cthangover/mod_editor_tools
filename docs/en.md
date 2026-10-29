# Editor Tools — making scenes and scenarios inside the running game

Editor Tools are the author's windows that open over the running game: the scene editor, the scenario editor, editors for the world map, interactive objects and lights, a builder of Godot scenes and the catalog export of a mod. Everything they save is ordinary mod files, and the game picks the change up at once, so an author writes a place, walks into it and plays it without restarting.

The mod also holds the base of the item and character editors (they are separate mods: Items Editor, Characters Editor). A game gets these tools through the Developer Tools toolset or its own collection.

## Opening the tools

In a game, the hammer button on the toolbar opens the tool box:

![The toolbar of the novel template with the hammer pressed and the tool box open: Interactive Editor, Light Editor, Map Editor, Scene editor.](assets/toolbox.jpg)

- **Scene editor**, **Map Editor**, **Interactive Editor** and **Light Editor** are in the tool box. Other tool mods add their own entries there (the item, character and RPG map editors, the Data Editor).
- The **scenario editor** opens from the scene editor: select a scene, pick one of its scenarios and press **Open**, or **+ New scenario**.
- When the tools are loaded at the main menu (for example with `--all-mods`), every tool also has a button in the menu's tool block, including the scene builder and the catalog export.

The windows stay open across scene changes, can be moved and resized, and close with the cross.

## A whole small edit: a new place

The novel template has a town, a bar and a home. Here an author adds a park that can be reached from the town, using only the editors.

**1. A new scene.** In the scene editor press **+ New scene**, type the name, choose the mod to save it to and add a background:

![The scene editor: the scenes my_novel_bar, my_novel_home and my_novel_town of my_novel_game on the left; in the middle the background home/vestibule added to the scene; on the right Name my_novel_park, Save to my_novel_game, Sound none, Default scenario none.](assets/new-scene.jpg)

Save (Ctrl+S) writes `scenes/my_novel_park.json` into `my_novel_game`; the game knows the scene at once.

**2. Its scenario.** **+ New scenario** opens the scenario editor with a file for the scene. The park offers one way back:

![The scenario editor with my_novel_park.scenario: scene my_novel_park, priority 10, the narration "A quiet park behind the town. Wet leaves, an empty bench.", the choice "What next?" with "Back to the town" leading to switch_scene my_novel_town.](assets/park-scenario.jpg)

**3. A way there.** In the town's scenario one answer and one label are added:

![The scenario editor with my_novel_town.scenario: the choice "Where to go?" now has the answer "Go to the park" -> :park, and the label :park does switch_scene my_novel_park.](assets/town-scenario.jpg)

```scenario
option "Go to the park" -> :park

:park
switch_scene my_novel_park
end
```

Saving a scenario drops the game's scenario caches, so the next entry into the town plays the new text.

**4. Play it.** **Go to scene** in the scene editor, or simply playing on, enters the town, and the new answer is there:

![The town in the running game: "Where to go?" with Go into the bar, Go to the park, Go back home.](assets/town-choice.jpg)

![The new park after choosing Go to the park: "What next?" with Back to the town.](assets/park.jpg)

## The editors

- **Scene editor** — the places of a novel: name, the mod to save to, backgrounds (one is picked at random on entry), ambient sound with a listen button, the default scenario and the scene's scenarios. **Go to scene** ends the running dialog and enters the scene.
- **Scenario editor** — the files of every mod, a summary and a graph of scenes and transitions; DSL highlighting; Ctrl+S saves; **Play** (F5) saves and plays the scenario inside the running game, and Stop returns to where the game was; **Open in external editor** opens the file in the system's editor.
- **Map Editor** — the zones of the world map.
- **Interactive Editor** — clickable objects of the current scene.
- **Light Editor** — the lights of the current scene.
- **Scene builder** — Godot `.tscn` scenes for mods.
- **Catalog export** — packs a mod into a zip and writes its entry for a mod catalog with checksums.

Undo works while editing (Ctrl+Z in the text and in the forms of the entity editors). Checking a mod for broken references is the Validator's job; the editors do not stop you from saving a scenario with a mistake.

## Whose files are written

The tools write only into mods installed as folders. A scene is the sum of the `scenes/<name>.json` files of all mods, so the scene editor writes the file of the mod chosen in **Save to**. Changing a scene that belongs to another or a packed mod creates a file in your mod that adds to it; backgrounds that come from other files are shown locked. Scenarios are saved in the mod they belong to.

## If something goes wrong

1. **The hammer button is missing.** The game does not include the tools; add the Developer Tools toolset in its collection or run with `--with-mods=editor_tools`.
2. **A new scenario does not play.** Its header names another scene, or another scenario of the scene is chosen first (compare their `priority`).
3. **Save to lists no mods.** Only mods installed as folders can be written to.
