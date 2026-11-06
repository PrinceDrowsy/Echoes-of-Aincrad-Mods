using CUE4Parse.UE4.VirtualFileSystem;

namespace Cardinal.Mods;

/// <summary>
/// A .pak or .utoc set the game mounts, by its file name without extension. Order is Unreal's priority for it:
/// a higher order wins when two containers hold the same asset. Installed means it sits in a folder under Paks, like ~mods.
/// </summary>
public sealed record Container(string Name, long Order, bool Installed, IReadOnlySet<string> Assets)
{
    public override string ToString() => Name;
}

/// <summary>Every container holding one asset, highest priority first. Winner is null when the top two tie.</summary>
public sealed record AssetOwners(string Asset, IReadOnlyList<Container> Holders, Container? Winner)
{
    public string Outcome()
    {
        var top = Holders.TakeWhile(h => h.Order == Holders[0].Order).ToList();
        var unused = Holders.Skip(top.Count).Where(h => h.Installed).ToList();
        var text = Winner is null
            ? $"{string.Join(" and ", top.Select(Label))} have the same priority, so which one the game loads is unpredictable."
            : $"{Label(Winner)} wins.";
        if (unused.Count > 0)
            text += $" {string.Join(" and ", unused)} {(unused.Count == 1 ? "is" : "are")} never used.";
        if (Holders.Count(h => h.Installed) > 1 && Culture() is { } culture)
            text += $" Each one holds all of the game's {Text.GameText.Name(culture)} text, so only one mod's text changes show; merge them to keep every change.";
        return text;
    }

    // A language's text file, Localization/Game/<culture>/Game, which mods can only replace whole.
    string? Culture() =>
        Asset.StartsWith(Text.GameText.Folder, StringComparison.OrdinalIgnoreCase) && Asset.EndsWith("/Game", StringComparison.OrdinalIgnoreCase)
            ? Asset[Text.GameText.Folder.Length..^"/Game".Length]
            : null;

    static string Label(Container c) => c.Installed ? c.Name : $"the game's own {c.Name}";
}

/// <summary>
/// Which installed mods replace the same game files, and which copy the game would load. Uses Unreal's rule,
/// which CUE4Parse follows: a name ending in _N_P ranks by N, and a plain _P ranks the same as the game's own _0_P patch.
/// </summary>
public sealed class ModConflicts
{
    public IReadOnlyList<Container> Mods { get; }

    /// <summary>Assets that more than one mod replaces, or that a mod replaces without outranking the game.</summary>
    public IReadOnlyList<AssetOwners> Conflicts { get; }

    readonly Dictionary<string, AssetOwners> owners;

    ModConflicts(IReadOnlyList<Container> mods, Dictionary<string, AssetOwners> owners)
    {
        Mods = mods;
        this.owners = owners;
        Conflicts = owners.Values
            .Where(o => o.Holders.Count(h => h.Installed) > 1 || o.Winner is not { Installed: true })
            .OrderBy(o => o.Asset, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static ModConflicts Scan(Settings settings)
    {
        using var provider = Game.Mount(settings, true);
        var paks = Path.GetFullPath(settings.PaksFolder!);
        var containers = provider.MountedVfs
            .GroupBy(reader => Path.ChangeExtension(Path.GetRelativePath(paks, reader.Path), null), StringComparer.OrdinalIgnoreCase)
            .Select(group => new Container(
                group.Key.Replace('\\', '/'),
                group.Max(reader => reader.ReadOrder),
                Path.GetDirectoryName(group.Key)!.Length > 0,
                group.SelectMany(Assets).ToHashSet(StringComparer.OrdinalIgnoreCase)))
            .ToList();

        var mods = containers.Where(c => c.Installed).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var holders = new Dictionary<string, List<Container>>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in mods.SelectMany(m => m.Assets))
            holders.TryAdd(asset, []);
        foreach (var container in containers)
            foreach (var asset in container.Assets)
                if (holders.TryGetValue(asset, out var list))
                    list.Add(container);

        var owners = holders.ToDictionary(h => h.Key, h =>
        {
            var sorted = h.Value.OrderByDescending(c => c.Order).ToList();
            var tie = sorted.Count > 1 && sorted[0].Order == sorted[1].Order;
            return new AssetOwners(h.Key, sorted, tie ? null : sorted[0]);
        }, StringComparer.OrdinalIgnoreCase);
        return new ModConflicts(mods, owners);
    }

    /// <summary>Who provides an asset that some installed mod replaces, or null if no mod touches it.</summary>
    public AssetOwners? Owners(string asset) => owners.GetValueOrDefault(asset);

    // A package's .uasset, .uexp and .ubulk files all count as the one asset, named by its path without extension.
    static IEnumerable<string> Assets(IAesVfsReader reader) =>
        reader.Files.Keys.Select(path =>
        {
            var name = path.LastIndexOf('/') + 1;
            var dot = path.IndexOf('.', name);
            return dot < 0 ? path : path[..dot];
        });
}
