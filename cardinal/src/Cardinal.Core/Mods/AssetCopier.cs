using System.Text.RegularExpressions;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

namespace Cardinal.Mods;

/// <summary>Writes an AssetCopy: the unmodded source asset under its new package path and name, with its exports renamed to match.</summary>
public static partial class AssetCopier
{
    /// <summary>The asset's file inside the game, such as EchoesofAincrad/Content/ROD/DataAssets/Town/Town_011.uasset.</summary>
    public static string File(string package) => Game.ContentRoot + package["/Game/".Length..] + ".uasset";

    /// <summary>Why the copy can't be made, or null when it can.</summary>
    public static string? Check(Game game, AssetRegistry registry, AssetCopy copy)
    {
        if (!copy.From.StartsWith("/Game/") || !copy.Package.StartsWith("/Game/"))
            return "both paths must start with /Game/, as in /Game/ROD/DataAssets/Town/Town_004.";
        if (!ValidName().IsMatch(copy.Name))
            return $"\"{copy.Name}\" can't be an asset name. Use letters, digits and underscores.";
        if (!game.Provider.Files.ContainsKey(File(copy.From)))
            return $"the game has no asset {copy.From} to copy.";
        if (game.Provider.Files.ContainsKey(File(copy.Package)))
            return $"the game already has an asset {copy.Package}.";
        // Only assets the registry lists can be copied, since the registry patch copies the source's entry too.
        if (!registry.Contains(copy.From))
            return $"{copy.From} isn't in the game's asset registry, so a copy of it couldn't be found by the game.";
        return null;
    }

    public static void Write(Settings settings, Usmap mappings, AssetCopy copy, string assetsFolder)
    {
        var work = EditableAsset.WorkFolder();
        try
        {
            var asset = new UAsset(Retoc.Extract(settings, File(copy.From), work), EngineVersion.VER_UE5_3, mappings);
            var oldName = copy.From[(copy.From.LastIndexOf('/') + 1)..];
            var names = asset.GetNameMapIndexList();
            for (var i = 0; i < names.Count; i++)
            {
                var name = names[i].Value;
                var renamed = name == copy.From ? copy.Package
                    : name == $"{copy.From}.{oldName}" ? $"{copy.Package}.{copy.Name}"
                    : name == oldName ? copy.Name
                    : null;
                if (renamed is not null)
                    asset.SetNameReference(i, new FString(renamed));
            }
            // A name ending in _123 is stored as its plain part plus a number, so the export of QST_Free_20001 is
            // QST_Free numbered 20002, and renaming the name map alone would leave it pointing at the old number.
            var (oldPlain, oldNumber) = AssetRegistry.Split(oldName);
            var (newPlain, newNumber) = AssetRegistry.Split(copy.Name);
            foreach (var export in asset.Exports.Where(e => e.ObjectName.Value.Value == oldPlain && e.ObjectName.Number == (int)oldNumber))
                export.ObjectName = new FName(asset, newPlain, (int)newNumber);
            asset.FolderName = new FString(copy.Package);

            var target = Path.Combine(assetsFolder, File(copy.Package));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            asset.Write(target);
        }
        finally
        {
            Directory.Delete(work, true);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial Regex ValidName();
}
