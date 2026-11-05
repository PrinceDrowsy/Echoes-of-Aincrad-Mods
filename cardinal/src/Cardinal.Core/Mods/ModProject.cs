using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cardinal.Mods;

/// <summary>
/// One changed value. Table is a table id (Items/MaterialItem), Row a row key, Field a column's dotted path.
/// Original is the unmodded value when the edit was made, kept so a game update that changes it can be spotted.
/// </summary>
public sealed record ModEdit(string Table, string Row, string Field, string Value, string? Original = null)
{
    /// <summary>Whether two cell texts mean the same value, so 5 and 5.0, or True and true, count as unchanged.</summary>
    public static bool Same(string a, string? b)
    {
        if (b is null)
            return false;
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        if (double.TryParse(a, System.Globalization.NumberStyles.Float, invariant, out var x)
            && double.TryParse(b, System.Globalization.NumberStyles.Float, invariant, out var y))
            return x == y;
        if (bool.TryParse(a, out var p) && bool.TryParse(b, out var q))
            return p == q;
        return a == b;
    }
}

/// <summary>
/// A mod stored as its changes rather than whole game files, so it can be rebuilt on top of a newer game.
/// The format is documented in the README; keep it stable and bump FormatVersion if it ever has to change.
/// </summary>
public sealed partial class ModProject
{
    public const int CurrentFormat = 1;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public int FormatVersion { get; set; } = CurrentFormat;
    public string Name { get; set; } = "MyMod";
    public string Version { get; set; } = "1.0.0";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public List<ModEdit> Edits { get; set; } = [];

    public static ModProject Load(string path)
    {
        ModProject? project;
        try
        {
            project = JsonSerializer.Deserialize<ModProject>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException e)
        {
            throw new ArgumentException($"{Path.GetFileName(path)} isn't a valid mod project: {e.Message}");
        }
        if (project is null)
            throw new ArgumentException($"{Path.GetFileName(path)} is empty.");
        if (project.FormatVersion > CurrentFormat)
            throw new ArgumentException($"{Path.GetFileName(path)} was made by a newer Cardinal (format {project.FormatVersion}).");
        return project;
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));

    public ModEdit? Find(string table, string row, string field) =>
        Edits.FirstOrDefault(e => e.Table == table && e.Row == row && e.Field == field);

    /// <summary>Adds the edit, replacing any earlier one to the same field. An earlier edit's original value is kept.</summary>
    public void Set(ModEdit edit)
    {
        var old = Find(edit.Table, edit.Row, edit.Field);
        if (old is not null)
            Edits[Edits.IndexOf(old)] = edit with { Original = old.Original ?? edit.Original };
        else
            Edits.Add(edit);
    }

    public bool Remove(string table, string row, string field) =>
        Find(table, row, field) is { } edit && Edits.Remove(edit);

    /// <summary>
    /// The name the built files get: letters, digits, dashes and underscores, ending in a patch number and _P.
    /// Unreal ranks patch containers by that number, and a plain Name_P ties with the game's own patch
    /// (pakchunk0-WindowsClient_0_P), so which copy of an asset loads would be left to chance.
    /// </summary>
    public string FileName()
    {
        var name = new string(Name.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').ToArray());
        if (name.Length == 0)
            name = "CardinalMod";
        if (PatchSuffix().IsMatch(name))
            return name;
        if (name.EndsWith("_P"))
            name = name[..^2];
        return name + "_9999_P";
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"_\d+_P$")]
    private static partial System.Text.RegularExpressions.Regex PatchSuffix();
}
