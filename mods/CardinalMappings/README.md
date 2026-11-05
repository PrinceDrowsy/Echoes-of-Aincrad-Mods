# Cardinal Mappings

A companion mod for Cardinal. It writes the game's mappings file (`.usmap`), which Cardinal needs to read the game's data, and writes a new one after each game update. It contains no game files.

## Requirements

- [UE4SS for Echoes of Aincrad](https://www.nexusmods.com/echoesofaincrad/mods/7)

## Installing

Copy the `CardinalMappings` folder into `EchoesofAincrad/Binaries/Win64/ue4ss/Mods/` in your game folder. Start the game and wait at the title screen for about 15 seconds. The UE4SS log says "wrote new mappings for Cardinal" when it's done, and Cardinal picks up the file the next time it starts.

On later launches it does nothing unless the game has updated. To force a new file, delete `dumped.txt` in the mod's folder.

## Uninstalling

Delete the `CardinalMappings` folder. It never touches your save.

## License

MIT.
