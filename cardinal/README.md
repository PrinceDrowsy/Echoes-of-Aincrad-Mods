# Cardinal - The Cardinal System

A modding tool for Echoes of Aincrad, in the spirit of Skyrim's Creation Kit. Version 0.3 reads the game, builds data mods from your changes, and merges mods that change the same files.

- **Tables:** browse the game's items, recipes, enemies, skills, quests and every other data table, with English text next to each text key. Search, and export to CSV or JSON.
- **Editing and mod building:** change values right in the tables, keep the changes as a small mod project, and build them into a mod with one button, or straight into the game's `~mods` folder.
- **Merging and conflicts:** combine several mods into one merged patch so they all work, and see which installed mods, made with Cardinal or not, replace the same game files and which copy the game loads.
- **Map:** see each floor from above, cell by cell, colored by region, plus the HLOD grids the game draws in the distance. Switch on the developer layers the level designers left in the game, such as combat area circles with their sizes, treasure chest spots, barriers and size references. Hover for details, and switch on the cells installed mods replace.
- **Command line:** everything the window does, scriptable, for batch work and AI assistants.

Cardinal reads your own copy of the game and never changes the game's own files. Mods it builds are new files in `Paks\~mods`, so deleting them undoes them. It doesn't contain any of the game's files.

## Making a mod

1. Open a table and double-click a value. Numbers, text, true or false, and choices can be changed; lists and groups can't yet. Cardinal checks each value against the game file before taking it, and changed values turn yellow.
2. Every change collects in the **Mod** tab, with its original value. Select changes there to revert them, or type the original value back into the table.
3. Give the mod a name, version, author and priority, and **Save project**. The project is a small JSON file holding only your changes (see below), so it can be rebuilt after a game update.
4. **Build mod into a folder** or **Build and install into ~mods**. Cardinal pulls fresh copies of the files it changes from the unmodded game, applies your changes, and packs them into `<Name>_<priority>_P.utoc`, `.ucas` and `.pak`.

Builds always start from the unmodded game, never from other installed mods.

After a game update, **Check against the game** lists changes that can't be made any more, because a table, row or field is gone, and changes whose original value the update changed. Building again is all a rebuild needs, since every build starts fresh.

Some tables are read-only for now: the enemy blueprint list and quests (Cardinal can't write those back yet), and any table whose file can't be decoded with your mappings. The Tables tab says why. Mappings written for your exact game version, by Cardinal Mappings, usually fix the second kind.

## Mods that change the same files

A mod replaces whole game files, so two mods that change the same file can't both work: the game loads one copy and ignores the other. Which one it loads is decided by the number before `_P` in the file name, the patch priority. Higher wins, equal priorities are left to chance, and a name ending in just `_P` ranks the same as the game's own update (`pakchunk0-WindowsClient_0_P`), so that's left to chance too.

- **Priorities:** a project's `priority` goes from 1 to 9998, default 1000, and becomes part of the file name, as in `MyMod_1000_P`. Load order is just priorities: give the mod that should win the higher number.
- **Merging (Merge tab):** add the projects in order and build one `CardinalMerged_9999_P` patch from all their changes. Where two set the same value, the later one in the list wins, and the tab lists every such value. 9999 is kept for the merged patch so it outranks every mod it was made from. When installing it, Cardinal offers to remove those mods' own builds from `~mods`, since the merged patch has all their changes. The list is saved with your settings.
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
Cardinal.exe set PROJECT.json TABLE ROW FIELD VALUE
Cardinal.exe build PROJECT.json --out DIR
Cardinal.exe build PROJECT.json --install [--force]
Cardinal.exe check PROJECT.json
Cardinal.exe merge A.json B.json [C.json...] --out DIR
Cardinal.exe merge A.json B.json [C.json...] --install [--force] [--remove-single-builds]
Cardinal.exe conflicts
Cardinal.exe diagnostics
Cardinal.exe help
```

- `list tables` prints each table's id, source and `editable` or `read-only`.
- `set` checks one change against the game file and saves it in the project, creating the project if it doesn't exist. `FIELD` is the column name as the table shows it, such as `CoolTime` or `QuestData.Floor`. Setting a field back to its original value removes the change. Example: `Cardinal.exe set MyMod.json Skills/DT_ActiveSkillList 0 CoolTime 2.5`.
- `build` checks every change first and builds nothing if any is wrong. `--install` builds straight into `Paks\~mods` and refuses to replace an earlier build of the same mod unless you add `--force`.
- `check` re-checks every change against the current game. Each problem line starts with `can't build` or `changed` (the original value is different now; the change still applies).
- `merge` prints every value more than one project sets and which wins, then builds `CardinalMerged_9999_P`. `--remove-single-builds` deletes the merged projects' own builds from `~mods`.
- `conflicts` prints a `mod` line per installed mod and a `conflict` line per game file, tab-separated, with what the game loads.
- `diagnostics` prints the same report as the window's **Copy diagnostic info**.
- `set`, `check`, `build` and `merge` always work from the unmodded game and refuse `--with-mods`.

Output goes to stdout, errors to stderr. Exit codes:

| Code | Meaning |
|---|---|
| 0 | Done |
| 1 | Bad arguments, unknown command, table or row, or a change that can't be made (wrong type, read-only table) |
| 2 | Setup missing or wrong (paks, key, mappings, Oodle, retoc), or the files didn't open with that key |
| 3 | Something couldn't be found or read in the game files. `export tables` still writes the rest and lists failures in `tables.json` |
| 4 | Writing the output failed, or `--install` found an earlier build without `--force` |
| 5 | `check` or `conflicts` found something to look at |

## Mod projects

A project is a small JSON file holding only the changes. Field names are stable within a `formatVersion`; this is version 1.

```json
{ "formatVersion": 1, "name": "FasterRecovery", "version": "1.0.0", "author": "", "description": "", "priority": 1000,
  "edits": [ { "table": "Skills/DT_ActiveSkillList", "row": "0", "field": "CoolTime", "value": "2.5", "original": "5" } ] }
```

- `table`: a table id from `list tables`. `row`: the row key. `field`: the column name.
- `value`: the new value as text. Enum values can be written with or without their type, as `Crystal02` or `EDropGimmick::Crystal02`.
- `original`: the unmodded value when the change was made, for reference. Builds don't need it.
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
.\build.ps1 publish   # one self-contained Cardinal.exe in publish\, with README, license and notices
```

Needs the .NET 10 SDK. `build.ps1` keeps the SDK and every NuGet and .NET cache under `CARDINAL_TOOLS` (default `D:\dev-tools`) and temp files under `CARDINAL_SCRATCH` (default `obj\scratch`); set those to change where they go.

Building mods needs `retoc.exe` beside `Cardinal.exe`. The project copies it from `..\tools\retoc\retoc.exe` (not committed), or from the path in the `RetocExe` MSBuild property. retoc downloads `oo2core_9_win64.dll` next to itself the first time it runs; `publish` deletes any such copy so it never ships.

`src/Cardinal.Core` reads the game: settings, the CUE4Parse session, tables, floor maps and the exporters, plus `Mods/` for projects, building, merging and the conflict scan (UAssetAPI edits the extracted files, retoc extracts and packs them). `src/Cardinal` is the window and the command line, and both call the same core code.

## Credits

Built on [CUE4Parse](https://github.com/FabianFG/CUE4Parse), [UAssetAPI](https://github.com/atenfyr/UAssetAPI) and [retoc](https://github.com/trumank/retoc). See `THIRD_PARTY_NOTICES.md`.
