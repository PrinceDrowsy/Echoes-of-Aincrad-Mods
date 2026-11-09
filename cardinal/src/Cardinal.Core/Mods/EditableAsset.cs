using System.Globalization;
using Cardinal.Tables;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

namespace Cardinal.Mods;

/// <summary>
/// A game asset loaded with UAssetAPI so its values can be changed and written back.
/// CUE4Parse reads everything for browsing; this side only exists for the files a mod edits.
/// </summary>
public sealed class EditableAsset
{
    readonly UAsset asset;

    /// <summary>Why this asset can't be edited, or null when it can.</summary>
    public string? Problem { get; }

    public string File { get; }

    EditableAsset(string file, UAsset asset, string? problem)
    {
        File = file;
        this.asset = asset;
        Problem = problem;
    }

    /// <summary>Extracts the unmodded asset with retoc and parses it. A file UAssetAPI can't decode comes back read-only, not as an error.</summary>
    public static EditableAsset Load(Settings settings, Usmap mappings, string file)
    {
        var work = WorkFolder();
        try
        {
            var path = Retoc.Extract(settings, file, work);
            var asset = new UAsset(path, EngineVersion.VER_UE5_3, mappings);
            // An export UAssetAPI can't decode is written back byte for byte, so it only blocks changes made inside it.
            // Flow graphs often have Blueprint steps the mappings don't describe; Change checks its own step instead.
            var raw = asset.Exports.OfType<RawExport>().FirstOrDefault();
            return new EditableAsset(file, asset, raw is null
                ? null
                : $"Cardinal can't decode {Path.GetFileName(file)} with the current mappings, so it's read-only. " +
                  "Mappings made for your exact game version (Cardinal Mappings writes them) may fix this.");
        }
        finally
        {
            Directory.Delete(work, true);
        }
    }

    public static string WorkFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Cardinal", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Checks that an edit fits the field it targets without changing anything. Returns why not, or null.</summary>
    public string? Check(EditTarget target, ModEdit edit)
    {
        try
        {
            Set(target, edit, false);
            return null;
        }
        catch (EditException e)
        {
            return e.Message;
        }
    }

    public void Apply(EditTarget target, ModEdit edit) => Set(target, edit, true);

    public void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        asset.Write(path);
    }

    void Set(EditTarget target, ModEdit edit, bool commit)
    {
        if (Problem is not null)
            throw new EditException(Problem);
        SetField(RowFields(target, edit.Row), edit.Field, edit.Value, commit);
    }

    void SetField(List<PropertyData> fields, string field, string value, bool commit)
    {
        var parts = field.Split('.');
        PropertyData? property = null;
        foreach (var part in parts)
        {
            property = fields.FirstOrDefault(p => p.Name.ToString() == part);
            if (property is null)
                throw new EditException($"{field} isn't stored in this row. Fields left at their default value can't be edited yet.");
            if (part != parts[^1])
                fields = property is StructPropertyData nested
                    ? nested.Value
                    : throw new EditException($"{field} can't be edited: {part} isn't a plain group of fields.");
        }
        SetValue(property!, value, commit);
    }

    /// <summary>Checks that a row can be added without changing anything. Returns why not, or null.</summary>
    public string? Check(EditTarget target, RowAddition row)
    {
        try
        {
            Add(target, row, false);
            return null;
        }
        catch (EditException e)
        {
            return e.Message;
        }
    }

    public void Apply(EditTarget target, RowAddition row) => Add(target, row, true);

    // The new row is a deep copy of From with its own key, so every field the game leaves at its default stays that way too.
    void Add(EditTarget target, RowAddition row, bool commit)
    {
        if (Problem is not null)
            throw new EditException(Problem);
        if (target.Map is null)
        {
            var rows = DataTable().Table.Data;
            if (rows.Any(r => r.Name.ToString() == row.Key))
                throw new EditException($"The game already has a row {row.Key}.");
            var copy = (StructPropertyData)(rows.FirstOrDefault(r => r.Name.ToString() == row.From)
                                            ?? throw new EditException($"There's no row {row.From} to copy.")).Clone();
            copy.Name = Name(row.Key);
            foreach (var (field, text) in row.Fields)
                SetField(copy.Value, field, text, true);
            if (commit)
                rows.Add(copy);
            return;
        }

        var map = Map(target);
        if (map.Value.Keys.Any(k => KeyText(k) == row.Key))
            throw new EditException($"The game already has a row {row.Key}.");
        var (fromKey, fromValue) = map.Value.FirstOrDefault(e => KeyText(e.Key) == row.From);
        if (fromKey is null)
            throw new EditException($"There's no row {row.From} to copy.");
        if (fromValue is not StructPropertyData entry)
            throw new EditException($"Row {row.From} isn't a group of fields.");
        var key = (PropertyData)fromKey.Clone();
        SetValue(key, row.Key, true);
        var value = (StructPropertyData)entry.Clone();
        foreach (var (field, text) in row.Fields)
            SetField(value.Value, field, text, true);
        if (commit)
            map.Value.Add(key, value);
    }

    /// <summary>Whether a string table already has a key. Only for string table assets.</summary>
    public bool HasText(string key) => StringTable().ContainsKey(new FString(key));

    /// <summary>Adds a key to a string table, with English as its text: the source every language's line translates.</summary>
    public void AddText(string key, string english)
    {
        var table = StringTable();
        if (table.ContainsKey(new FString(key)))
            throw new EditException($"The game already has text with the key {key}.");
        table.Add(new FString(key), new FString(english));
    }

    /// <summary>Checks that a Flow step can be changed as asked without changing anything. Returns why not, or null.</summary>
    public string? Check(NodeChange change)
    {
        try
        {
            Change(change, false);
            return null;
        }
        catch (EditException e)
        {
            return e.Message;
        }
    }

    public void Apply(NodeChange change) => Change(change, true);

    // The step stays the same export, so the graph's map of steps and every connection into it still find it.
    void Change(NodeChange change, bool commit)
    {
        var guid = UnrealGuid(change.Node);
        var node = asset.Exports.OfType<NormalExport>().FirstOrDefault(e => NodeGuid(e) == guid)
                   ?? throw new EditException(asset.Exports.OfType<RawExport>().Any()
                       ? $"There's no step {change.Node} that Cardinal can read in {Path.GetFileName(File)}. Steps made in Blueprint, like conversations, can't be changed yet."
                       : $"There's no step {change.Node} in {Path.GetFileName(File)}.");
        var kind = change.Kind ?? Import(node.ClassIndex).ObjectName.ToString();
        var fields = StepFields(kind);

        var kept = change.Kind is null
            ? node.Data.ToList()
            : node.Data.Where(p => fields.TryGetValue(p.Name.ToString(), out var field)
                                   && field.PropertyData.Type.ToString() == p.PropertyType.ToString()).ToList();
        SetFields(kept, kind, fields, change.Fields, commit);

        var connections = Connections(kept);
        var renamed = new List<(NamePropertyData Pin, string To)>();
        foreach (var (from, to) in change.Outputs)
        {
            var pin = connections?.Value.Keys.OfType<NamePropertyData>().FirstOrDefault(k => k.Value.ToString() == from)
                      ?? throw new EditException($"Step {change.Node} has no connected output {from}.");
            renamed.Add((pin, to));
        }
        var links = Targets(change.Links);
        if (!commit)
            return;

        foreach (var (pin, to) in renamed)
            pin.Value = Name(to);
        Link(kept, links);
        node.Data = kept;
        if (change.Kind is not null)
            (node.ClassIndex, node.TemplateIndex) = KindImports(kind);
    }

    /// <summary>
    /// Reserves an added step's id before any step is checked, so changes and other new steps can link to it. Returns
    /// why it can't be added, or null.
    /// </summary>
    public string? Reserve(AddedNode added)
    {
        try
        {
            var id = UnrealGuid(added.Node);
            if (StepIds().Contains(id) || !reserved.Add(id))
                throw new EditException($"{Path.GetFileName(File)} already has a step {added.Node}.");
            return null;
        }
        catch (EditException e)
        {
            return e.Message;
        }
    }

    /// <summary>Checks that a reserved step can be added as asked without changing anything. Returns why not, or null.</summary>
    public string? Check(AddedNode added)
    {
        try
        {
            Add(added, false);
            return null;
        }
        catch (EditException e)
        {
            return e.Message;
        }
    }

    public void Apply(AddedNode added) => Add(added, true);

    readonly HashSet<Guid> reserved = [];

    // A new export inside the graph, shaped like the game's own steps: the graph is its outer, the graph's map of steps
    // points to it, and the graph lists it among the exports to create before the graph itself is read.
    void Add(AddedNode added, bool commit)
    {
        var (graphIndex, graph) = Graph();
        var steps = (MapPropertyData)graph.Data.First(p => p.Name.ToString() == "Nodes");
        var sibling = asset.Exports.OfType<NormalExport>().First(e => NodeGuid(e) is not null);
        var fields = StepFields(added.Kind);
        var data = new List<PropertyData>();
        SetFields(data, added.Kind, fields, added.Fields, true);
        var links = Targets(added.Links);
        if (!commit)
            return;

        var id = UnrealGuid(added.Node);
        var guid = (StructPropertyData)sibling.Data.First(p => p.Name.ToString() == "NodeGuid").Clone();
        ((GuidPropertyData)guid.Value[0]).Value = id;
        data.Add(guid);
        Link(data, links);

        var (classIndex, templateIndex) = KindImports(added.Kind);
        var export = new NormalExport(asset, sibling.Extras.ToArray())
        {
            ObjectName = UniqueName(added.Kind),
            ClassIndex = classIndex,
            TemplateIndex = templateIndex,
            OuterIndex = FPackageIndex.FromExport(graphIndex),
            SuperIndex = FPackageIndex.FromRawIndex(0),
            ObjectFlags = sibling.ObjectFlags,
            PackageGuid = sibling.PackageGuid,
            Data = data,
            SerializationBeforeSerializationDependencies = [],
            CreateBeforeSerializationDependencies = [],
            SerializationBeforeCreateDependencies = [classIndex, templateIndex],
            CreateBeforeCreateDependencies = [FPackageIndex.FromExport(graphIndex)],
        };
        asset.Exports.Add(export);
        var index = FPackageIndex.FromExport(asset.Exports.Count - 1);

        var (key, value) = steps.Value.First();
        var newKey = (StructPropertyData)key.Clone();
        ((GuidPropertyData)newKey.Value[0]).Value = id;
        var newValue = (ObjectPropertyData)value.Clone();
        newValue.Value = index;
        steps.Value.Add(newKey, newValue);
        graph.CreateBeforeSerializationDependencies.Add(index);
    }

    (int Index, NormalExport Graph) Graph()
    {
        for (var i = 0; i < asset.Exports.Count; i++)
            if (asset.Exports[i] is NormalExport e && e.Data.Any(p => p.Name.ToString() == "Nodes" && p is MapPropertyData))
                return (i, e);
        throw new EditException($"{Path.GetFileName(File)} isn't a Flow graph Cardinal can read.");
    }

    HashSet<Guid> StepIds() =>
        asset.Exports.OfType<NormalExport>().Select(NodeGuid).OfType<Guid>().ToHashSet();

    // Numbered after the game's own steps of the kind, as in FlowNode_Finish_0, FlowNode_Finish_1.
    FName UniqueName(string kind)
    {
        var taken = asset.Exports.Select(e => e.ObjectName.ToString()).ToHashSet();
        var number = 0;
        while (taken.Contains($"{kind}_{number}"))
            number++;
        return Name($"{kind}_{number}");
    }

    Dictionary<string, UsmapProperty> StepFields(string kind)
    {
        var schema = Schema(kind);
        if (!Lineage(schema).Any(s => s.Name == "FlowNode"))
            throw new EditException($"{kind} isn't a kind of Flow step.");
        return Lineage(schema).SelectMany(s => s.Properties.Values).ToDictionary(p => p.Name);
    }

    // Fields already on the step are only changed on commit; new ones are fresh objects, so setting them is harmless.
    void SetFields(List<PropertyData> data, string kind, Dictionary<string, UsmapProperty> fields, Dictionary<string, string> values, bool commit)
    {
        foreach (var (name, text) in values)
        {
            var property = data.FirstOrDefault(p => p.Name.ToString() == name);
            if (property is not null)
            {
                SetValue(property, text, commit);
                continue;
            }
            property = NewProperty(fields.GetValueOrDefault(name) ?? throw new EditException($"{kind} has no field {name}."));
            SetValue(property, text, true);
            data.Add(property);
        }
    }

    // The kind's class and its default object, imported from the module the mappings list for it.
    (FPackageIndex Class, FPackageIndex Template) KindImports(string kind)
    {
        var module = Schema(kind).ModulePath;
        var package = FindOrAddImport("/Script/CoreUObject", "Package", FPackageIndex.FromRawIndex(0), module);
        return (FindOrAddImport("/Script/CoreUObject", "Class", package, kind),
            FindOrAddImport(module, kind, package, "Default__" + kind));
    }

    static MapPropertyData? Connections(List<PropertyData> data) =>
        data.OfType<MapPropertyData>().FirstOrDefault(m => m.Name.ToString() == "Connections");

    // A link is "step id" or "step id:Pin"; an empty one removes the output's link.
    List<(string Output, Guid? To, string Pin)> Targets(Dictionary<string, string>? links)
    {
        var targets = new List<(string, Guid?, string)>();
        if (links is null)
            return targets;
        var known = StepIds();
        foreach (var (output, text) in links)
        {
            if (text.Length == 0)
            {
                targets.Add((output, null, ""));
                continue;
            }
            var cut = text.IndexOf(':');
            var id = UnrealGuid(cut < 0 ? text : text[..cut]);
            if (!known.Contains(id) && !reserved.Contains(id))
                throw new EditException($"Output {output} leads to {text}, but {Path.GetFileName(File)} has no step {(cut < 0 ? text : text[..cut])}.");
            targets.Add((output, id, cut < 0 ? "In" : text[(cut + 1)..]));
        }
        return targets;
    }

    void Link(List<PropertyData> data, List<(string Output, Guid? To, string Pin)> targets)
    {
        if (targets.Count == 0)
            return;
        var connections = Connections(data);
        if (connections is null)
        {
            connections = (MapPropertyData)Template().Clone();
            connections.Value.Clear();
            data.Add(connections);
        }
        var (templateKey, templateValue) = Template().Value.First();
        foreach (var (output, to, pin) in targets)
        {
            var old = connections.Value.Keys.FirstOrDefault(k => k is NamePropertyData n && n.Value.ToString() == output);
            if (old is not null)
                connections.Value.Remove(old);
            if (to is null)
                continue;
            var key = (NamePropertyData)templateKey.Clone();
            key.Value = Name(output);
            var value = (StructPropertyData)templateValue.Clone();
            var target = (StructPropertyData)value.Value.First(p => p.Name.ToString() == "NodeGuid");
            ((GuidPropertyData)target.Value[0]).Value = to.Value;
            ((NamePropertyData)value.Value.First(p => p.Name.ToString() == "PinName")).Value = Name(pin);
            connections.Value.Add(key, value);
        }
    }

    // Any link in the graph, as the pattern for new ones: every graph has at least its start step's.
    MapPropertyData Template() =>
        asset.Exports.OfType<NormalExport>().Select(e => Connections(e.Data)).FirstOrDefault(m => m is { Value.Count: > 0 })
        ?? throw new EditException($"{Path.GetFileName(File)} has no links to copy the layout of.");

    // Projects write a step's NodeGuid the way the game's tools show it: four hex numbers, as in 3FC7D895-4BFD1ED9-8EF446A4-A5C5CCEA.
    static Guid UnrealGuid(string text)
    {
        var parts = text.Split('-');
        if (parts.Length != 4 || !parts.All(p => uint.TryParse(p, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)))
            throw new EditException($"{text} isn't a step id, which is written as four hex numbers like 3FC7D895-4BFD1ED9-8EF446A4-A5C5CCEA.");
        var bytes = parts.SelectMany(p => BitConverter.GetBytes(uint.Parse(p, NumberStyles.HexNumber, CultureInfo.InvariantCulture))).ToArray();
        return new Guid(bytes);
    }

    static Guid? NodeGuid(NormalExport export) =>
        export.Data.FirstOrDefault(p => p.Name.ToString() == "NodeGuid") is StructPropertyData { Value: [GuidPropertyData id] }
            ? id.Value
            : null;

    Import Import(FPackageIndex index) =>
        index.IsImport() ? index.ToImport(asset) : throw new EditException("That step's kind isn't one Cardinal can change.");

    UsmapSchema Schema(string name) =>
        asset.Mappings.Schemas.TryGetValue(name, out var schema)
            ? schema
            : throw new EditException($"The game has no kind of step called {name}.");

    IEnumerable<UsmapSchema> Lineage(UsmapSchema schema)
    {
        for (var s = schema; s is not null; s = s.SuperType is not null && asset.Mappings.Schemas.TryGetValue(s.SuperType, out var super) ? super : null)
            yield return s;
    }

    PropertyData NewProperty(UsmapProperty field)
    {
        var name = Name(field.Name);
        return field.PropertyData switch
        {
            { Type: EPropertyType.BoolProperty } => new BoolPropertyData(name),
            { Type: EPropertyType.IntProperty } => new IntPropertyData(name),
            { Type: EPropertyType.FloatProperty } => new FloatPropertyData(name),
            { Type: EPropertyType.NameProperty } => new NamePropertyData(name),
            { Type: EPropertyType.StrProperty } => new StrPropertyData(name),
            UsmapEnumData e => new EnumPropertyData(name) { EnumType = Name(e.Name) },
            _ => throw new EditException($"{field.Name} is a {field.PropertyData.Type} field, which Cardinal can't set on a step yet."),
        };
    }

    FPackageIndex FindOrAddImport(string classPackage, string className, FPackageIndex outer, string objectName)
    {
        for (var i = 0; i < asset.Imports.Count; i++)
        {
            var import = asset.Imports[i];
            if (import.ClassPackage.ToString() == classPackage && import.ClassName.ToString() == className
                && import.OuterIndex.Index == outer.Index && import.ObjectName.ToString() == objectName)
                return FPackageIndex.FromImport(i);
        }
        return asset.AddImport(new Import(Name(classPackage), Name(className), outer, Name(objectName), false));
    }

    FStringTable StringTable() =>
        (Problem is null ? asset.Exports.OfType<StringTableExport>().FirstOrDefault()?.Table : null)
        ?? throw new EditException(Problem ?? $"{Path.GetFileName(File)} isn't a string table.");

    DataTableExport DataTable() =>
        asset.Exports.OfType<DataTableExport>().FirstOrDefault()
        ?? throw new EditException($"{Path.GetFileName(File)} isn't a data table.");

    MapPropertyData Map(EditTarget target) =>
        asset.Exports.OfType<NormalExport>().SelectMany(e => e.Data).OfType<MapPropertyData>()
            .FirstOrDefault(m => m.Name.ToString() == target.Map)
        ?? throw new EditException($"{target.Map} wasn't found in {Path.GetFileName(File)}.");

    List<PropertyData> RowFields(EditTarget target, string row)
    {
        if (target.Map is null)
            return DataTable().Table.Data.FirstOrDefault(r => r.Name.ToString() == row)?.Value
                   ?? throw new EditException($"There's no row {row}.");

        foreach (var (key, value) in Map(target).Value)
            if (KeyText(key) == row)
                return value is StructPropertyData entry
                    ? entry.Value
                    : throw new EditException($"Row {row} isn't a group of fields.");
        throw new EditException($"There's no row {row}.");
    }

    // Matches how CUE4Parse writes map keys, which is where the row keys in the browser come from.
    static string? KeyText(PropertyData key) => key switch
    {
        IntPropertyData i => i.Value.ToString(CultureInfo.InvariantCulture),
        NamePropertyData n => n.Value.ToString(),
        StrPropertyData s => s.Value?.Value,
        EnumPropertyData e => e.Value.ToString(),
        _ => null,
    };

    void SetValue(PropertyData property, string text, bool commit)
    {
        var invariant = CultureInfo.InvariantCulture;
        switch (property)
        {
            case BoolPropertyData b:
                var flag = bool.TryParse(text, out var parsed) ? parsed : throw Expected("true or false", property);
                if (commit) b.Value = flag;
                break;
            case IntPropertyData i:
                var whole = int.TryParse(text, NumberStyles.Integer, invariant, out var n) ? n : throw Expected("a whole number", property);
                if (commit) i.Value = whole;
                break;
            case Int64PropertyData l:
                var big = long.TryParse(text, NumberStyles.Integer, invariant, out var n64) ? n64 : throw Expected("a whole number", property);
                if (commit) l.Value = big;
                break;
            case UInt32PropertyData u:
                var positive = uint.TryParse(text, NumberStyles.Integer, invariant, out var u32) ? u32 : throw Expected("a whole number of 0 or more", property);
                if (commit) u.Value = positive;
                break;
            case FloatPropertyData f:
                var single = float.TryParse(text, NumberStyles.Float, invariant, out var f32) ? f32 : throw Expected("a number", property);
                if (commit) f.Value = single;
                break;
            case DoublePropertyData d:
                var precise = double.TryParse(text, NumberStyles.Float, invariant, out var f64) ? f64 : throw Expected("a number", property);
                if (commit) d.Value = precise;
                break;
            case NamePropertyData name:
                if (commit) name.Value = Name(text);
                break;
            case StrPropertyData str:
                if (commit) str.Value = new FString(text);
                break;
            case EnumPropertyData enumeration:
                var value = EnumValue(enumeration.EnumType?.ToString(), text, property);
                if (commit) enumeration.Value = Name(value);
                break;
            case BytePropertyData { ByteType: BytePropertyType.FName } byteEnum:
                var byteValue = EnumValue(byteEnum.EnumType?.ToString(), text, property);
                if (commit) byteEnum.EnumValue = Name(byteValue);
                break;
            case BytePropertyData plainByte:
                var small = byte.TryParse(text, NumberStyles.Integer, invariant, out var b8) ? b8 : throw Expected("a whole number from 0 to 255", property);
                if (commit) plainByte.Value = small;
                break;
            case ArrayPropertyData list:
                if (text.Trim() != "[]")
                    throw Expected("[] to empty it, the only change Cardinal can make to a list so far", property);
                if (commit) list.Value = [];
                break;
            default:
                throw new EditException($"{property.Name} is a {property.PropertyType} field, which Cardinal can't edit yet.");
        }
    }

    FName Name(string text)
    {
        asset.AddNameReference(new FString(text));
        return FName.FromString(asset, text);
    }

    // Enum values are stored as EType::Value. Typing just the value part is fine, and the mappings list the allowed values.
    string EnumValue(string? enumType, string text, PropertyData property)
    {
        if (string.IsNullOrWhiteSpace(enumType) || enumType == "None")
            return text.Contains("::") ? text : throw Expected("a value written as EType::Value", property);
        var value = text.Contains("::") ? text : $"{enumType}::{text}";
        if (asset.Mappings.EnumMap.TryGetValue(enumType, out var known))
        {
            var allowed = known.Values.Values.Select(v => v.Contains("::") ? v : $"{enumType}::{v}").ToList();
            if (!allowed.Contains(value))
                throw new EditException($"{property.Name} must be one of: {string.Join(", ", allowed.Select(a => a[(a.IndexOf("::") + 2)..]))}.");
        }
        return value;
    }

    static EditException Expected(string what, PropertyData property) => new($"{property.Name} needs {what}.");
}

public sealed class EditException(string message) : Exception(message);
