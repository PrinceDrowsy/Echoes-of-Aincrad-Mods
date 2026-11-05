using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cardinal.Tables;

/// <summary>One row as the game stores it, plus English text for any text keys found in it.</summary>
public sealed record TableRow(string Key, JObject Fields, IReadOnlyDictionary<string, string> Text);

/// <summary>A table of rows read from the game, with a flat view for grids and CSV.</summary>
public sealed class Table
{
    const int FlattenDepth = 3;

    public string Name { get; }
    public string Source { get; }
    public IReadOnlyList<TableRow> Rows { get; }
    public IReadOnlyList<string> Columns { get; }
    public IReadOnlyList<string[]> Cells { get; }

    public Table(string name, string source, IReadOnlyList<TableRow> rows)
    {
        Name = name;
        Source = source;
        Rows = rows;

        var flat = rows.Select(Flatten).ToList();
        var columns = new List<string> { "Key" };
        var seen = new HashSet<string>(columns);
        foreach (var column in flat.SelectMany(r => r.Keys))
            if (seen.Add(column))
                columns.Add(column);
        Columns = columns;
        Cells = flat.Select(r => columns.Select(c => r.GetValueOrDefault(c, "")).ToArray()).ToList();
    }

    static Dictionary<string, string> Flatten(TableRow row)
    {
        var cells = new Dictionary<string, string> { ["Key"] = row.Key };
        foreach (var (name, value) in row.Fields)
            Flatten(cells, name, value!, 1, row.Text);
        return cells;
    }

    static void Flatten(Dictionary<string, string> cells, string path, JToken value, int depth,
        IReadOnlyDictionary<string, string> text)
    {
        if (value is JObject obj && Reference(obj) is { } reference)
            cells[path] = reference;
        else if (value is JObject nested && depth < FlattenDepth && nested.Count > 0)
            foreach (var (name, child) in nested)
                Flatten(cells, $"{path}.{name}", child!, depth + 1, text);
        else
        {
            cells[path] = Cell(value);
            if (text.TryGetValue(path, out var english))
                cells[path + " (text)"] = english;
        }
    }

    static string Cell(JToken value) => value switch
    {
        JValue { Value: null } => "",
        JValue v => Convert.ToString(v.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "",
        JArray array when array.All(item => item is JValue) => string.Join(", ", array.Select(Cell)),
        JObject obj when Reference(obj) is { } reference => reference,
        _ => value.ToString(Formatting.None),
    };

    // Asset references come out as small objects; a path reads better in a cell.
    static string? Reference(JObject obj)
    {
        if (obj.Count == 2 && obj["ObjectPath"] is JValue objectPath)
            return objectPath.ToString();
        if (obj.Count == 2 && obj["AssetPathName"] is JValue assetPath && obj.ContainsKey("SubPathString"))
            return assetPath.ToString();
        return null;
    }

    /// <summary>Finds text keys anywhere in a row, keyed by the same dotted paths the flat view uses.</summary>
    public static Dictionary<string, string> FindText(Game game, JObject fields)
    {
        var found = new Dictionary<string, string>();
        foreach (var (name, value) in fields)
            FindText(game, name, value!, 1, found);
        return found;
    }

    static void FindText(Game game, string path, JToken value, int depth, Dictionary<string, string> found)
    {
        if (value is JObject nested && depth < FlattenDepth)
            foreach (var (name, child) in nested)
                FindText(game, $"{path}.{name}", child!, depth + 1, found);
        else if (value is JValue { Type: JTokenType.String } text && game.Text((string?)text) is { } english)
            found[path] = english;
    }
}
