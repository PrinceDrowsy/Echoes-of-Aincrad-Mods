using System.Globalization;
using Cardinal.Tables;
using UAssetAPI.Unversioned;

namespace Cardinal.Mods;

/// <summary>A new town: the DT_TownList row it starts as a copy of, its town ID and its English name.</summary>
public sealed record NewTown(string From, int Id, string Name);

/// <summary>
/// The "new town" template. A town is a DT_TownList row plus a Town_ asset named after the row's ID, and the warp list
/// shows the text AreaTitle_ followed by the row's Name. The copy keeps the source town's map, terminal and start points.
/// </summary>
public static class TownTemplate
{
    public const string Table = "Other/DT_TownList";
    const string Folder = "/Game/ROD/DataAssets/Town/", TitlePrefix = "AreaTitle_";

    /// <summary>The first town ID after every one the game and the project's own new towns use.</summary>
    public static int NextId(Table towns, ModProject project) =>
        towns.Rows.Select(r => r.Fields["ID"]?.ToString())
            .Concat(project.AddedRows.Where(r => r.Table == Table).Select(r => r.Fields.GetValueOrDefault("ID")))
            .Select(Number).DefaultIfEmpty(0).Max() + 1;

    static int Number(string? text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    /// <summary>
    /// Adds the town to the project, after checking it against the unmodded game. Returns notes for the modder about
    /// what the copy shares. Throws EditException when the town can't be added as given.
    /// </summary>
    public static IReadOnlyList<string> Add(Game game, Settings settings, ModProject project, IReadOnlyList<TableSource> sources, NewTown town)
    {
        var source = sources.Single(s => s.Id == Table);
        var towns = source.Load(game);
        var from = towns.Rows.FirstOrDefault(r => r.Key == town.From)
                   ?? throw new EditException($"{Table} has no row {town.From} to copy.");
        if (town.Id is <= 0 or > 999)
            throw new EditException("The town ID must be a whole number from 1 to 999.");
        var id = town.Id.ToString("000", CultureInfo.InvariantCulture);
        if (towns.Rows.Any(r => Number(r.Fields["ID"]?.ToString()) == town.Id)
            || project.AddedRows.Any(r => r.Table == Table && Number(r.Fields.GetValueOrDefault("ID")) == town.Id))
            throw new EditException($"Town ID {id} is already taken. The next free one is {NextId(towns, project):000}.");

        // The row's Name only picks the warp list's text key, so it's the English name without spaces or punctuation.
        var english = town.Name.Trim();
        var name = new string(english.Where(char.IsAsciiLetterOrDigit).ToArray());
        if (name.Length == 0)
            throw new EditException("The town needs an English name with at least one letter or digit from A to Z or 0 to 9.");
        var title = TitlePrefix + name;
        if (towns.Rows.Any(r => r.Fields["Name"]?.ToString() == name) || game.HasText(title)
            || project.FindAddedText(Game.TextNamespace, title) is not null)
            throw new EditException($"A town is already called {name}. Choose another name.");

        var key = (towns.Rows.Select(r => Number(r.Key))
            .Concat(project.AddedRows.Where(r => r.Table == Table).Select(r => Number(r.Key)))
            .DefaultIfEmpty(-1).Max() + 1).ToString(CultureInfo.InvariantCulture);
        var row = new RowAddition(Table, key, from.Key, new Dictionary<string, string> { ["ID"] = id, ["Name"] = name });
        var asset = EditableAsset.Load(settings, new Usmap(settings.MappingsFile!), source.Target!.File);
        if (asset.Check(source.Target, row) is { } problem)
            throw new EditException(problem);
        var copy = new AssetCopy(Folder + "Town_" + from.Fields["ID"], Folder + "Town_" + id);
        if (AssetCopier.Check(game, AssetRegistry.Read(game.Provider.SaveAsset(AssetRegistry.File)), copy) is { } copyProblem)
            throw new EditException(copyProblem);

        project.SetRow(row);
        project.SetAsset(copy);
        project.SetAddedText(new AddedText(Game.TextNamespace, title, new Dictionary<string, string> { [Cardinal.Text.GameText.English] = english }));

        var copying = game.Text(TitlePrefix + from.Fields["Name"]) ?? $"town {from.Fields["ID"]}";
        return
        [
            $"It starts as a copy of {copying}, so it uses that town's map, gate, shops and start points. A map of its own needs The Seed, Cardinal's Unreal kit.",
            $"It appears in the warp list once the story reaches step {from.Fields["StoryProgress"]}, like {copying}. Change StoryProgress in its row to open it sooner or later.",
            "Players need the registry patch for the game to find it. Installing from Cardinal updates it; otherwise press Patch new assets.",
        ];
    }
}
