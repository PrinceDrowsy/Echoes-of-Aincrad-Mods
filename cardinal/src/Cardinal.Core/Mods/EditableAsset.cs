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
    readonly UAsset? asset;

    /// <summary>Why this asset can't be edited, or null when it can.</summary>
    public string? Problem { get; }

    public string File { get; }

    EditableAsset(string file, UAsset? asset, string? problem)
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
            var raw = asset.Exports.OfType<RawExport>().FirstOrDefault();
            return raw is null
                ? new EditableAsset(file, asset, null)
                : new EditableAsset(file, null,
                    $"Cardinal can't decode {Path.GetFileName(file)} with the current mappings, so it's read-only. " +
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
        asset!.Write(path);
    }

    void Set(EditTarget target, ModEdit edit, bool commit)
    {
        if (asset is null)
            throw new EditException(Problem!);
        var fields = RowFields(target, edit.Row);
        var parts = edit.Field.Split('.');
        PropertyData? property = null;
        foreach (var part in parts)
        {
            property = fields.FirstOrDefault(p => p.Name.ToString() == part);
            if (property is null)
                throw new EditException($"{edit.Field} isn't stored in this row. Fields left at their default value can't be edited yet.");
            if (part != parts[^1])
                fields = property is StructPropertyData nested
                    ? nested.Value
                    : throw new EditException($"{edit.Field} can't be edited: {part} isn't a plain group of fields.");
        }
        SetValue(property!, edit.Value, commit);
    }

    List<PropertyData> RowFields(EditTarget target, string row)
    {
        if (target.Map is null)
        {
            var table = asset!.Exports.OfType<DataTableExport>().FirstOrDefault()
                        ?? throw new EditException($"{Path.GetFileName(File)} isn't a data table.");
            return table.Table.Data.FirstOrDefault(r => r.Name.ToString() == row)?.Value
                   ?? throw new EditException($"There's no row {row}.");
        }

        var map = asset!.Exports.OfType<NormalExport>().SelectMany(e => e.Data).OfType<MapPropertyData>()
                      .FirstOrDefault(m => m.Name.ToString() == target.Map)
                  ?? throw new EditException($"{target.Map} wasn't found in {Path.GetFileName(File)}.");
        foreach (var (key, value) in map.Value)
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
            default:
                throw new EditException($"{property.Name} is a {property.PropertyType} field, which Cardinal can't edit yet.");
        }
    }

    FName Name(string text)
    {
        asset!.AddNameReference(new FString(text));
        return FName.FromString(asset, text);
    }

    // Enum values are stored as EType::Value. Typing just the value part is fine, and the mappings list the allowed values.
    string EnumValue(string? enumType, string text, PropertyData property)
    {
        if (string.IsNullOrWhiteSpace(enumType) || enumType == "None")
            return text.Contains("::") ? text : throw Expected("a value written as EType::Value", property);
        var value = text.Contains("::") ? text : $"{enumType}::{text}";
        if (asset!.Mappings.EnumMap.TryGetValue(enumType, out var known))
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
