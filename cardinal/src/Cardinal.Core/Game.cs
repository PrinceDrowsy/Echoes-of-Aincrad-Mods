using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;

namespace Cardinal;

public class SetupException(string message) : Exception(message);

/// <summary>The player's own game files, opened read-only through CUE4Parse.</summary>
public sealed class Game : IDisposable
{
    public const string SteamAppId = "2244210";
    public const string ContentRoot = "EchoesofAincrad/Content/";
    const string TextNamespace = "ST_GeneralLocalizeList";
    const string TextTablePath = ContentRoot + "ROD/DataAssets/StringTable/ST_GeneralLocalizeList.uasset";

    public DefaultFileProvider Provider { get; }
    readonly HashSet<string> textKeys;

    Game(DefaultFileProvider provider, HashSet<string> textKeys)
    {
        Provider = provider;
        this.textKeys = textKeys;
    }

    /// <summary>
    /// Opens the unmodded game by default. With installed mods included, files in Paks\~mods override the
    /// originals as they do in game, which is what you want to inspect but never what an edit should start from.
    /// </summary>
    public static Game Open(Settings settings, bool withMods = false)
    {
        var problems = settings.Problems().ToList();
        if (problems.Count > 0)
            throw new SetupException(string.Join(Environment.NewLine, problems));

        OodleHelper.Initialize(settings.OodleFile);
        var provider = new DefaultFileProvider(settings.PaksFolder!,
            withMods ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly,
            new VersionContainer(EGame.GAME_UE5_3));
        provider.MappingsContainer = new FileUsmapTypeMappingsProvider(settings.MappingsFile!);
        provider.Initialize();
        provider.SubmitKey(new FGuid(), new FAesKey(settings.HexKey()));
        provider.PostMount();

        if (!provider.Files.ContainsKey(TextTablePath))
        {
            provider.Dispose();
            throw new SetupException("The game files didn't open. Check that the AES key matches this version of the game.");
        }
        provider.LoadLocalization(ELanguage.English);

        var keys = provider.LoadPackageObject<CUE4Parse.UE4.Assets.Exports.Internationalization.UStringTable>(ObjectPath(TextTablePath))
            .StringTable.KeysToEntries.Keys.ToHashSet();
        return new Game(provider, keys);
    }

    // CUE4Parse wants Folder/Name.Name, not a file name with its extension.
    static string ObjectPath(string file)
    {
        var path = file[..file.LastIndexOf('.')];
        return $"{path}.{Path.GetFileName(path)}";
    }

    /// <summary>English text for one of the game's text keys, such as ItemName_Material_1.</summary>
    public string? Text(string? key) =>
        key is not null && textKeys.Contains(key) ? Provider.Internationalization.SafeGet(TextNamespace, key, key) : null;

    public IEnumerable<string> FindFiles(string folder, string extension) =>
        Provider.Files.Keys
            .Where(path => path.StartsWith(ContentRoot + folder, StringComparison.OrdinalIgnoreCase)
                           && path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase);

    public IPackage Package(string path) => Provider.LoadPackage(path);

    public T Load<T>(string path) where T : UObject => Provider.LoadPackageObject<T>(ObjectPath(path));

    public void Dispose() => Provider.Dispose();
}
