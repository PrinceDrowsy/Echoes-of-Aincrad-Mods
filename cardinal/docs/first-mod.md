# Your first mod

Never made a mod before? Made a few for other games, but Unreal scares you off? Perfect, this is for you! (๑•̀ㅂ•́)و✧ In about ten minutes you'll make a real mod for Echoes of Aincrad, install it, see it working in game, and take it back out again. No Unreal, no code, no scary file juggling.

The mod we're making is a tiny one: it makes the Recovery skill come back faster, so its cooldown goes from 5 seconds to 1. It's harmless, it's easy to spot in game, and it touches everything you need to know to make bigger mods later.

## Before you start

You'll need Cardinal set up, with your AES key, mappings and Oodle all filled in. If you haven't done that yet, the installation steps on the mod page walk you through it. Come back when the setup screen stops complaining. ( ˘ω˘ )

Also, close the game if it's running. Mods only get picked up when the game starts.

## 1. Find the value

1. Open Cardinal and go to the **Tables** tab, the data browser.
2. In the table list, open **Skills**, then **DT_ActiveSkillList**. This is the table for your active skills.
3. Find the row named **Recovery**. You can type it into the search box if you don't feel like scrolling.
4. Look along that row for the **CoolTime** column. It says **5**. That's the cooldown in seconds.

Every number you see in the Tables tab comes straight from your own copy of the game, so this is exactly what the game is using right now. (・∀・)

## 2. Change it

1. Double-click the **5** in the CoolTime column.
2. Type **1** and press Enter.

The cell turns yellow. That's Cardinal telling you "this is changed from the original". If you typed something that doesn't fit, like a word where a number should go, Cardinal says so and leaves the value alone, so you can't break anything by accident.

Changed your mind? Type the original value back in and the change disappears. (￣▽￣)

## 3. Name your mod

1. Open the **Mod** tab.
2. Give your mod a name, like **Quick Recovery**, a version like **1.0**, and your name as the author.
3. Down below you'll see your change listed, with the old value next to the new one. That list IS your mod. Cardinal only keeps track of what you changed, not a whole copy of the game's files, which is why your mods stay tiny and can be rebuilt after game updates.
4. Hit **Save project** and put the project file somewhere you'll find it again. This is the file you open next time you want to work on the mod.

## 4. Build and install it

Hit **Build and install into ~mods**. Cardinal pulls a fresh copy of the skill table out of the game, applies your change, packs it into a mod, and puts it in `Paks\~mods` in your game folder. It only takes a few seconds.

The first time you build, Cardinal needs the internet for a moment to fetch a file it uses for packing. After that it works offline.

## 5. Try it in game

Start the game, load your character, and use Recovery. Instead of waiting 5 seconds, it's ready again after 1. That's your mod, running in the game! ヽ(・∀・)ﾉ

## 6. Take it back out

Mods made with Cardinal never touch your save, so removing one is always safe.

1. Close the game.
2. Open `EchoesofAincrad\Content\Paks\~mods` in your game folder.
3. Delete the three files whose names start with your mod's name (one `.pak`, one `.utoc`, one `.ucas`).

Next time you start the game, Recovery is back to 5 seconds. Your project file is still where you saved it, so you can rebuild the mod any time. :3

## Where to go from here

You now know the whole loop: find a value, change it, build, test, remove. Every data mod is that same loop, just with more changes. Some ideas to try next:

- Tweak a weapon's stats.
- Change how much an item sells for.
- Make a few skills cheaper to use.

If two of your mods change the same table, don't panic! Cardinal can merge them into one, so both sets of changes work together. And if you get stuck, or something doesn't do what this guide says, let me know through the bug reports tab on the mod page. Screenshots help a ton. m(_ _)m

Have fun, and welcome to modding Aincrad! ٩(◕‿◕)۶

Please don't use your new powers to trap us all in a death game thankies <3 <3
