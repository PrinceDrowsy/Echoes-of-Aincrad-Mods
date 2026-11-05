using System.Globalization;
using System.Text.RegularExpressions;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Core.i18N;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.UObject;

namespace Cardinal.Maps;

public sealed record Vector3(double X, double Y, double Z)
{
    public static Vector3 From(FVector v) => new(v.X, v.Y, v.Z);
}

public sealed record Bounds(Vector3 Min, Vector3 Max);

/// <summary>One World Partition streaming cell. X and Y are grid indices at the cell's own level.</summary>
public sealed record MapCell(string Package, string Grid, int Level, int X, int Y, Vector3 Center, double Size,
    Bounds? ContentBounds, IReadOnlyList<string> Layers);

public sealed record MapLayer(string Name, string? Parent, bool Developer, int CellCount);

/// <summary>An actor placed in a cell, such as a developer's annotation. Radius comes from labels like "Elite Combat 20.0m".</summary>
public sealed record MapMarker(string Layer, string Cell, string Kind, string Label, Vector3 Location, double Yaw, double? RadiusMeters);

/// <summary>A floor's World Partition layout, read from its main map without loading the cells themselves.</summary>
public sealed partial class FloorMap
{
    // Layers the level designers left in the shipped maps (annotations, size references, blockout); everything else is part of the world.
    // Blockout_WorldAreas also parents Floor 2's regions, so the game probably keeps it loaded.
    static readonly HashSet<string> DeveloperLayers =
        ["Measurements", "GimmickTools", "Reference", "Blockout_WorldAreas", "Legend", "Legends", "AreaHex", "Route_Arrows"];

    public string Id { get; }
    public string MapPath { get; }
    public IReadOnlyList<MapCell> Cells { get; }
    public IReadOnlyList<MapLayer> Layers { get; }
    readonly string cellFolder;

    FloorMap(string id, string mapPath, IReadOnlyList<MapCell> cells, IReadOnlyList<MapLayer> layers)
    {
        Id = id;
        MapPath = mapPath;
        Cells = cells;
        Layers = layers;
        cellFolder = mapPath[..^".umap".Length] + "/_Generated_/";
    }

    /// <summary>Floor IDs such as WL01, for every floor that has a World Partition map.</summary>
    public static IReadOnlyList<string> Floors(Game game) =>
        game.FindFiles("ROD/Maps/Main/", ".umap")
            .Select(p => FloorFile().Match(p))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

    public static FloorMap Load(Game game, string floor)
    {
        var path = game.FindFiles("ROD/Maps/Main/", ".umap")
            .FirstOrDefault(p => FloorFile().Match(p) is { Success: true } m && m.Groups[1].Value.Equals(floor, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"No World Partition map for floor {floor}.");
        var exports = game.Package(path).GetExports().ToList();

        var layerAssets = new Dictionary<string, (string Asset, string? Parent)>();
        foreach (var e in exports.Where(e => e.ExportType == "DataLayerInstanceWithAsset"))
        {
            var parent = e.GetOrDefault<FPackageIndex?>("Parent");
            layerAssets[e.Name] = (e.GetOrDefault<FPackageIndex>("DataLayerAsset").Name, parent is { IsNull: false } ? parent.Name : null);
        }

        var cellLayers = exports.Where(e => e.ExportType == "WorldPartitionRuntimeLevelStreamingCell")
            .ToDictionary(e => e.Name, e => e.GetOrDefault<FName[]>("DataLayers", [])
                .Select(n => layerAssets.TryGetValue(n.Text, out var l) ? l.Asset : n.Text).ToArray());

        var cells = new List<MapCell>();
        foreach (var e in exports.Where(e => e.ExportType == "WorldPartitionRuntimeCellDataSpatialHash"))
        {
            var name = e.Outer!.Name.Text;
            var center = e.GetOrDefault<FVector>("position");
            var size = e.GetOrDefault<float>("Extent") * 2.0;
            var content = e.GetOrDefault<FBox?>("ContentBounds");
            cells.Add(new MapCell(name, e.GetOrDefault<FName>("GridName").Text, e.GetOrDefault<int>("Level"),
                (int)Math.Round(center.X / size), (int)Math.Round(center.Y / size), Vector3.From(center), size,
                content is { IsValid: > 0 } box ? new Bounds(Vector3.From(box.Min), Vector3.From(box.Max)) : null,
                cellLayers.GetValueOrDefault(name, [])));
        }

        var counts = cells.SelectMany(c => c.Layers).GroupBy(l => l).ToDictionary(g => g.Key, g => g.Count());
        var parentAssets = layerAssets.ToDictionary(l => l.Key, l => l.Value.Asset);
        var layers = layerAssets.Values
            .Select(l => new MapLayer(l.Asset, l.Parent is null ? null : parentAssets.GetValueOrDefault(l.Parent, l.Parent),
                DeveloperLayers.Contains(l.Asset), counts.GetValueOrDefault(l.Asset)))
            .OrderBy(l => l.Developer).ThenBy(l => l.Name)
            .ToList();

        return new FloorMap(floor.ToUpperInvariant(), path, cells, layers);
    }

    /// <summary>Reads the actors in every cell of one layer. Loads those cells, so it's slower than the layout.</summary>
    public IReadOnlyList<MapMarker> LoadMarkers(Game game, string layer)
    {
        var markers = new List<MapMarker>();
        foreach (var cell in Cells.Where(c => c.Layers.Contains(layer)))
        {
            var exports = game.Package(cellFolder + cell.Package + ".umap").GetExports().ToList();
            var components = exports.Where(e => e.Outer?.Outer?.Class?.Name.Text == "Level").ToLookup(e => e.Outer!.Name.Text);
            foreach (var actor in exports.Where(e => e.Outer?.Class?.Name.Text == "Level"))
            {
                var parts = components[actor.Name].ToList();
                var root = parts.FirstOrDefault(c => c.Properties.Any(p => p.Name.Text == "RelativeLocation")
                                                     && c.Properties.All(p => p.Name.Text != "AttachParent"));
                if (root is null)
                    continue;
                var label = string.Join(" / ", parts.Where(c => c.ExportType == "TextRenderComponent")
                    .Select(c => c.GetOrDefault<FText?>("Text")?.Text)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => Whitespace().Replace(t!, " ").Trim())
                    .Distinct());
                if (label.Length == 0)
                    label = parts.Select(c => c.GetOrDefault<FPackageIndex?>("StaticMesh"))
                        .FirstOrDefault(m => m is { IsNull: false })?.Name ?? "";
                var radius = Radius().Match(label);
                markers.Add(new MapMarker(layer, cell.Package, Kind(actor.ExportType), label,
                    Vector3.From(root.GetOrDefault<FVector>("RelativeLocation")),
                    root.GetOrDefault<FRotator>("RelativeRotation").Yaw,
                    radius.Success ? double.Parse(radius.Groups[1].Value, CultureInfo.InvariantCulture) : null));
            }
        }
        return markers;
    }

    static string Kind(string type) => type.EndsWith("_C") ? type[..^2] : type;

    [GeneratedRegex(@"/PL_(WL\d+)_WP\.umap$", RegexOptions.IgnoreCase)]
    private static partial Regex FloorFile();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\b(\d+(?:\.\d+)?)m\b")]
    private static partial Regex Radius();
}
