# Cardinal - The Cardinal System

A modding tool for Echoes of Aincrad, in the spirit of Skyrim's Creation Kit. Version 0.5 reads the game, builds data and text mods from your changes, adds new items, and merges mods that change the same files.

- **Tables:** browse the game's items, recipes, enemies, skills, quests and every other data table, with English text next to each text key. Search, and export to CSV or JSON.
- **Text:** read and change the game's text in all of its languages, next to the English, and export a language to CSV for translating and import it back.
- **Editing and mod building:** change values right in the tables and lines in the Text tab, keep the changes as a small mod project, and build them into a mod with one button, or straight into the game's `~mods` folder.
- **New items:** add an item that starts as a copy of an existing one, with its own ID, name and description in any language.
- **New assets:** add a copy of a game asset, such as a town, under its own name, and keep every installed mod's new assets findable by the game with one registry patch.
- **Merging and conflicts:** combine several mods into one merged patch so they all work, and see which installed mods, made with Cardinal or not, replace the same game files and which copy the game loads.
- **Map:** see each floor from above, cell by cell, colored by region, plus the HLOD grids the game draws in the distance. Switch on the developer layers the level designers left in the game, such as combat area circles with their sizes, treasure chest spots, barriers and size references. Hover for details, and switch on the cells installed mods replace.
- **Command line:** everything the window does, scriptable, for batch work and AI assistants.

Cardinal reads your own copy of the game and never changes the game's own files. Mods it builds are new files in `Paks\~mods`, so deleting them undoes them. It doesn't contain any of the game's files.

## Making a mod

1. Open a table and double-click a value. Numbers, text, true or false, and choices can be changed; lists and groups can't yet. Cardinal checks each value against the game file before taking it, and changed values turn yellow.
2. Every change collects in the **Mod** tab, with its original value. Select changes there to revert them, or type the original value back into the table.
3. Give the mod a name, version, author and priority, and **Save project**. The project is a small JSON file holding only your changes (see below), so it can be rebuilt after a game update.
4. **Build mod into a folder** or **Build and install into ~mods**. Cardinal pulls fresh copies of the files it changes from the unmodded game, applies your changes, and packs them into `<Name>_<priority>_P.utoc`, `.ucas` and `.pak`. Changed text goes in the `.pak`, so a mod that only changes text is just the `.pak`.

Builds always start from the unmodded game, never from other installed mods.

After a game update, **Check against the game** lists changes that can't be made any more, because a table, row, field or line of text is gone or the game now has a row or text key the mod adds, and changes whose original value or text the update changed. Building again is all a rebuild needs, since every build starts fresh.

Some tables are read-only for now: the enemy blueprint list and quests (Cardinal can't write those back yet), and any table whose file can't be decoded with your mappings. The Tables tab says why. Mappings written for your exact game version, by Cardinal Mappings, usually fix the second kind.

## Changing text

The **Text** tab shows every line of one language at a time, next to its English, with the namespace and key that identify it. Double-click a line's text to change it; changed lines turn yellow and collect in the Mod tab with the rest of the project. Typing the game's own text back removes the change.

The game keeps each language's text in one file, `Localization/Game/<language>/Game.locres`, and a mod can only replace that file whole. So a text mod carries every line of each language it touches, not only the changed ones. Two things follow:

- **Rebuild text mods after every game update.** An old build would put back the text from before the update in that whole language. **Check against the game** lists lines the update changed.
- **Two text mods for the same language always conflict.** Only one mod's text shows. Merge them (see below) to keep every change.

**Translating:** **Export for translating** writes a CSV with `Namespace`, `Key`, `English` and `Text` columns for the chosen language, with the open project's changes already in the `Text` column. Translate the `Text` column in any spreadsheet and keep the rest as it is, then **Import translation** reads it back into the project. Lines the game doesn't have are skipped and listed, and lines whose English has changed since the export are imported but listed too, since their translation may need another look. The CSV is UTF-8.

## Adding items

Select an item in one of the Items tables that end in `Item`, such as **UseItem** (potions and other consumables), and press **New item from this row**. The heal, Col, sphere and skill point tables aren't offered: each holds one field pickup that's used on the spot, so a copy could never be held. Give it an ID (the next free one is filled in), an English name and a description. The new item:

- is a copy of the selected one, so it has the same icon, rarity, prices and effects until you change them. Its row shows up highlighted at the bottom of the table, and you can change its values there like any other. A new icon needs The Seed, Cardinal's Unreal kit, which isn't out yet.
- gets new text keys for its name and description in the game's main string table, named like the game's own (`ItemName_Usable_<ID>`). Every language shows the English until you give it its own: new lines are listed first on the Text tab.
- for weapons, shields and gear, also gets copies of the source's rows in the tables that hold menu pictures by ID (such as `DT_OneHandedSwordThumbnail`). Whether the game finds a 3D model for a new ID hasn't been tested.

Cardinal can't put it in a shop, chest or drop list yet: the shop list can't be written back with current mappings, and drop and chest lists are lists, which Cardinal can't edit yet. To get it in game, point an existing recipe's result at it (`ItemData.ItemId` in a recipe table) or give it to yourself with a dev mod. It also isn't added to the in-game database (`DT_ItemDatabase`).

A new item changes the item asset and the main string table, so two mods that add items always replace each other's copies of those files. Merge them to keep both. Two mods adding the same ID to the same table clash, and the later one in the merge wins.

## New assets and the registry patch

A mod can add a copy of a game asset under a new name, such as a town: `new asset` on the command line adds `/Game/ROD/DataAssets/Town/Town_004` as `/Game/ROD/DataAssets/Town/Town_011`. The copy starts with every value of the original. Together with a new `DT_TownList` row whose `ID` is `011`, that's a new town in the warp list, sharing Urbas's map.

The game only finds towns, quests and other primary assets through its asset registry, `AssetRegistry.bin`, and a mod can only replace that file whole. So instead of carrying a registry, a mod built with new assets carries a short list of them in its `.pak`, and **Patch new assets** on the Installed mods tab (or `Cardinal.exe patch`) builds one `CardinalRegistry_9999_P.pak` from every installed mod's list. It starts from the registry the game would load without it, so mods that ship their own registry keep working too.

- Installing a mod with new assets from Cardinal updates the patch by itself.
- After installing or removing such mods any other way, press **Patch new assets** again. With none left, it removes the patch.
- Without the patch, the game can't find a new town at all: warping there leaves the screen black.
- Players who use mods with new assets need Cardinal for this one button, and nothing else.

Removing a mod that added a town, quest or item leaves saves that still load: the game drops what it can't find.

## Mods that change the same files

A mod replaces whole game files, so two mods that change the same file can't both work: the game loads one copy and ignores the other. Which one it loads is decided by the number before `_P` in the file name, the patch priority. Higher wins, equal priorities are left to chance, and a name ending in just `_P` ranks the same as the game's own update (`pakchunk0-WindowsClient_0_P`), so that's left to chance too.

- **Priorities:** a project's `priority` goes from 1 to 9998, default 1000, and becomes part of the file name, as in `MyMod_1000_P`. Load order is just priorities: give the mod that should win the higher number.
- **Merging (Merge tab):** add the projects in order and build one `CardinalMerged_9999_P` patch from all their changes. Where two set the same value or line of text, the later one in the list wins, and the tab lists every such value and line. 9999 is kept for the merged patch so it outranks every mod it was made from. When installing it, Cardinal offers to remove those mods' own builds from `~mods`, since the merged patch has all their changes. The list is saved with your settings.
- **Conflicts (Installed mods tab):** scans everything under `Paks`, including mods not made with Cardinal, and lists each game file more than one mod replaces, or that a mod replaces without outranking the game, with what the game ends up loading. **Show cells installed mods replace** on the Map tab colors those map cells by the mod that wins them, and red where it's left to chance.
- **Copy diagnostic info** in the status bar copies Cardinal's version, the game's version, your setup and installed mods for a bug report. It never includes your key, and paths show up relative to the game folder or as bare file names.

## Setup

Run `Cardinal.exe`. The Setup tab asks for four things, saved in `%APPDATA%\Cardinal\settings.json`:

1. **Game Paks folder:** `EchoesofAincrad\Content\Paks` in the game's install folder. **Detect** finds it in your Steam libraries.
2. **AES key:** the game's files are encrypted, and Cardinal can't include the key. Run [AESDumpster](https://github.com/GHFear/AESDumpster) on `EchoesofAincrad-Win64-Shipping.exe` and paste the key it prints.
3. **Mappings (.usmap):** describes the game's data layout. The easiest way is the Cardinal Mappings companion mod, an optional download on the same page: install it with UE4SS, start the game once and wait at the title screen for about 15 seconds, and Cardinal finds the file by itself. It writes new mappings after each game update. You can also make one by hand with UE4SS's `DumpUSMAP()` and choose it here.
4. **Oodle library:** the files are compressed with Oodle, which Cardinal can't ship. **Download** fetches it from the [OodleUE](https://github.com/WorkingRobot/OodleUE) builds, as FModel does, or choose an `oo2core_9_win64.dll` or `oodle-data-shared.dll` you already have. Building a mod also needs internet access the first time, because retoc fetches its own copy of Oodle from the same place, checked against a fixed hash.

Then press **Save and open the game files**.

## Command line

Any arguments run Cardinal without the window. Commands use the saved settings; `--paks`, `--key`, `--usmap` and `--oodle` override them for one run. `--with-mods` reads the game as the mods installed in `Paks\~mods` change it, instead of unmodded.

```
Cardinal.exe setup [--paks DIR] [--key HEX] [--usmap FILE] [--oodle FILE] [--download-oodle]
Cardinal.exe list tables
Cardinal.exe list floors
Cardinal.exe list layers WL02
Cardinal.exe export tables --out DIR [--only Items/MaterialItem,Skills/DT_ActiveSkillList]
Cardinal.exe export map WL02|all --out DIR
Cardinal.exe list cultures
Cardinal.exe text get CULTURE KEY
Cardinal.exe text set PROJECT.json CULTURE NAMESPACE KEY TEXT
Cardinal.exe text export CULTURE --out FILE.csv [--project PROJECT.json]
Cardinal.exe text import CULTURE FILE.csv PROJECT.json
Cardinal.exe set PROJECT.json TABLE ROW FIELD VALUE
Cardinal.exe new item PROJECT.json --from ROW --name TEXT --description TEXT [--id ID] [--category TABLE]
Cardinal.exe build PROJECT.json --out DIR
Cardinal.exe build PROJECT.json --install [--force]
Cardinal.exe check PROJECT.json
Cardinal.exe merge A.json B.json [C.json...] --out DIR
Cardinal.exe merge A.json B.json [C.json...] --install [--force] [--remove-single-builds]
Cardinal.exe new asset PROJECT.json FROM PACKAGE
Cardinal.exe patch
Cardinal.exe conflicts
Cardinal.exe diagnostics
Cardinal.exe help
```

- `list tables` prints each table's id, source and `editable` or `read-only`.
- `set` checks one change against the game file and saves it in the project, creating the project if it doesn't exist. `FIELD` is the column name as the table shows it, such as `CoolTime` or `QuestData.Floor`. Setting a field back to its original value removes the change. Example: `Cardinal.exe set MyMod.json Skills/DT_ActiveSkillList 0 CoolTime 2.5`.
- `list cultures` prints each language the game has text for: its code (`en`, `ja-JP`, ...), name and number of lines. `CULTURE` takes the code, in any case.
- `text get` prints every line with that key in that language: namespace, key and text, tab-separated. `text set` changes one line in the project, like `set` does for values. Output is UTF-8.
- `text export` and `text import` are the translating round trip described in [Changing text](#changing-text). `text import` exits with 5 when it skipped or flagged any line.
- `build` checks every change first and builds nothing if any is wrong. `--install` builds straight into `Paks\~mods` and refuses to replace an earlier build of the same mod unless you add `--force`.
- `check` re-checks every change against the current game. Each problem line starts with `can't build` or `changed` (the original value is different now; the change still applies).
- `merge` prints every value and line of text more than one project sets and which wins, then builds `CardinalMerged_9999_P`. `--remove-single-builds` deletes the merged projects' own builds from `~mods`.
- `conflicts` prints a `mod` line per installed mod and a `conflict` line per game file, tab-separated, with what the game loads.
- `diagnostics` prints the same report as the window's **Copy diagnostic info**.
- `new asset` adds a copy of the game asset `FROM` as `PACKAGE`, both `/Game` paths; see [New assets and the registry patch](#new-assets-and-the-registry-patch). `patch` rebuilds the registry patch from every installed mod and lists each new asset; problems exit with 5.
- `new item` adds an item to the project; see [Adding items](#adding-items). `--category` is the item table, such as `Items/UseItem`, and is needed when `ROW` is an ID in more than one. Without `--id` it takes the next free ID. For text the project adds, `text set` sets one language's line, and empty text makes that language show the English again.
- `set`, `text set`, `text import`, `new item`, `check`, `build` and `merge` always work from the unmodded game and refuse `--with-mods`.

Output goes to stdout, errors to stderr. Exit codes:

| Code | Meaning |
|---|---|
| 0 | Done |
| 1 | Bad arguments, unknown command, table or row, or a change that can't be made (wrong type, read-only table) |
| 2 | Setup missing or wrong (paks, key, mappings, Oodle, retoc, repak), or the files didn't open with that key |
| 3 | Something couldn't be found or read in the game files. `export tables` still writes the rest and lists failures in `tables.json` |
| 4 | Writing the output failed, or `--install` found an earlier build without `--force` |
| 5 | `check`, `conflicts`, `patch` or `text import` found something to look at |

## Mod projects

A project is a small JSON file holding only the changes. Field names are stable within a `formatVersion`; this is version 4. Version 2 added `textEdits`, version 3 `addedRows` and `addedText`, and version 4 `addedAssets`. Older files open unchanged, and are saved as version 4.

```json
{ "formatVersion": 4, "name": "FasterRecovery", "version": "1.0.0", "author": "", "description": "", "priority": 1000,
  "edits": [ { "table": "Skills/DT_ActiveSkillList", "row": "0", "field": "CoolTime", "value": "2.5", "original": "5" } ],
  "textEdits": [ { "culture": "en", "namespace": "ST_DatabaseLocalizeList", "key": "...", "value": "New text", "original": "Old text" } ],
  "addedRows": [ { "table": "Items/UseItem", "key": "75", "from": "1",
                   "fields": { "ID": "75", "ItemKey": "ItemName_Usable_75", "DescriptionKey": "ItemDescription_Usable_75" } } ],
  "addedText": [ { "namespace": "ST_GeneralLocalizeList", "key": "ItemName_Usable_75", "text": { "en": "My Item", "ja-JP": "..." } } ],
  "addedAssets": [ { "from": "/Game/ROD/DataAssets/Town/Town_004", "package": "/Game/ROD/DataAssets/Town/Town_011" } ] }
```

- `table`: a table id from `list tables`. `row`: the row key. `field`: the column name.
- `value`: the new value as text. Enum values can be written with or without their type, as `Crystal02` or `EDropGimmick::Crystal02`.
- `original`: the unmodded value when the change was made, for reference. Builds don't need it.
- `textEdits`: `culture` is a code from `list cultures`, `namespace` and `key` identify the line, `value` is the new text and `original` the game's text when the change was made.
- `addedRows`: a new row in `table` with the key `key`. It starts as a copy of the game's row `from` in the same table, and `fields` replaces single values of the copy by column name. A field the copied row leaves at its default can't be set yet. Rows are added before `edits` apply, so the copy is always of the game's own row.
- `addedAssets`: a copy of the game asset `from` as the new asset `package`, both `/Game` paths. The last part of `package` is the copy's name: letters, digits and underscores.
- `addedText`: a new key in `namespace`, which must be `ST_GeneralLocalizeList`, the game's main string table. `text` holds a line per culture and must have `en`, which goes in the string table itself: every language without its own line shows it, as the game does for its own untranslated lines.
- `priority`: 1 to 9998, default 1000 when missing. The built files are named after `name` (letters, digits, dashes and underscores) plus `_<priority>_P`, so `FasterRecovery_1000_P`. Any priority makes the mod outrank the game's own patch; see [Mods that change the same files](#mods-that-change-the-same-files).

## Export formats

Field names are stable within a `formatVersion`. Cardinal writes version 1.

### Tables

`export tables` writes `DIR/<group>/<name>.json` and `.csv` for each table, plus `DIR/tables.json`.

Table ids (`list tables`) are `<group>/<name>`. Groups: `Items`, `Enemies`, `Skills`, `Quests`, `Other` (every remaining `DT_` data table in the game).

`tables.json`:

```json
{ "formatVersion": 1,
  "tables": [ { "id": "Items/MaterialItem", "group": "Items", "name": "MaterialItem",
                "source": "/Game/ROD/DataAssets/Items/ItemDataAsset", "rows": 53, "error": null } ] }
```

`<name>.json`:

```json
{ "formatVersion": 1, "table": "MaterialItem", "source": "/Game/ROD/DataAssets/Items/ItemDataAsset",
  "rows": [ { "key": "1",
              "fields": { "ItemKey": "ItemName_Material_1", "BuyAmount": 10, "...": "..." },
              "text": { "ItemKey": "Iron Ingot" } } ] }
```

- `key`: the row name, map key, or asset name for one-asset-per-row tables like quests.
- `fields`: the row exactly as the game stores it, in CUE4Parse's JSON form. Asset references are `{ "AssetPathName", "SubPathString" }` or `{ "ObjectName", "ObjectPath" }`; enums are `"EType::Value"`.
- `text`: English text for any field holding a game text key, keyed by the field's dotted path (`QuestData.NameKey`).

`<name>.csv` is the same rows flattened: one column per field, nested fields as dotted paths up to three levels, anything deeper or any list of objects as compact JSON, asset references as their path, and a `<field> (text)` column after each text key. UTF-8 with BOM.

### Maps

`export map` writes `DIR/<floor>.json`, `<floor>-cells.csv` and `<floor>-markers.csv`. Floors are `WL01` (Floor 1) and `WL02` (Floor 2). Units are Unreal units (1 = 1 cm); the map is seen from above with X to the right and Y down, which may not match the in-game map's orientation.

```json
{ "formatVersion": 1, "floor": "WL02", "map": "EchoesofAincrad/Content/ROD/Maps/Main/WL02/PL_WL02_WP.umap",
  "layers":  [ { "name": "Forest_2-2", "parent": "Blockout_WorldAreas", "developer": false, "cellCount": 126 } ],
  "cells":   [ { "package": "000TUCP9O1H1H8Q6RJNJTFZIN", "grid": "MainGrid", "level": 1, "x": 7, "y": -5,
                 "center": { "x": 179200, "y": -128000, "z": 0 }, "size": 25600,
                 "contentBounds": { "min": { "x": 0, "y": 0, "z": 0 }, "max": { "x": 0, "y": 0, "z": 0 } },
                 "layers": [ "Forest_2-2" ] } ],
  "markers": [ { "layer": "Measurements", "cell": "021HG6GV9VGTU3RAGU2E895KZ", "kind": "BP_LDBlockout_AreaTool",
                 "label": "Elite Combat 20.0m", "location": { "x": 0, "y": 0, "z": 0 }, "yaw": -122.2, "radiusMeters": 20 } ] }
```

- **layers:** the floor's World Partition data layers. `developer` marks layers the level designers left in the game: annotations (`Measurements`, `GimmickTools`), size references (`Reference`), the blockout (`Blockout_WorldAreas`), legends and labels. Everything else is part of the world, mostly regions such as `Forest_2-2`. `Blockout_WorldAreas` is also the parent of Floor 2's regions, so the game likely keeps it loaded.
- **cells:** every streaming cell. `package` is the cell's file under `<map folder>/PL_<floor>_WP/_Generated_/`. `grid` is `MainGrid` or an HLOD grid (distant low-detail versions). `level` 0 is the finest; each level up doubles `size`, the cell's edge length. `x` and `y` are grid indices at that level (`center / size`). `contentBounds` is what the cell actually contains, or `null`. `layers` is empty for cells that belong to no layer.
- **markers:** the actors in every developer layer's cells. `kind` is the actor's class without `_C`. `label` joins its text labels, or names its mesh when it has none. `radiusMeters` comes from labels ending in a size, like the combat circles, and is `null` otherwise.

The CSVs hold the same cells and markers flat. `layers` in the cells CSV is joined with `;`.

## Building

```
.\build.ps1           # debug build
.\build.ps1 run help  # build and run with arguments
.\build.ps1 test      # run the tests
.\build.ps1 publish   # one self-contained Cardinal.exe in publish\, with README, license and notices
.\build.ps1 release   # publish, then release\Cardinal-VERSION.zip, CardinalMappings-VERSION.zip and SHA256.txt
```

Needs the .NET 10 SDK. `build.ps1` keeps the SDK and every NuGet and .NET cache under `CARDINAL_TOOLS` (default `D:\dev-tools`) and temp files under `CARDINAL_SCRATCH` (default `obj\scratch`); set those to change where they go.

Building mods needs `retoc.exe` and `repak.exe` beside `Cardinal.exe`. The project copies them from `..\tools\retoc\retoc.exe` and `..\tools\repak\repak.exe` (not committed), or from the paths in the `RetocExe` and `RepakExe` MSBuild properties. retoc downloads `oo2core_9_win64.dll` next to itself the first time it runs; `publish` deletes any such copy so it never ships.

`tests/Cardinal.Tests` covers projects, merging, `.locres` reading and writing and the CSV round trip without the game. The rest read your own copy of the game, from Cardinal's saved settings or a `settings.json` named by `CARDINAL_TEST_SETTINGS`, and are skipped when there isn't one: they load every table, write each language's text back byte for byte, read both floors, and build, install and merge real mods. Those mods go into a folder of hard links to the game's containers under the temp folder, never into the game's own `~mods`, so the temp folder has to be on the same drive as the game.

`src/Cardinal.Core` reads the game: settings, the CUE4Parse session, tables, floor maps and the exporters, plus `Mods/` for projects, building, merging, the conflict scan and the game's asset registry (read and extended byte for byte, for adding new primary assets such as towns) (UAssetAPI edits the extracted files, retoc extracts and packs them, repak packs text) and `Text/` for the game's text (Cardinal reads and writes `.locres` files itself). `src/Cardinal` is the window and the command line, and both call the same core code.

## Credits

Built on [CUE4Parse](https://github.com/FabianFG/CUE4Parse), [UAssetAPI](https://github.com/atenfyr/UAssetAPI), [retoc](https://github.com/trumank/retoc) and [repak](https://github.com/trumank/repak). See `THIRD_PARTY_NOTICES.md`.
