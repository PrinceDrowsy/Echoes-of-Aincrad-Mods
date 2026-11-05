using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cardinal.Tables;

/// <summary>One row as the game stores it, plus English text for any text keys found in it.</summary>
public sealed record TableRow(string Key, JObject Fields, IReadOnlyDictionary<string, string> Text);

/// <summary>A table of rows read from the game, with a flat view for grids and CSV.</summary>
public sealed partial class Table
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
        Columns = OrderColumns(flat);
        Cells = flat.Select(r => Columns.Select(c => r.GetValueOrDefault(c, "")).ToArray()).ToList();
    }

    // Rows leave out fields still at their defaults, so taking columns in first-seen order scrambles them.
    // Each row is in schema order, so a column missing so far goes in right after the one before it in that row.
    // Then names and IDs come first, since those are what people scan for.
    static List<string> OrderColumns(IEnumerable<Dictionary<string, string>> rows)
    {
        var columns = new List<string> { "Key" };
        foreach (var row in rows)
        {
            var at = 0;
            foreach (var column in row.Keys)
            {
                var index = columns.IndexOf(column);
                if (index < 0)
                    columns.Insert(index = at + 1, column);
                at = index;
            }
        }
        var leading = columns.Skip(1).Where(Identifying).ToList();
        return ["Key", .. leading, .. columns.Skip(1).Except(leading)];
    }

    // Top-level fields like ItemName or SkillID, and a nested struct's own Name or NameKey (as in quests).
    // Deeper IDs usually point at other things, such as a reward's ItemId, so they stay where they are.
    static bool Identifying(string column)
    {
        var root = column.EndsWith(" (text)") ? column[..^7] : column;
        var last = root[(root.LastIndexOf('.') + 1)..];
        return root.Contains('.')
            ? last is "Name" or "NameKey" or "Title" or "TitleKey" or "ID"
            : TopLevelIdentifying().IsMatch(root);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^(\w*Name|\w*ID|\w*Id|Title)$")]
    private static partial System.Text.RegularExpressions.Regex TopLevelIdentifying();

    /// <summary>The raw value at a column's dotted path in one row, or null for the key, text and missing fields.</summary>
    public JToken? Field(int row, string column)
    {
        JToken? token = Rows[row].Fields;
        foreach (var part in column.Split('.'))
            token = (token as JObject)?[part];
        return token;
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

    /// <summary>How a value reads in a grid cell or CSV.</summary>
    public static string Cell(JToken value) => value switch
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
