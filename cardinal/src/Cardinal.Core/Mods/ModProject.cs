using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cardinal.Mods;

/// <summary>
/// One changed value. Table is a table id (Items/MaterialItem), Row a row key, Field a column's dotted path.
/// Original is the unmodded value when the edit was made, kept so a game update that changes it can be spotted.
/// </summary>
public sealed record ModEdit(string Table, string Row, string Field, string Value, string? Original = null)
{
    [JsonIgnore]
    public string Label => $"{Table} / {Row} / {Field}";

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
/// One changed line of the game's text. Culture is the language's code as the game names it (en, ja-JP),
/// Namespace and Key identify the line. Original is the unmodded text when the edit was made.
/// </summary>
public sealed record TextEdit(string Culture, string Namespace, string Key, string Value, string? Original = null)
{
    [JsonIgnore]
    public string Label => $"{Culture} / {Namespace} / {Key}";
}

/// <summary>
/// A row the mod adds to a table. It starts as a copy of From, an existing row of the same table, and Fields then
/// replace single values of the copy by their column's dotted path. Key is the new row's key.
/// </summary>
public sealed record RowAddition(string Table, string Key, string From, Dictionary<string, string> Fields)
{
    [JsonIgnore]
    public string Label => $"{Table} / {Key}";
}

/// <summary>
/// A line of text the mod adds, for a new key that rows can point at. Text holds the line per language code and must
/// have English; every language without its own line shows the English, as the game does for its own untranslated lines.
/// </summary>
public sealed record AddedText(string Namespace, string Key, Dictionary<string, string> Text)
{
    [JsonIgnore]
    public string Label => $"{Namespace} / {Key}";

    [JsonIgnore]
    public string English => Text.GetValueOrDefault(Cardinal.Text.GameText.English, "");
}

/// <summary>
/// A new game asset the mod adds as a copy of an existing one, such as a town: From and Package are /Game paths, and
/// Package's last segment becomes the copy's name. The game only finds primary assets through its asset registry, so
/// the registry patch lists every installed copy (see RegistryPatch).
/// </summary>
public sealed record AssetCopy(string From, string Package)
{
    [JsonIgnore]
    public string Label => Package;

    [JsonIgnore]
    public string Name => Package[(Package.LastIndexOf('/') + 1)..];
}

/// <summary>
/// A change to a step of a Flow graph (story events, quests, tutorials). Asset is the graph's /Game path and Node the
/// step's NodeGuid. Kind, when set, turns it into another kind of step: fields the old and new kinds share keep their
/// values and the old kind's others are dropped. Fields sets the step's own fields. Outputs renames its output pins,
/// old name to new, so it still leads to the same next steps, and Links then points outputs elsewhere.
/// </summary>
public sealed record NodeChange(string Asset, string Node, string? Kind, Dictionary<string, string> Fields,
    Dictionary<string, string> Outputs, Dictionary<string, string>? Links = null)
{
    [JsonIgnore]
    public string Label => $"{Asset} / {Node}";
}

/// <summary>
/// A change made to every step of kind Match, in every script or only those in Folder, whose fields have the values in
/// Where. It changes each one as a NodeChange with Kind, Fields and Outputs would, so it also reaches steps a game update
/// adds. A field value written as {Path}, such as {Params[0].ID}, copies that value from the step being changed.
/// </summary>
public sealed record NodeRule(string Match, string? Folder, Dictionary<string, string>? Where, string? Kind,
    Dictionary<string, string> Fields, Dictionary<string, string> Outputs)
{
    [JsonIgnore]
    public string Label => Match + (Folder is { Length: > 0 } ? $" in {Folder}" : "")
                           + string.Concat((Where ?? []).Select(w => $", {w.Key} {(w.Value.Length > 0 ? w.Value : "unset")}"));
}

/// <summary>
/// A new step in a Flow graph. Node is the id it gets, written like any step's NodeGuid, which links from other steps
/// use. Links maps each of its outputs to the step it leads to: a step id arrives at that step's In pin, and
/// "id:Pin" at another one.
/// </summary>
public sealed record AddedNode(string Asset, string Node, string Kind, Dictionary<string, string> Fields,
    Dictionary<string, string> Links)
{
    [JsonIgnore]
    public string Label => $"{Asset} / {Node}";
}

/// <summary>
/// An item the mod adds to the end of a list in an asset, such as a new part's mesh in AvatarMesh_Pupil's AvatarMeshes.
/// Asset is the asset's /Game path, Field the list's name, and Value the new item, written like an edit's value.
/// </summary>
public sealed record ListItem(string Asset, string Field, string Value)
{
    [JsonIgnore]
    public string Label => $"{Asset} / {Field} / {Value}";
}

/// <summary>
/// A mod stored as its changes rather than whole game files, so it can be rebuilt on top of a newer game.
/// The format is documented in the README; keep it stable and bump FormatVersion if it ever has to change.
/// Format 2 added text edits, format 3 added rows and text, format 4 copied assets, format 5 changed Flow steps, and
/// format 6 added Flow steps, links and step rules, and format 7 list items; older files load unchanged.
/// </summary>
public sealed partial class ModProject
{
    public const int CurrentFormat = 7;

    // The patch number in the built file name, which Unreal uses to rank mods: higher wins when two replace the same file.
    // The merged patch takes the top number, so it wins over every mod it was merged from.
    public const int DefaultPriority = 1000, MaxPriority = 9998, MergedPriority = 9999;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        // Text edits in Japanese or Russian stay readable in the file instead of becoming \u escapes.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public int FormatVersion { get; set; } = CurrentFormat;
    public string Name { get; set; } = "MyMod";
    public string Version { get; set; } = "1.0.0";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public int Priority { get; set; } = DefaultPriority;
    public List<ModEdit> Edits { get; set; } = [];
    public List<TextEdit> TextEdits { get; set; } = [];
    public List<RowAddition> AddedRows { get; set; } = [];
    public List<AddedText> AddedText { get; set; } = [];
    public List<AssetCopy> AddedAssets { get; set; } = [];
    public List<NodeChange> NodeChanges { get; set; } = [];
    public List<AddedNode> AddedNodes { get; set; } = [];
    public List<NodeRule> NodeRules { get; set; } = [];
    public List<ListItem> AddedListItems { get; set; } = [];

    /// <summary>Every change of every kind.</summary>
    [JsonIgnore]
    public int Changes => TextEdits.Count + AddedAssets.Count + AssetChanges;

    /// <summary>Whether a build has a container (.utoc and .ucas) besides the .pak: anything but text changes.</summary>
    [JsonIgnore]
    public bool HasContainer => AddedAssets.Count + AssetChanges > 0;

    /// <summary>Changes made inside the game's own assets.</summary>
    [JsonIgnore]
    public int AssetChanges => Edits.Count + AddedRows.Count + AddedText.Count + NodeChanges.Count + AddedNodes.Count + NodeRules.Count
                               + AddedListItems.Count;

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
        if (project.Priority is < 1 or > MaxPriority)
            throw new ArgumentException($"{Path.GetFileName(path)} has priority {project.Priority}; it must be from 1 to {MaxPriority}, since {MergedPriority} is kept for the merged patch.");
        return project;
    }

    // An older file saved again takes the current format, so an older Cardinal refuses it instead of dropping what it can't read.
    public void Save(string path)
    {
        FormatVersion = CurrentFormat;
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

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

    public TextEdit? FindText(string culture, string ns, string key) =>
        TextEdits.FirstOrDefault(e => e.Culture == culture && e.Namespace == ns && e.Key == key);

    /// <summary>Adds the text edit, replacing any earlier one to the same line. An earlier edit's original text is kept.</summary>
    public void SetText(TextEdit edit)
    {
        var old = FindText(edit.Culture, edit.Namespace, edit.Key);
        if (old is not null)
            TextEdits[TextEdits.IndexOf(old)] = edit with { Original = old.Original ?? edit.Original };
        else
            TextEdits.Add(edit);
    }

    public bool RemoveText(string culture, string ns, string key) =>
        FindText(culture, ns, key) is { } edit && TextEdits.Remove(edit);

    public RowAddition? FindRow(string table, string key) =>
        AddedRows.FirstOrDefault(r => r.Table == table && r.Key == key);

    /// <summary>Adds the row, replacing an earlier addition with the same table and key.</summary>
    public void SetRow(RowAddition row)
    {
        if (FindRow(row.Table, row.Key) is { } old)
            AddedRows[AddedRows.IndexOf(old)] = row;
        else
            AddedRows.Add(row);
    }

    public bool RemoveRow(string table, string key) =>
        FindRow(table, key) is { } row && AddedRows.Remove(row);

    public AddedText? FindAddedText(string ns, string key) =>
        AddedText.FirstOrDefault(t => t.Namespace == ns && t.Key == key);

    /// <summary>Adds the line, replacing an earlier one with the same namespace and key.</summary>
    public void SetAddedText(AddedText text)
    {
        if (FindAddedText(text.Namespace, text.Key) is { } old)
            AddedText[AddedText.IndexOf(old)] = text;
        else
            AddedText.Add(text);
    }

    public bool RemoveAddedText(string ns, string key) =>
        FindAddedText(ns, key) is { } text && AddedText.Remove(text);

    public AssetCopy? FindAsset(string package) =>
        AddedAssets.FirstOrDefault(a => a.Package.Equals(package, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds the copy, replacing an earlier one with the same package.</summary>
    public void SetAsset(AssetCopy copy)
    {
        if (FindAsset(copy.Package) is { } old)
            AddedAssets[AddedAssets.IndexOf(old)] = copy;
        else
            AddedAssets.Add(copy);
    }

    public bool RemoveAsset(string package) =>
        FindAsset(package) is { } copy && AddedAssets.Remove(copy);

    public NodeChange? FindNode(string asset, string node) =>
        NodeChanges.FirstOrDefault(n => n.Asset.Equals(asset, StringComparison.OrdinalIgnoreCase) && n.Node == node);

    /// <summary>Adds the change, replacing an earlier one to the same step.</summary>
    public void SetNode(NodeChange change)
    {
        if (FindNode(change.Asset, change.Node) is { } old)
            NodeChanges[NodeChanges.IndexOf(old)] = change;
        else
            NodeChanges.Add(change);
    }

    public bool RemoveNode(string asset, string node) =>
        FindNode(asset, node) is { } change && NodeChanges.Remove(change);

    public AddedNode? FindAddedNode(string asset, string node) =>
        AddedNodes.FirstOrDefault(n => n.Asset.Equals(asset, StringComparison.OrdinalIgnoreCase) && n.Node == node);

    /// <summary>Adds the step, replacing an earlier addition with the same id in the same graph.</summary>
    public void SetAddedNode(AddedNode added)
    {
        if (FindAddedNode(added.Asset, added.Node) is { } old)
            AddedNodes[AddedNodes.IndexOf(old)] = added;
        else
            AddedNodes.Add(added);
    }

    public bool RemoveAddedNode(string asset, string node) =>
        FindAddedNode(asset, node) is { } added && AddedNodes.Remove(added);

    /// <summary>Adds the rule, replacing an earlier one for the same steps.</summary>
    public void SetRule(NodeRule rule)
    {
        if (NodeRules.FirstOrDefault(r => r.Label == rule.Label) is { } old)
            NodeRules[NodeRules.IndexOf(old)] = rule;
        else
            NodeRules.Add(rule);
    }

    public ListItem? FindListItem(string asset, string field, string value) =>
        AddedListItems.FirstOrDefault(i => i.Asset.Equals(asset, StringComparison.OrdinalIgnoreCase) && i.Field == field && i.Value == value);

    /// <summary>Adds the item unless the project already adds the same one.</summary>
    public void SetListItem(ListItem item)
    {
        if (FindListItem(item.Asset, item.Field, item.Value) is null)
            AddedListItems.Add(item);
    }

    public bool RemoveListItem(string asset, string field, string value) =>
        FindListItem(asset, field, value) is { } item && AddedListItems.Remove(item);

    /// <summary>
    /// The name the built files get: BaseName(), then the priority and _P.
    /// Unreal ranks patch containers by that number, and a plain Name_P ties with the game's own patch
    /// (pakchunk0-WindowsClient_0_P), so which copy of an asset loads would be left to chance.
    /// </summary>
    public string FileName() => $"{BaseName()}_{Priority}_P";

    /// <summary>The name's letters, digits, dashes and underscores: the part of FileName() that stays the same at any priority.</summary>
    public string BaseName()
    {
        var name = PatchSuffix().Replace(new string(Name.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').ToArray()), "");
        return name.Length > 0 ? name : "CardinalMod";
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"(_\d+)?_P$")]
    private static partial System.Text.RegularExpressions.Regex PatchSuffix();
}
