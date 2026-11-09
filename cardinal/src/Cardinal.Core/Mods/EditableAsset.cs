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
        var oldClass = Import(node.ClassIndex);
        var schema = Schema(change.Kind);
        if (!Lineage(schema).Any(s => s.Name == "FlowNode"))
            throw new EditException($"{change.Kind} isn't a kind of Flow step.");
        var fields = Lineage(schema).SelectMany(s => s.Properties.Values).ToDictionary(p => p.Name);

        var kept = node.Data.Where(p => fields.TryGetValue(p.Name.ToString(), out var field)
                                        && field.PropertyData.Type.ToString() == p.PropertyType.ToString()).ToList();
        foreach (var (name, text) in change.Fields)
        {
            var property = kept.FirstOrDefault(p => p.Name.ToString() == name);
            if (property is null)
            {
                property = NewProperty(fields.GetValueOrDefault(name)
                                       ?? throw new EditException($"{change.Kind} has no field {name}."));
                kept.Add(property);
            }
            SetValue(property, text, true);
        }

        var connections = kept.OfType<MapPropertyData>().FirstOrDefault(m => m.Name.ToString() == "Connections");
        var renamed = new List<(NamePropertyData Pin, string To)>();
        foreach (var (from, to) in change.Outputs)
        {
            var pin = connections?.Value.Keys.OfType<NamePropertyData>().FirstOrDefault(k => k.Value.ToString() == from)
                      ?? throw new EditException($"Step {change.Node} has no connected output {from}.");
            renamed.Add((pin, to));
        }
        if (!commit)
            return;

        foreach (var (pin, to) in renamed)
            pin.Value = Name(to);
        node.Data = kept;
        // The new kind comes from the same script package as the old one, as Flow steps in one game module do.
        var package = oldClass.OuterIndex;
        node.ClassIndex = FindOrAddImport("/Script/CoreUObject", "Class", package, change.Kind);
        node.TemplateIndex = FindOrAddImport(Import(package).ObjectName.ToString(), change.Kind, package, "Default__" + change.Kind);
    }

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
