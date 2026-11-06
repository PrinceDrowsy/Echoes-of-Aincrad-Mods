using System.Globalization;
using System.Text;
using Cardinal.Maps;
using Cardinal.Tables;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Cardinal;

public sealed record TableResult(string Id, string Group, string Name, string Source, int Rows, string? Error);

/// <summary>Writes tables and maps as JSON and CSV. Field names here are documented in the README; keep them stable.</summary>
public static class Export
{
    public const int FormatVersion = 1;

    static readonly JsonSerializer Json = JsonSerializer.Create(new JsonSerializerSettings
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Formatting = Formatting.Indented,
    });

    public static void Table(Table table, string folder, string fileName)
    {
        Directory.CreateDirectory(folder);
        var json = new JObject
        {
            ["formatVersion"] = FormatVersion,
            ["table"] = table.Name,
            ["source"] = table.Source,
            ["rows"] = new JArray(table.Rows.Select(r => new JObject
            {
                ["key"] = r.Key,
                ["fields"] = r.Fields,
                ["text"] = JObject.FromObject(r.Text),
            })),
        };
        File.WriteAllText(Path.Combine(folder, fileName + ".json"), json.ToString(Formatting.Indented));
        WriteCsv(Path.Combine(folder, fileName + ".csv"), table.Columns, table.Cells);
    }

    public static void Map(FloorMap map, IReadOnlyList<MapMarker> markers, string folder)
    {
        Directory.CreateDirectory(folder);
        var json = new JObject
        {
            ["formatVersion"] = FormatVersion,
            ["floor"] = map.Id,
            ["map"] = map.MapPath,
            ["layers"] = JArray.FromObject(map.Layers, Json),
            ["cells"] = JArray.FromObject(map.Cells, Json),
            ["markers"] = JArray.FromObject(markers, Json),
        };
        File.WriteAllText(Path.Combine(folder, $"{map.Id}.json"), json.ToString(Formatting.Indented));

        WriteCsv(Path.Combine(folder, $"{map.Id}-cells.csv"),
            ["package", "grid", "level", "x", "y", "centerX", "centerY", "centerZ", "size",
             "contentMinX", "contentMinY", "contentMinZ", "contentMaxX", "contentMaxY", "contentMaxZ", "layers"],
            map.Cells.Select(c => new[]
            {
                c.Package, c.Grid, Num(c.Level), Num(c.X), Num(c.Y), Num(c.Center.X), Num(c.Center.Y), Num(c.Center.Z), Num(c.Size),
                Num(c.ContentBounds?.Min.X), Num(c.ContentBounds?.Min.Y), Num(c.ContentBounds?.Min.Z),
                Num(c.ContentBounds?.Max.X), Num(c.ContentBounds?.Max.Y), Num(c.ContentBounds?.Max.Z),
                string.Join(";", c.Layers),
            }).ToList());

        WriteCsv(Path.Combine(folder, $"{map.Id}-markers.csv"),
            ["layer", "cell", "kind", "label", "x", "y", "z", "yaw", "radiusMeters"],
            markers.Select(m => new[]
            {
                m.Layer, m.Cell, m.Kind, m.Label, Num(m.Location.X), Num(m.Location.Y), Num(m.Location.Z), Num(m.Yaw), Num(m.RadiusMeters),
            }).ToList());
    }

    /// <summary>Exports each table to folder/Group/Name.json and .csv, plus an index. A table that fails is listed with its error.</summary>
    public static IReadOnlyList<TableResult> Tables(Game game, IEnumerable<TableSource> sources, string folder, Action<string>? progress = null)
    {
        var results = new List<TableResult>();
        foreach (var source in sources)
        {
            progress?.Invoke(source.Id);
            try
            {
                var table = source.Load(game);
                Table(table, Path.Combine(folder, source.Group), source.Name);
                results.Add(new TableResult(source.Id, source.Group, source.Name, source.Source, table.Rows.Count, null));
            }
            catch (Exception e) when (e is not IOException and not UnauthorizedAccessException)
            {
                results.Add(new TableResult(source.Id, source.Group, source.Name, source.Source, 0, e.Message));
            }
        }
        var index = new JObject
        {
            ["formatVersion"] = FormatVersion,
            ["tables"] = JArray.FromObject(results, Json),
        };
        File.WriteAllText(Path.Combine(folder, "tables.json"), index.ToString(Formatting.Indented));
        return results;
    }

    /// <summary>Exports a floor's layout with the markers from all its developer layers.</summary>
    public static FloorMap Floor(Game game, string floor, string folder)
    {
        var map = FloorMap.Load(game, floor);
        var markers = map.Layers.Where(l => l.Developer && l.CellCount > 0)
            .SelectMany(l => map.LoadMarkers(game, l.Name))
            .ToList();
        Map(map, markers, folder);
        return map;
    }

    static string Num(double? value) => value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "";

    internal static void WriteCsv(string path, IReadOnlyList<string> columns, IReadOnlyList<string[]> rows)
    {
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(",", columns.Select(Quote)));
        foreach (var row in rows)
            csv.AppendLine(string.Join(",", row.Select(Quote)));
        // The BOM makes Excel read the file as UTF-8.
        File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true));
    }

    static string Quote(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
}
