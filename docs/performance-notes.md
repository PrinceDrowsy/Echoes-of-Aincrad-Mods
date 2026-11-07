# Performance notes

Game 1.0.6.0, with No-Intro, UE4SS, Beta Grace and DevTools installed. Recorded with `dev.ps1 bench`.

## Save loads are bound by system memory

On a machine short on system memory, loading a save took somewhere between five and eleven minutes, or crashed partway. While a save loads, the game's committed memory climbs by about 8 GB in under two minutes, to around 17 GB. When the system runs out of room for that, the game exits with "Out of video memory trying to allocate a rendering resource", even though video memory still has plenty free. That message can mean system memory, not the graphics card.

Alt-tabbing during the load isn't the cause: the crash happened without it.

With more system memory on the same machine, mods and save, the same load took about a minute and a half and didn't crash. The game's commit peaked lower, around 14 GB, and disk reads ran about three times faster, so much of the slow load was the system paging.

For System Assist, load time is mostly a memory and disk question. Lighter memory use during the load would help players with less RAM the most.

## Video memory

Once in game, video memory use gets high enough to matter on cards with less memory. Worth watching as mods add assets, for System Assist.

## Measuring

Press the first `Ctrl+F12` mark right as the load is chosen, not on reaching the title screen, so the two marks bracket the load alone.
