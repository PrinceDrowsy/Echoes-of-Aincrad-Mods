using System.Text.RegularExpressions;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cardinal.Tables;

/// <summary>A table Cardinal knows how to read, loaded only when asked for.</summary>
public sealed record TableSource(string Group, string Name, string Source, Func<Game, Table> Load)
{
    /// <summary>A file-safe name used for exports, such as Items/MaterialItem.</summary>
    public string Id => $"{Group}/{Regex.Replace(Name, @"[^A-Za-z0-9_\-]", "")}";
}

/// <summary>Finds the browsable tables in the player's game files.</summary>
public static class TableCatalog
{
    const string DataAssets = "ROD/DataAssets/";
    const string ItemDataAsset = Game.ContentRoot + DataAssets + "Items/ItemDataAsset.uasset";
    const string EnemyPackages = Game.ContentRoot + DataAssets + "Enemies/DA_EnemyPackageData.uasset";
    const string QuestsSource = "/Game/" + DataAssets + "Quests/";

    static readonly JsonSerializer Json = JsonSerializer.CreateDefault();

    public static IReadOnlyList<TableSource> Discover(Game game)
    {
        var sources = new List<TableSource>();
        var curated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddDataTable(string group, string path)
        {
            if (!curated.Add(path))
                return;
            var name = Path.GetFileNameWithoutExtension(path);
            sources.Add(new TableSource(group, name, AssetPath(path), g => DataTable(g, path, name)));
        }

        foreach (var map in ItemMaps(game))
            sources.Add(new TableSource("Items", map, AssetPath(ItemDataAsset), g => ItemMap(g, map)));
        foreach (var path in game.FindFiles(DataAssets + "Games/DataTables/DT_ShopItemList", ".uasset"))
            AddDataTable("Items", path);

        foreach (var path in game.FindFiles(DataAssets + "Database/DT_MonsterDatabase", ".uasset"))
            AddDataTable("Enemies", path);
        if (game.Provider.Files.ContainsKey(EnemyPackages))
            sources.Add(new TableSource("Enemies", "EnemyBlueprints", AssetPath(EnemyPackages), EnemyBlueprints));
        foreach (var path in game.FindFiles(DataAssets + "WorldAdmin/", ".uasset").Where(p => p.Contains("/DT_CharacterGroup")))
            AddDataTable("Enemies", path);

        foreach (var path in game.FindFiles(DataAssets + "Items/Weapons/SwordSkill/DT_", ".uasset"))
            AddDataTable("Skills", path);
        foreach (var name in new[] { "Parameters/Hero/DT_ActiveSkillList", "Character/Partner/DT_SupportSkill", "Character/Partner/DT_CombinationSlash" })
            foreach (var path in game.FindFiles(DataAssets + name, ".uasset"))
                AddDataTable("Skills", path);

        sources.Add(new TableSource("Quests", "Quests", QuestsSource, Quests));

        foreach (var path in game.FindFiles("ROD/", ".uasset").Where(p => Path.GetFileName(p).StartsWith("DT_")))
            AddDataTable("Other", path);

        return sources;
    }

    static string AssetPath(string file) => "/Game/" + file[Game.ContentRoot.Length..^".uasset".Length];

    static JObject ToJson(object value) => JObject.FromObject(value, Json);

    static TableRow Row(Game game, string key, JObject fields) => new(key, fields, Table.FindText(game, fields));

    static Table DataTable(Game game, string path, string name)
    {
        var table = game.Load<UDataTable>(path);
        var rows = table.RowMap.Select(row => Row(game, row.Key.Text, ToJson(row.Value))).ToList();
        return new Table(name, AssetPath(path), rows);
    }

    // The item asset holds one map per item kind, such as MaterialItemDataAsMap.
    static IEnumerable<string> ItemMaps(Game game)
    {
        if (!game.Provider.Files.ContainsKey(ItemDataAsset))
            return [];
        return game.Load<UObject>(ItemDataAsset).Properties
            .Select(p => p.Name.Text)
            .Where(n => n.EndsWith("DataAsMap"))
            .Select(n => n[..^"DataAsMap".Length]);
    }

    static Table ItemMap(Game game, string map)
    {
        var asset = ToJson(game.Load<UObject>(ItemDataAsset));
        var entries = asset["Properties"]?[map + "DataAsMap"] as JArray ?? [];
        var rows = entries.OfType<JObject>()
            .Select(e => Row(game, e["Key"]?.ToString() ?? "", e["Value"] as JObject ?? []))
            .ToList();
        return new Table(map, AssetPath(ItemDataAsset), rows);
    }

    static Table EnemyBlueprints(Game game)
    {
        var asset = ToJson(game.Load<UObject>(EnemyPackages));
        var rows = new List<TableRow>();
        foreach (var (family, value) in asset["Properties"] as JObject ?? [])
        {
            var entries = value is JArray array ? array.ToList() : [value!];
            foreach (var entry in entries)
            {
                var blueprint = entry["AssetPathName"]?.ToString() ?? "";
                var fields = new JObject { ["Family"] = family, ["Blueprint"] = blueprint };
                rows.Add(Row(game, Path.GetFileNameWithoutExtension(blueprint.Split('.')[0]), fields));
            }
        }
        return new Table("EnemyBlueprints", AssetPath(EnemyPackages), rows);
    }

    static Table Quests(Game game)
    {
        var rows = new List<TableRow>();
        foreach (var path in game.FindFiles(DataAssets + "Quests/", ".uasset").Where(p => Path.GetFileName(p).StartsWith("QST_")))
        {
            var fields = ToJson(game.Load<UObject>(path))["Properties"] as JObject ?? [];
            rows.Add(Row(game, Path.GetFileNameWithoutExtension(path), fields));
        }
        return new Table("Quests", QuestsSource, rows);
    }
}
