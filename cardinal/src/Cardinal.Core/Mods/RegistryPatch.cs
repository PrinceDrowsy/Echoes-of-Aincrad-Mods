using System.Text.Json;

namespace Cardinal.Mods;

/// <summary>What a registry patch run did: the copies it listed, the ones it couldn't, and the .pak it wrote, if any.</summary>
public sealed record RegistryPatchResult(IReadOnlyList<AssetCopy> Added, IReadOnlyList<string> Problems, string? Pak);

/// <summary>
/// The game only finds primary assets (towns, quests) through AssetRegistry.bin, and a mod can only replace that file
/// whole, so two mods shipping their own would hide each other's assets. Instead each mod built with copied assets
/// carries a small list of them, and this patch puts every installed mod's list into one registry, starting from the
/// one the game would otherwise load so other people's registry mods keep working too.
/// </summary>
public static class RegistryPatch
{
    public const string Name = "CardinalRegistry";
    const string ListFolder = "EchoesofAincrad/Cardinal/Registry/";

    // The merged patch shares the top number but never carries a registry, so the two can't clash.
    public static string FileName => $"{Name}_{ModProject.MergedPriority}_P";

    /// <summary>Where a project's list of copies goes inside its .pak.</summary>
    public static string ListPath(ModProject project) => ListFolder + project.BaseName() + ".json";

    public static byte[] List(IEnumerable<AssetCopy> copies) =>
        JsonSerializer.SerializeToUtf8Bytes(copies, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });

    /// <summary>
    /// Rebuilds the patch in ~mods from the mods installed now, after removing the old one. With no copies installed it
    /// leaves no patch at all, so running it after uninstalling the last content mod cleans up after itself.
    /// </summary>
    public static RegistryPatchResult Build(Settings settings)
    {
        var mods = ModBuilder.ModsFolder(settings);
        var pak = Path.Combine(mods, FileName + ".pak");
        try
        {
            File.Delete(pak);
        }
        catch (IOException)
        {
            throw new IOException($"{FileName}.pak is in use. Close the game and try again.");
        }

        using var game = Game.Open(settings, true);
        var lists = game.Provider.Files.Keys
            .Where(k => k.StartsWith(ListFolder, StringComparison.OrdinalIgnoreCase) && k.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var added = new List<AssetCopy>();
        var problems = new List<string>();
        if (lists.Count == 0)
            return new RegistryPatchResult(added, problems, null);

        var registry = AssetRegistry.Read(game.Provider.SaveAsset(AssetRegistry.File));
        foreach (var list in lists)
        {
            var mod = Path.GetFileNameWithoutExtension(list);
            List<AssetCopy>? copies;
            try
            {
                copies = JsonSerializer.Deserialize<List<AssetCopy>>(game.Provider.SaveAsset(list), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            }
            catch (JsonException e)
            {
                problems.Add($"{mod}: its list of new assets can't be read ({e.Message}).");
                continue;
            }
            // A copy can be listed twice, by a mod and by a merged patch made from it, and also by another mod's registry.
            foreach (var copy in copies ?? [])
            {
                if (registry.Contains(copy.Package))
                    continue;
                try
                {
                    registry.AddCopy(copy.From, copy.Package);
                    added.Add(copy);
                }
                catch (EditException e)
                {
                    problems.Add($"{mod}: {e.Message}");
                }
            }
        }
        if (added.Count == 0)
            return new RegistryPatchResult(added, problems, null);

        var stage = EditableAsset.WorkFolder();
        try
        {
            var file = Path.Combine(stage, AssetRegistry.File);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, registry.Write());
            return new RegistryPatchResult(added, problems, Repak.Pack(stage, mods, FileName));
        }
        finally
        {
            Directory.Delete(stage, true);
        }
    }
}
