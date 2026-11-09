using System.Globalization;

namespace Cardinal.Text;

/// <summary>
/// The game's text in each language: one Game.locres per culture under Localization/Game. The game's own update
/// replaces every one of them, and a mod can only replace a language's file whole, never single lines.
/// </summary>
public static class GameText
{
    public const string Folder = Game.ContentRoot + "Localization/Game/";
    public const string English = "en";

    public static string FilePath(string culture) => $"{Folder}{culture}/Game.locres";

    /// <summary>
    /// The languages the game has shipped since release, offered before its files are open (they're encrypted until then).
    /// Once they're open, Cultures reads the real list, so a language an update adds still shows.
    /// </summary>
    public static readonly IReadOnlyList<string> Shipped =
        ["de", "en", "en-AU", "es-419", "es-ES", "fr", "id", "it", "ja-JP", "ko", "pt-BR", "ru", "th", "zh-Hans-CN", "zh-Hant-TW"];

    /// <summary>Culture codes the game has text for, such as en and ja-JP, with their names as they appear in the game files.</summary>
    public static IReadOnlyList<string> Cultures(Game game) =>
        game.Provider.Files.Values
            .Select(file => file.Path)
            .Where(path => path.StartsWith(Folder, StringComparison.OrdinalIgnoreCase)
                           && path.EndsWith("/Game.locres", StringComparison.OrdinalIgnoreCase))
            .Select(path => path[Folder.Length..path.LastIndexOf('/')])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The culture as the game spells it, or null if the game has no text for it.</summary>
    public static string? Find(Game game, string culture) =>
        Cultures(game).FirstOrDefault(c => c.Equals(culture, StringComparison.OrdinalIgnoreCase));

    public static Locres Load(Game game, string culture) =>
        Locres.Read(game.Provider.SaveAsset(FilePath(culture)));

    /// <summary>A culture's name for menus, like "Japanese (Japan)". Unknown codes are shown as they are.</summary>
    public static string Name(string culture)
    {
        try
        {
            var info = CultureInfo.GetCultureInfo(culture);
            return info.EnglishName.StartsWith("Unknown", StringComparison.Ordinal) ? culture : $"{info.EnglishName} ({culture})";
        }
        catch (CultureNotFoundException)
        {
            return culture;
        }
    }
}
