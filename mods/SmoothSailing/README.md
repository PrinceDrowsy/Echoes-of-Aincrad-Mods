# Smooth Sailing

Small fixes that take chores out of Echoes of Aincrad without changing its challenge. Each one has its own setting, so you can keep the ones you like.

## What it changes

**Shorter Death Game warning.** The first time you choose Death Game mode, you get the game's full warning, but it scrolls three times faster. Once you've read it, later characters get a short version instead: the game's own "Are you sure you wish to play on Death Game mode?" line and the warning box about lost saves and system stability, with no long tips to scroll through. The choice still can't be undone, so the important part stays.

**Skip notices you've read.** The autosave notice at every startup and the "Enter the player character's name" notice before every new character's name entry get closed for you, once you've closed each of them yourself. The autosave icon in the corner stays, since it's the actual warning not to quit mid-save.

Everything shown is the game's own text, so it works in every language the game has.

The mod remembers what you've read in its own folder, in `read.txt`. Delete that file to see everything again. A fresh install shows them again too.

More fixes are planned.

## Requirements

- [UE4SS for Echoes of Aincrad](https://www.nexusmods.com/echoesofaincrad/mods/7)
- Optional: [Mod Menu](https://www.nexusmods.com/echoesofaincrad/mods/84), to change the settings in game

## Installing

Copy the `SmoothSailing` folder into `EchoesofAincrad/Binaries/Win64/ue4ss/Mods/` in your game folder.

## Settings

With Mod Menu, the settings change live, no restart needed. Without it, edit `SmoothSailing/Scripts/config.lua`.

- **Smooth Sailing on** (`ENABLED`, on by default). Turns every change off at once.
- **Short Death Game warning once read** (`SHORT_DEATH_GAME_WARNING`, on by default).
- **Skip notices you've read** (`SKIP_READ_NOTICES`, on by default).
- **Death Game warning scroll speed** (`WARNING_SCROLL_SPEED`, 60 by default). The game's own speed is 20.
- **Extra logging** (`DEBUG_LOGS`, off by default). Only useful for bug reports.

## Compatibility

It only changes these windows while they're open, so it should work alongside most mods. If a game update changes the warning window, the mod turns itself off and says so in the UE4SS log, and you get the full warning as usual. A notice that doesn't close is left for you to close.

## Uninstalling

Delete the `SmoothSailing` folder. It never writes to your save, so removing it is safe at any point.

## License

MIT. The source is on GitHub.
