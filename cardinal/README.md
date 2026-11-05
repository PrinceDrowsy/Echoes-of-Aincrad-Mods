# Cardinal - The Cardinal System

A modding tool for Echoes of Aincrad, in the spirit of Skyrim's Creation Kit. Version 0.1 reads the game; editing and building mods come later.

- **Tables:** browse the game's items, recipes, enemies, skills, quests and every other data table, with English text next to each text key. Search, and export to CSV or JSON.
- **Map:** see each floor from above, cell by cell, colored by region. Switch on the developer layers the level designers left in the game, such as combat area circles with their sizes, treasure chest spots, barriers and size references. Hover for details.
- **Command line:** everything the window does, scriptable, for batch work and AI assistants.

Cardinal only reads your own copy of the game. It never changes the game's files, and it doesn't contain any of them.

## Setup

Run `Cardinal.exe`. The Setup tab asks for four things, saved in `%APPDATA%\Cardinal\settings.json`:

1. **Game Paks folder:** `EchoesofAincrad\Content\Paks` in the game's install folder. **Detect** finds it in your Steam libraries.
2. **AES key:** the game's files are encrypted, and Cardinal can't include the key. Run [AESDumpster](https://github.com/GHFear/AESDumpster) on `EchoesofAincrad-Win64-Shipping.exe` and paste the key it prints.
3. **Mappings (.usmap):** describes the game's data layout. The easiest way is the Cardinal Mappings companion mod, an optional download on the same page: install it with UE4SS, start the game once and wait at the title screen for about 15 seconds, and Cardinal finds the file by itself. It writes new mappings after each game update. You can also make one by hand with UE4SS's `DumpUSMAP()` and choose it here.
4. **Oodle library:** the files are compressed with Oodle, which Cardinal can't ship. **Download** fetches it from the [OodleUE](https://github.com/WorkingRobot/OodleUE) builds, as FModel does, or choose an `oo2core_9_win64.dll` or `oodle-data-shared.dll` you already have.

Then press **Save and open the game files**.

## Command line

Any arguments run Cardinal without the window. Commands use the saved settings; `--paks`, `--key`, `--usmap` and `--oodle` override them for one run.

```
Cardinal.exe setup [--paks DIR] [--key HEX] [--usmap FILE] [--oodle FILE] [--download-oodle]
Cardinal.exe list tables
Cardinal.exe list floors
Cardinal.exe list layers WL02
Cardinal.exe export tables --out DIR [--only Items/MaterialItem,Skills/DT_ActiveSkillList]
Cardinal.exe export map WL02|all --out DIR
Cardinal.exe help
```

Output goes to stdout, errors to stderr. Exit codes:

| Code | Meaning |
|---|---|
| 0 | Done |
| 1 | Bad arguments, unknown command or unknown table |
| 2 | Setup missing or wrong (paks, key, mappings, Oodle), or the files didn't open with that key |
| 3 | Something couldn't be found or read in the game files. `export tables` still writes the rest and lists failures in `tables.json` |
| 4 | Writing the output failed |

## Export formats

Field names are stable within a `formatVersion`. Cardinal 0.1 writes version 1.

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

`src/Cardinal.Core` reads the game: settings, the CUE4Parse session, tables, floor maps and the exporters. `src/Cardinal` is the window and the command line, and both call the same core code.

## Credits

Built on [CUE4Parse](https://github.com/FabianFG/CUE4Parse). See `THIRD_PARTY_NOTICES.md`.
