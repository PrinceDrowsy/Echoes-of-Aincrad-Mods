# Description

Do you want to make mods for Echoes of Aincrad, but bounce right off of Unreal's tools? Are you tired of digging through thousands of files in FModel just to find out how much a sword costs? (╯°□°)╯ Do you just want to see how the game is put together? The Cardinal System, or Cardinal for short, is a modding tool for Echoes of Aincrad in the spirit of Skyrim's Creation Kit. Right now it lets you browse the game's data in plain, searchable tables, change the values you want, build them into a mod with one button, merge mods that would otherwise fight over the same files, and explore both floors on a map, all from your own copy of the game. ヽ(・∀・)ﾉ

It's the start of something much bigger. The goal is for anyone to be able to make mods for this game without having to learn Unreal first, and every version from here on brings it a step closer. (ง •̀_•́)ง

# Features

- **Data browser:** items, enemies, skills, quests and every other data table in the game, 218 tables in all, shown as plain tables with English names pulled from the game's own text. Search any of them, and export them to CSV or JSON.
- **Editing and one-button mod building:** double-click a value in the tables to change it, and build your changes into an installable mod with one button, or drop it straight into your mods folder. (ﾉ´ヮ`)ﾉ*: ･ﾟ
- **Merging:** mods that change the same game file normally just overwrite each other, so only one of them works. Cardinal keeps your mods as lists of changes instead, and can merge as many as you like into one patch where they ALL work. If two mods set the same value, you decide which one wins by putting them in order. ٩(ˊᗜˋ*)و
- **Conflict checker:** scans every mod you have installed, made with Cardinal or not, and tells you which ones replace the same files and which copy the game will actually load. No more guessing why a mod isn't doing anything!
- **After game updates:** Cardinal can re-check your mods against the new version of the game and tell you if anything they change has moved or disappeared, then rebuild them fresh.
- **Map viewer:** both floors, drawn from the game's own map data, with every region in its own color. Zoom, pan, and hover over any spot to see what's there. You can also light up every part of the map your installed mods replace, colored by which mod wins there.
- **Developer layers:** the game shipped with some of its level designers' planning notes still inside it, like how big each kind of fight is meant to be, where chests go, where the barriers sit, and the color legend for their rough layouts. Tick them on and off in a checklist to see how the floors were planned. (・∀・)
- **Command line:** every export also works without the window, so scripts can pull the game's data straight into your own projects. The file formats are documented in the README.
- **Your game, your files:** Cardinal reads everything from your own copy of the game. It never ships any of the game's files or its encryption key.

# Planned Features

- Outfit fitting for body replacers, in the spirit of BodySlide and Outfit Studio.
- A quest and dialogue editor.
- A location editor for building new places out of the game's own assets.
- A VRoid and VRM pipeline, so you can bring your own hair, outfits and accessories into the game.

# Requirements

- Windows, 64-bit.
- Echoes of Aincrad.
- The game's AES key. Cardinal can't include it (´・ω・`), so you'll need to get it from your own copy with [AESDumpster](https://github.com/GHFear/AESDumpster/releases). Cardinal explains how on its setup screen.
- **Optional:** [UE4SS](https://www.nexusmods.com/echoesofaincrad/mods/7), for the Cardinal Mappings companion mod.

# Installation

1. Download the main file and extract the whole folder wherever you like. Don't run it from inside the zip.
2. Download Cardinal Mappings from the optional files, and put the `CardinalMappings` folder in `EchoesofAincrad/Binaries/Win64/ue4ss/Mods/`.
3. Start the game and wait at the title screen for about 15 seconds. That's when Cardinal Mappings writes the file Cardinal needs to read the game's data. It writes a fresh one by itself after every game update, so you only have to do this once. ( ˘ω˘ )
4. Open `Cardinal.exe` and fill in the setup screen. It finds your game by itself, so all you need to do is paste in your AES key and hit the Oodle download button (or pick your own Oodle DLL, if you have one).
5. Done! Go nuts poking around Aincrad's insides. (ﾉ´ヮ`)ﾉ*: ･ﾟ

If you'd rather not use Cardinal Mappings, you can make the mappings file yourself with UE4SS's `DumpUSMAP()` and pick it on the setup screen instead.

# Spoilers

Cardinal shows EVERYTHING in the game's data, including enemies, quests and areas you might not have reached yet. If you're still on your first playthrough, keep that in mind before you go browsing. (・_・;)

# Compatibility

Cardinal never touches your game files or your saves, so it can't conflict with anything. Cardinal Mappings is a tiny UE4SS mod that does nothing at all unless the game has updated, so it should work alongside any other mod. (￣▽￣)

# Bugs

If a table looks wrong, the map doesn't line up, or Cardinal won't open your game, please let me know through the bug reports tab of this mod page. Screenshots help a ton, and so does the **Copy diagnostic info** button at the bottom of Cardinal's window. It copies your Cardinal and game versions and which mods you have installed, and never your key or anything about your PC. m(_ _)m

# Credits

- [CUE4Parse](https://github.com/FabianFG/CUE4Parse), the library FModel is built on, which does the heavy lifting of reading the game's files. Its license is included with Cardinal.
- [Terrain Inspector](https://www.nexusmods.com/skyrimspecialedition/mods/198117) for Skyrim, which inspired the map viewer.
- eluwave, for the UE4SS build the whole Echoes of Aincrad modding scene runs on.

# License

Cardinal is open source under the MIT license, so take it apart and build on it however you like! ＼(^o^)／
