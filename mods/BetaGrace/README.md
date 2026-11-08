# Beta Grace

In the books, the death game starts when the beta test ends. Beta Grace brings that to Echoes of Aincrad: in Death Game mode, dying during the beta portion of the story won't delete your save. Once the beta is over, Death Game works exactly as normal.

## How it works

The game already keeps a list of quests where a death doesn't count for Death Game. Beta Grace adds the beta quests to that list while the game runs, so a death there restarts the quest instead of ending your run. Your save keeps its Death Game status the whole time.

It reads which quests belong to the beta from the game itself rather than a fixed list, so it should keep working after game updates. If an update changes things too much, it turns itself off and says so in the UE4SS log, instead of guessing.

## Requirements

- [UE4SS for Echoes of Aincrad](https://www.nexusmods.com/echoesofaincrad/mods/7)
- Optional: [Mod Menu](https://www.nexusmods.com/echoesofaincrad/mods/84), to switch the protection on and off in game

## Installing

Copy the `BetaGrace` folder into `EchoesofAincrad/Binaries/Win64/ue4ss/Mods/` in your game folder.

## Settings

With Mod Menu, both settings change live, no restart needed. Without it, edit `BetaGrace/Scripts/config.lua`.

- **Protect Death Game saves during the beta** (`ENABLED`, on by default). Turn it off to take the risk back.
- **Extra logging** (`DEBUG_LOGS`, off by default). Only useful for bug reports.

## Compatibility

It only changes one list in the game's settings while the game runs, so it should work alongside most mods. Seamless Aincrad stays off during the beta, so the two don't overlap. A mod that opens the field during the beta would need more: a death outside a quest there isn't covered.

## Uninstalling

Delete the `BetaGrace` folder. Beta Grace never writes to your save, so removing it is safe at any point. From then on, a death during the beta deletes a Death Game save again, as in the unmodded game.

## License

MIT. The source is on GitHub.
