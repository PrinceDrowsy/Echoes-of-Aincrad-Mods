using System.Globalization;
using System.Text.RegularExpressions;
using Cardinal.Tables;
using Cardinal.Text;
using Newtonsoft.Json.Linq;
using UAssetAPI.Unversioned;

namespace Cardinal.Mods;

/// <summary>A new item: its kind (an item table such as Items/HealItem), the item it starts as a copy of, its ID and its text.</summary>
public sealed record NewItem(string Kind, string From, int Id, string Name, string Description)
{
    /// <summary>Names and descriptions in other languages, by language code. Languages left out show the English.</summary>
    public Dictionary<string, (string Name, string Description)> Translations { get; init; } = [];
}

/// <summary>
/// The "new item" template. An item is a row in one of the item asset's maps, keyed by its ID, with text keys for its
/// name and description. The new row copies an existing item, so it keeps that item's icon, model, rarity and prices
/// until they're changed, and the text goes in as new keys of the game's main string table.
/// </summary>
public static partial class ItemTemplate
{
    /// <summary>The item kinds a new item can be: one table per kind in the item asset, such as Items/HealItem.</summary>
    public static IReadOnlyList<TableSource> Kinds(IEnumerable<TableSource> sources) =>
        sources.Where(s => s.Group == "Items" && s.Target?.Map is not null && s.Name.EndsWith("Item")).ToList();

    /// <summary>The first ID after every ID the kind already has, counting the project's own new items.</summary>
    public static int NextId(Table kind, ModProject project, string kindId) =>
        kind.Rows.Select(r => Id(r.Key)).Concat(project.AddedRows.Where(r => r.Table == kindId).Select(r => Id(r.Key))).DefaultIfEmpty(0).Max() + 1;

    static int Id(string key) => int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0;

    /// <summary>
    /// Adds the item to the project, after checking it against the unmodded game. Returns notes for the modder about
    /// what the copy keeps. Throws EditException when the item can't be added as given.
    /// </summary>
    public static IReadOnlyList<string> Add(Game game, Settings settings, ModProject project, IReadOnlyList<TableSource> sources, NewItem item)
    {
        var kind = Kinds(sources).FirstOrDefault(s => s.Id.Equals(item.Kind, StringComparison.OrdinalIgnoreCase))
                   ?? throw new EditException($"{item.Kind} isn't an item kind. Item kinds are the Items tables ending in Item, such as Items/HealItem.");
        if (item.Id <= 0)
            throw new EditException("The ID must be a whole number above 0.");
        if (string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Description))
            throw new EditException("The item needs an English name and description.");

        var table = kind.Load(game);
        var from = table.Rows.FirstOrDefault(r => r.Key == item.From)
                   ?? throw new EditException($"{kind.Id} has no item {item.From} to copy.");
        var key = item.Id.ToString(CultureInfo.InvariantCulture);
        if (table.Rows.Any(r => r.Key == key))
            throw new EditException($"{kind.Id} already has an item with ID {key}. The next free ID is {NextId(table, project, kind.Id)}.");
        if (project.FindRow(kind.Id, key) is not null)
            throw new EditException($"This project already adds an item with ID {key} to {kind.Id}.");

        var nameKey = TextKey(from.Fields, "ItemKey", item.Id);
        var descriptionKey = TextKey(from.Fields, "DescriptionKey", item.Id);
        foreach (var textKey in new[] { nameKey, descriptionKey })
            if (game.HasText(textKey) || project.FindAddedText(Game.TextNamespace, textKey) is not null)
                throw new EditException($"The text key {textKey} is already taken, so ID {key} can't be used. Try another ID.");

        var row = new RowAddition(kind.Id, key, item.From, new Dictionary<string, string>
        {
            ["ID"] = key,
            ["ItemKey"] = nameKey,
            ["DescriptionKey"] = descriptionKey,
        });
        var asset = EditableAsset.Load(settings, new Usmap(settings.MappingsFile!), kind.Target!.File);
        if (asset.Check(kind.Target, row) is { } problem)
            throw new EditException(problem);

        var rows = new List<RowAddition> { row };
        rows.AddRange(Pictures(game, sources, project, kind, from, key));

        foreach (var added in rows)
            project.SetRow(added);
        project.SetAddedText(Text(nameKey, item.Name, item.Translations.ToDictionary(t => t.Key, t => t.Value.Name)));
        project.SetAddedText(Text(descriptionKey, item.Description, item.Translations.ToDictionary(t => t.Key, t => t.Value.Description)));

        var source = from.Text.GetValueOrDefault("ItemKey", item.From);
        var notes = new List<string>
        {
            $"It starts as a copy of {source}, so it has that item's icon, rarity, prices and effects until you change them. A new icon needs The Seed, Cardinal's Unreal kit, which isn't out yet.",
        };
        if (rows.Count > 1)
            notes.Add($"It also copies {source}'s pictures in {string.Join(", ", rows.Skip(1).Select(r => r.Table).Distinct())}, which are looked up by ID. Whether the game finds a 3D model for a new ID hasn't been tested.");
        notes.Add("The game can't hand it out yet: Cardinal can't add it to a shop, chest or drop list. Point an existing recipe's result at it (its ItemData.ItemId), or give it to yourself with a dev mod to test it.");
        return notes;
    }

    // The game's own keys end in the item's ID, as in ItemName_Heal_1, so the new key swaps in the new ID.
    static string TextKey(JObject fields, string field, int id)
    {
        var key = fields[field]?.ToString();
        if (string.IsNullOrEmpty(key) || key == "None")
            throw new EditException($"The item to copy has no {field}, so Cardinal can't give the new one its own text.");
        var number = id.ToString(CultureInfo.InvariantCulture);
        return TrailingNumber().IsMatch(key) ? TrailingNumber().Replace(key, "_" + number) : $"{key}_{number}";
    }

    [GeneratedRegex(@"_\d+$")]
    private static partial Regex TrailingNumber();

    static AddedText Text(string key, string english, Dictionary<string, string> translations)
    {
        var lines = translations.Where(t => !string.IsNullOrWhiteSpace(t.Value) && t.Key != GameText.English)
            .ToDictionary(t => t.Key, t => t.Value);
        lines[GameText.English] = english;
        return new AddedText(Game.TextNamespace, key, lines);
    }

    // Weapons and gear find their menu pictures by ID in tables like DT_OneHandedSwordThumbnail, so the copy gets rows there too.
    static IEnumerable<RowAddition> Pictures(Game game, IReadOnlyList<TableSource> sources, ModProject project, TableSource kind, TableRow from, string id)
    {
        var stem = kind.Name.EndsWith("WeaponItem") ? kind.Name[..^"WeaponItem".Length] : kind.Name[..^"Item".Length];
        var names = new[] { $"DT_{stem}Thumbnail", $"DT_Gendered{stem}Thumbnail", $"DT_{stem}Impostor" };
        var fromId = from.Fields["ID"]?.ToString();
        foreach (var source in sources.Where(s => s.Target is not null && names.Contains(s.Name)))
        {
            var table = source.Load(game);
            var match = table.Rows.FirstOrDefault(r => r.Fields["ID"]?.ToString() == fromId);
            if (match is null)
                continue;
            var next = table.Rows.Select(r => Id(r.Key))
                .Concat(project.AddedRows.Where(r => r.Table == source.Id).Select(r => Id(r.Key)))
                .DefaultIfEmpty(-1).Max() + 1;
            yield return new RowAddition(source.Id, next.ToString(CultureInfo.InvariantCulture), match.Key,
                new Dictionary<string, string> { ["ID"] = id });
        }
    }
}
