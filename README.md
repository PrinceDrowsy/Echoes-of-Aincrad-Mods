# Echoes of Aincrad modding

Mods and modding tools for Echoes of Aincrad (Steam, UE 5.3.2 custom branch, Denuvo). Never patch the exe; all changes happen through paks or at runtime.

## Local layout

None of these folders are committed. They hold third-party binaries, the AES key and data derived from the game.

- `tools/` - retoc, repak, FModel, UAssetGUI, UEVR, AESDumpster and the UE4SS build. The key is in `tools/_private/aes-key.txt`.
- `dumps/` - per-build `.usmap`, UE4SS object dump, UHT headers and the full asset list.
- `backups/` - saves and config from before modding started.

## Testing

`dev.ps1` deploys mods, snapshots and restores saves, launches the game and keeps old UE4SS logs. The header of the script lists its commands. Launch with `.\dev.ps1 play`, since launching from Steam directly overwrites the last log.

No-Intro (Nexus mod 3) stays in `~mods` during testing to skip the start-up logos.

`mods/DevTools` adds hotkeys for throwaway characters. Output goes to the UE4SS log.

| Key | Action |
|---|---|
| Ctrl+F5 | Log quest, Death Game and map state, and the received quests |
| Ctrl+F6 | Log `CanSwitch` and call `PlaySwitchAction` if it's allowed |
| Ctrl+F7 | Show a test chat message with custom text |
| Ctrl+F8 | Start the quest set in `START_QUEST` |
| Ctrl+F9 | Complete the current quest |
| Ctrl+F10 | Log body values and step the belly morph through test values, live meshes only |
| Ctrl+F11 | The same for belly, butt, hips, thighs, arms, forearms and legs together |
| Ctrl+F12 | Log a mark. `dev.ps1 bench` reports the time between the first two as the load time |
| Ctrl+Shift+1 | Add 10,000 Col |
| Ctrl+Shift+2 | Give the item set in `GIVE_ITEM` (category number and ID, as Cardinal shows them) |
| Ctrl+Shift+3 | Add 5,000 experience |
| Ctrl+Shift+4 | Toggle noclip, if the game still has its cheat manager |
| Ctrl+Shift+5 | Receive the quest set in `GIVE_QUEST`, as if an NPC handed it out (a `QST_Free_` number) |
| Ctrl+Shift+6 | Jump the main story to the step in `SET_STORY`. Throwaway saves only |
| Ctrl+Shift+7 | Release outfit dyeing before its side story does. Open the equipment menu after, not before |
| Ctrl+Shift+8 | Set the side story in `SET_SIDE_STORY` to its progress step, as a quest giver's conversation would. Throwaway saves only |

The cheats write to the save and are for solo play only, since the game has anti-cheat for online play.

`shared/` holds code every mod uses. `dev.ps1 deploy` copies it into the mod's `Scripts` folder, so each mod installs on its own.

## Docs

- `docs/performance-notes.md` - performance measurements

## Game install

UE4SS is eluwave's build from Nexus (mod 7), the one the rest of the scene targets. The official build is reported to fail its startup scan on this game (RE-UE4SS#1283). Its LoadMap hook is deliberately disabled. To remove it, delete `dwmapi.dll` and `ue4ss/` from `EchoesofAincrad/Binaries/Win64`. Pak mods go in `EchoesofAincrad/Content/Paks/~mods`.

## After a game update

Re-run AESDumpster in case the key changed, then regenerate the mappings and dumps into a new `dumps/` folder. Calling `DumpUSMAP()` and `GenerateUHTCompatibleHeaders()` from a UE4SS Lua mod produces them.

## FModel

Set the game directory to `EchoesofAincrad/Content/Paks` and the UE version to `GAME_UE5_3`, add the AES key, and point the local mapping file setting at the `.usmap` in `dumps/`.
