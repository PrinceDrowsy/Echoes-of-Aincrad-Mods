using Cardinal.Text;
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
    // The string table nearly all of the game's text keys live in. Its namespace in the .locres files is its own name.
    public const string TextNamespace = "ST_GeneralLocalizeList";
    public const string TextTablePath = ContentRoot + "ROD/DataAssets/StringTable/ST_GeneralLocalizeList.uasset";

    public DefaultFileProvider Provider { get; }
    // The main string table's keys and their English, which shows wherever a language has no line of its own.
    readonly Dictionary<string, string> sourceText;
    readonly Locres english;
    readonly Locres language;

    /// <summary>The culture Text shows the game's text in: the setting's, or English when the game has no text for it.</summary>
    public string Language { get; }

    Game(DefaultFileProvider provider, Dictionary<string, string> sourceText, Locres english, Locres language, string culture)
    {
        Provider = provider;
        this.sourceText = sourceText;
        this.english = english;
        this.language = language;
        Language = culture;
    }

    /// <summary>
    /// Opens the unmodded game by default. With installed mods included, files in Paks\~mods override the
    /// originals as they do in game, which is what you want to inspect but never what an edit should start from.
    /// </summary>
    public static Game Open(Settings settings, bool withMods = false)
    {
        var provider = Mount(settings, withMods);
        var source = provider.LoadPackageObject<CUE4Parse.UE4.Assets.Exports.Internationalization.UStringTable>(ObjectPath(TextTablePath))
            .StringTable.KeysToEntries.ToDictionary(e => e.Key, e => e.Value);
        // Only the copy the game loads. CUE4Parse's LoadLocalization merges every copy, and the game's own can win over a mod's.
        var english = Locres.Read(provider.SaveAsset(GameText.FilePath(GameText.English)));
        var culture = provider.Files.ContainsKey(GameText.FilePath(settings.TextLanguage)) ? settings.TextLanguage : GameText.English;
        var language = culture == GameText.English ? english : Locres.Read(provider.SaveAsset(GameText.FilePath(culture)));
        return new Game(provider, source, english, language, culture);
    }

    /// <summary>Mounts the game's containers, and with withMods everything under Paks too, without reading any text.</summary>
    public static DefaultFileProvider Mount(Settings settings, bool withMods)
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
        return provider;
    }

    // CUE4Parse wants Folder/Name.Name, not a file name with its extension.
    static string ObjectPath(string file)
    {
        var path = file[..file.LastIndexOf('.')];
        return $"{path}.{Path.GetFileName(path)}";
    }

    /// <summary>
    /// The text for one of the game's text keys, such as ItemName_Material_1, in Language. A line that language lacks
    /// shows in English, as it does in game.
    /// </summary>
    public string? Text(string? key) =>
        key is not null && sourceText.TryGetValue(key, out var source)
            ? language.Get(TextNamespace, key) ?? english.Get(TextNamespace, key) ?? source
            : null;

    public bool HasText(string key) => sourceText.ContainsKey(key);

    /// <summary>A main string table key's own English, before any language's line replaces it.</summary>
    public string? SourceText(string key) => sourceText.GetValueOrDefault(key);

    public IEnumerable<string> FindFiles(string folder, string extension) =>
        Provider.Files.Keys
            .Where(path => path.StartsWith(ContentRoot + folder, StringComparison.OrdinalIgnoreCase)
                           && path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase);

    public IPackage Package(string path) => Provider.LoadPackage(path);

    public T Load<T>(string path) where T : UObject => Provider.LoadPackageObject<T>(ObjectPath(path));

    public void Dispose() => Provider.Dispose();
}
