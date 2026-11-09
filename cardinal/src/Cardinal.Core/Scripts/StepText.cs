using System.Text.RegularExpressions;
using Cardinal.Text;
using Newtonsoft.Json.Linq;

namespace Cardinal.Scripts;

/// <summary>
/// A step told in a sentence, for people who don't read the game's classes. Staging is everything that only moves the
/// camera and the actors (timers, animations, looks, fades); the rest is the story itself: talk, choices, quests, items.
/// </summary>
public sealed record StepSentence(string Text, bool Staging);

/// <summary>Turns a script's steps into sentences in one language, with dialogue and names looked up in the game's text.</summary>
public sealed partial class StepText(Game game, Locres language)
{
    // Every line of the language by key; event dialogue lives in one string table per event.
    readonly Lazy<Dictionary<string, string>> lines = new(() =>
        language.Lines.GroupBy(l => l.Key).ToDictionary(g => g.Key, g => g.First().Text));

    static readonly HashSet<string> StagingKinds =
    [
        "Reroute", "RODTimer", "PlayAnimation", "LookAt", "ChangeViewTarget", "SetEmotion", "SetActorVisible",
        "SetActorTransform", "SetActorTransformTarget", "SetStandingPreset", "SetCastCharacterMode", "SetCharaLighting",
        "MoveCamera", "AttachSkeletalMesh", "DetachMesh", "ToggleOpacity", "AttachEquipment", "CameraShake",
        "ChangePlayerCamera", "PlayAkEvent", "WidgetFade", "StopAI", "PlayVoice", "CameraFade", "CinematicMode",
        "SpawnCastCharacter", "DestroyCastCharacter", "SpawnMobNPCs", "DestroyMobNPCs", "ExecutionSequence",
        "AddRestrictOperation", "RemoveRestrictOperation", "AdaptGoalMarker", "TestDialog (Blueprint)", "HideScreen2D",
        "ShowScreen2D", "IDGimmickNotify", "GimmickNotify", "DisablePartnerGuide", "CallOwnerUFunc",
    ];

    // What each CinematicMode switch does when it's turned off, and when it's turned back on.
    static readonly (string Field, string Off, string On)[] CinematicSwitches =
    [
        ("CharVisibility", "characters hidden", "characters shown"),
        ("HUDVisibility", "HUD hidden", "HUD shown"),
        ("PlayerInput", "your controls locked", "your controls back"),
        ("EnemyAnimalAI", "enemies and animals paused", "enemies and animals moving again"),
    ];

    /// <summary>
    /// Every step of the script told in a sentence, by step id. A scene often calls someone by a title ("Fencer") before
    /// the story says who they are, so unless names is set a cast member shows as their number until the subtitles first
    /// show their name as a speaker, and by name from that step on.
    /// </summary>
    public IReadOnlyDictionary<string, StepSentence> Describe(Script script, bool names)
    {
        var named = NamedAt(script);
        return script.Steps.Select((s, i) => (s, i)).ToDictionary(p => p.s.Id, p => Describe(p.s, id =>
            Name(id) is { } name && (names || named.TryGetValue(name, out var at) && p.i >= at) ? $"{name} ({id})" : $"character {id}"));
    }

    // Where, in story order, each name first appears as a speaker in the subtitles. A cutscene's lines come from the
    // event's text, which can't be split between several cutscenes, so they count from the last one.
    Dictionary<string, int> NamedAt(Script script)
    {
        var named = new Dictionary<string, int>();
        var lastCutscene = script.Steps.ToList().FindLastIndex(s => s.Kind.Contains("LevelSequence"));
        if (lastCutscene >= 0)
            foreach (var line in EventLines(script))
                if (Speaker(line) is { } speaker)
                    named.TryAdd(speaker, lastCutscene);
        for (var i = 0; i < script.Steps.Count; i++)
            if (Script.KindName(script.Steps[i].Kind) == "Talk (Blueprint)" && Speaker(Line((string?)script.Steps[i].Fields["TextID"])) is { } speaker)
                named[speaker] = Math.Min(named.GetValueOrDefault(speaker, i), i);
        return named;
    }

    // The speaker label a line of dialogue starts with, as in "Stella: ...". A name only mentioned in a line doesn't count.
    static string? Speaker(string? line) =>
        line?.IndexOf(':') is > 0 and < 40 and var colon && !line[..colon].Contains('\n') ? line[..colon].Trim() : null;

    StepSentence Describe(ScriptStep step, Func<string, string> Character)
    {
        var kind = Script.KindName(step.Kind);
        var f = step.Fields;
        string Who(JToken? target) => (Enum(target?["Target"]), (string?)target?["ID"]) switch
        {
            ("Hero", _) => "You",
            ("Partner", _) => "Your partner",
            (_, "Speaker") => "The speaker",
            (_, { } id) => Character(id),
            _ => "someone",
        };

        var text = kind switch
        {
            "Start" => "The script starts.",
            "Finish" => "The script ends.",
            "Reroute" => "(connector)",
            "RODTimer" => "Wait a moment.",
            "Talk (Blueprint)" => Line((string?)f["TextID"]) ?? $"{Who(f["Target"])} talks.",
            "TalkChat" => "Chat message: " + (Line((string?)f["TextID"]) ?? "…"),
            "Choice (Blueprint)" => $"Choice: {Line((string?)f["MainText"]?["Key"])} " +
                                    string.Join(" / ", (f["ChoiceText"] as JArray ?? []).Select(c => $"[{Line((string?)c["Key"])}]")),
            "SetMainStory" => $"The main story moves to step {f["NewProgress"]}.",
            "SetSubStory" => $"Side story {f["ID"]} moves to step {f["NewProgress"]}.",
            "CheckMainStory" => $"Depending on whether the main story is before, at or past step {f["CheckProgress"]}:",
            "ReceiveQuest" => $"You receive quest {Join(f["QuestIDs"])}.",
            "UpdateQuestProgress" => $"The quest's progress moves on ({f["Message"]}).",
            "QuestSuccess" => "The quest succeeds.",
            "QuestAborted" => "The quest is abandoned.",
            "QuestResult" => "The quest's result is shown.",
            "IsClearQuest" => "Depending on whether the quest is cleared:",
            "BranchPartner" => $"Depending on your partner ({Join(f["PartnerList"])}):",
            "LogicalAND" => "Once all of these have happened:",
            "TakeItem" => $"You get {Item(f["Item"])}.",
            "RemoveItem" => $"{Item(f["Item"])} is taken from you.",
            "TutorialGuide" => $"Tutorial {Tutorial(f)} is added to the Tutorials tab" +
                               ((bool?)f["bShow"] == true ? ", with a hint" : "") +
                               ((bool?)f["bShowInfoMessage"] == true ? " and a message" : "") + ".",
            "ShowTutorialWindow" => $"Tutorial {Tutorial(f)} pops up in a window.",
            "Announce" => "Announcement: " + (Line((string?)f["Key"]) ?? (string?)f["Key"]),
            "StartGeneralEvent" => $"Event {AssetName(f["Asset"])} plays.",
            "RODSubGraph" => $"Script {AssetName(f["Asset"])} runs.",
            "RODPlayLevelSequence" or "PlayOperatedLevelSequence" => $"Cutscene {(AssetName(f["Sequence"]) is { Length: > 0 } name ? name : "(unnamed)")} plays.",
            "PlayStageAnchorLevelSequence" => $"Cutscene {f["StageAnchorID"]} plays.",
            "SpawnBattleCharacter" => $"Your party joins you: {Join(new JArray(f.Properties().Where(p => p.Name.StartsWith("PartnerID")).Select(p => p.Value)))}.",
            "EnemyDefeatListener" => $"Wait until {f["FinishDeadEnemyCount"] ?? 1} of {Join(f["EnemyLocatorIDs"])} are defeated.",
            "ElapseWorldTime" => $"Time passes until {Enum(f["TimeZone"])}.",
            "ChangeLevel" => $"You travel to {Enum(f["Type"]) ?? "the character creator"}" + (f["QuestId"] is { } quest ? $" for quest {quest}" : "") + ".",
            "CameraFade" => ((bool?)f["bFadeOut"] == true ? "The screen fades out" : "The screen fades in")
                            + ((string?)f["Color"]?["Hex"] is "FFFFFF" ? " to white" : "")
                            + (f["Duration"] is { } seconds ? $" over {seconds} s" : "") + ".",
            "CinematicMode" => "Cutscene mode: " + string.Join(", ", CinematicSwitches
                .Where(s => f[s.Field] is not null)
                .Select(s => Enum(f[s.Field]) == "Disable" ? s.Off : s.On)) + ".",
            "SpawnCastCharacter" => $"{Character(f["ID"]!.ToString())} appears.",
            "DestroyCastCharacter" => $"{Character(f["ID"]!.ToString())} leaves.",
            "PlayAnimation" => $"{Who(f["Target"])} moves ({AssetName(f["Montage"])}).",
            "LookAt" => $"{Who(f["Target"])} looks at {Object(Who(f["LookAtTarget"]))}.",
            "SetEmotion" => $"{Who(f["Target"])} looks {Enum(f["Emotion"])?.TrimEnd('0', '1', '2', '3').ToLowerInvariant()}.",
            "ChangeViewTarget" or "MoveCamera" => $"The camera turns to {Who(f["TargetInfo"])}.",
            "AddRestrictOperation" => "Your controls are limited.",
            "RemoveRestrictOperation" => "Your controls are back.",
            "IDGimmickNotify" => $"Object {f["ID"]} is told \"{f["Message"]}\".",
            "GimmickNotify" => $"The event's object is told \"{f["Message"]}\".",
            _ => null,
        };
        if (!step.Reachable)
            text = $"(Nothing in this script leads here.) {text}";
        return new StepSentence(text ?? $"{kind}: {Script.Settings(step)}", StagingKinds.Contains(kind));
    }

    readonly Dictionary<string, string?> characters = [];

    // A cast number is a character's own number: NPCData_<number> defines the character, and a partner's entry names
    // its partner code, whose PartnerName_<code> is the name the game shows. Only that chain names anyone; guessing from
    // dialogue would be wrong (a talk step's target can be the listener). Extras and partners without a name stay numbers,
    // and the number always shows, so the name can be checked.
    string? Name(string id)
    {
        if (characters.TryGetValue(id, out var known))
            return known;
        var file = game.Provider.Files.Keys.FirstOrDefault(k =>
            k.Contains("/NPC/Data/", StringComparison.OrdinalIgnoreCase) && k.EndsWith($"/NPCData_{id}.uasset", StringComparison.OrdinalIgnoreCase));
        string? name = null;
        if (file is not null)
        {
            var appearance = game.Package(file).GetExports()
                .Select(e => JObject.FromObject(e)["Properties"]?["AppearanceData"])
                .FirstOrDefault(a => a is not null);
            if ((bool?)appearance?["bPartner"] == true && (string?)appearance["PartnerID"] is { Length: > 0 } code)
                name = Line("PartnerName_" + code);
        }
        return characters[id] = name;
    }

    /// <summary>
    /// The lines in the script's own event text, in their numbered order. A cutscene's dialogue lives in its sequence,
    /// not in the script, but its lines are in the event's string table under the script's name (Main_cev_09_02_01_...).
    /// </summary>
    public IReadOnlyList<string> EventLines(Script script)
    {
        var prefix = "Main_" + script.Source.Name.Replace("Flow_", "") + "_";
        return lines.Value.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(k => Line(k)!)
            .ToList();
    }

    /// <summary>A line of the game's text with its {Rep_...} names filled in, as the game does when it shows it.</summary>
    public string? Line(string? key)
    {
        if (key is null || !lines.Value.TryGetValue(key, out var text))
            return null;
        return Replacement().Replace(text, m => lines.Value.GetValueOrDefault(m.Groups[1].Value) ?? m.Value);
    }

    [GeneratedRegex(@"\{Rep_([A-Za-z0-9_]+)\}")]
    private static partial Regex Replacement();

    static string Tutorial(JObject f) =>
        f["Params"] is JArray { Count: > 0 } list && (bool?)f["bUseArray"] == true
            ? string.Join(", ", list.Select(p => $"{Enum(p["Category"])} {p["ID"]}"))
            : $"{Enum(f["TutorialCategory"])} {f["TutorialID"]}";

    static string Item(JToken? item) => item is null ? "an item" : $"item {Enum(item["Category"])?.Replace("ItemCategory_", "")} {item["ItemId"]}";

    // "You" and "The speaker" start a sentence; after a verb they read "you" and "the speaker". Names stay as they are.
    static string Object(string who) => who is "You" || who.StartsWith("Your ") || who.StartsWith("The ")
        ? char.ToLowerInvariant(who[0]) + who[1..]
        : who;

    static string Join(JToken? list) => list is JArray array ? string.Join(", ", array.Select(v => v.ToString())) : list?.ToString() ?? "";

    static string? Enum(JToken? value) => (string?)value is { } text ? text[(text.LastIndexOf(':') + 1)..] : null;

    static string AssetName(JToken? asset)
    {
        var path = (string?)asset?["ObjectPath"] ?? (string?)asset?["AssetPathName"] ?? "";
        return path[(path.LastIndexOf('/') + 1)..].Split('.')[0];
    }

    /// <summary>Loads the language's text once; dialogue is looked up in it as steps are described.</summary>
    public static StepText For(Game game, string culture) => new(game, GameText.Load(game, culture));
}
