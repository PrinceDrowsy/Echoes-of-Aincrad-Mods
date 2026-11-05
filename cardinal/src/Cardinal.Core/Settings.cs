using System.Text.Json;
using Microsoft.Win32;

namespace Cardinal;

/// <summary>What Cardinal needs to read a player's own copy of the game, saved in their AppData.</summary>
public sealed class Settings
{
    public string? PaksFolder { get; set; }
    public string? AesKey { get; set; }
    public string? MappingsFile { get; set; }
    public string? OodleFile { get; set; }

    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cardinal");

    static string FilePath => Path.Combine(Folder, "settings.json");

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static Settings Load()
    {
        var settings = File.Exists(FilePath)
            ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings()
            : new Settings();
        settings.PaksFolder ??= FindPaksFolder();
        settings.OodleFile ??= FindOodle();
        settings.MappingsFile = NewerMappings(settings.MappingsFile, settings.PaksFolder);
        return settings;
    }

    // The Cardinal Mappings companion mod dumps a fresh .usmap next to UE4SS after each game update,
    // so a newer one there replaces whatever was chosen before.
    static string? NewerMappings(string? current, string? paks)
    {
        if (paks is null)
            return current;
        var win64 = Path.GetFullPath(Path.Combine(paks, "..", "..", "Binaries", "Win64"));
        var newest = new[] { win64, Path.Combine(win64, "ue4ss") }
            .Where(Directory.Exists)
            .SelectMany(dir => new DirectoryInfo(dir).EnumerateFiles("*.usmap"))
            .MaxBy(file => file.LastWriteTimeUtc);
        if (newest is null)
            return current;
        if (current is not null && File.Exists(current) && File.GetLastWriteTimeUtc(current) >= newest.LastWriteTimeUtc)
            return current;
        return newest.FullName;
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>Returns what's missing or wrong, or nothing when the game can be opened.</summary>
    public IEnumerable<string> Problems()
    {
        if (string.IsNullOrWhiteSpace(PaksFolder) || !Directory.Exists(PaksFolder))
            yield return "The game's Paks folder isn't set or doesn't exist.";
        else if (!Directory.EnumerateFiles(PaksFolder, "*.utoc").Any())
            yield return "The Paks folder has no .utoc files. It should be EchoesofAincrad\\Content\\Paks.";
        if (!IsValidKey(AesKey))
            yield return "The AES key is missing or isn't 64 hex digits (an optional 0x in front is fine).";
        if (string.IsNullOrWhiteSpace(MappingsFile) || !File.Exists(MappingsFile))
            yield return "The mappings (.usmap) file isn't set or doesn't exist.";
        if (string.IsNullOrWhiteSpace(OodleFile) || !File.Exists(OodleFile))
            yield return "The Oodle library isn't set. Choose one, or download it from the setup screen.";
    }

    static bool IsValidKey(string? key)
    {
        var hex = key?.Trim();
        if (hex is null)
            return false;
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = hex[2..];
        return hex.Length == 64 && hex.All(Uri.IsHexDigit);
    }

    // The game can be in any Steam library, and libraryfolders.vdf says which one holds it.
    public static string? FindPaksFolder()
    {
        if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is not string steam)
            return null;
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf))
            return null;

        string? library = null;
        foreach (var line in File.ReadLines(vdf))
        {
            var parts = line.Split('"', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(p => p.Length > 0).ToArray();
            if (parts.Length == 2 && parts[0] == "path")
                library = parts[1].Replace(@"\\", @"\");
            else if (parts.Length == 2 && parts[0] == Game.SteamAppId && library is not null)
            {
                var paks = Path.Combine(library, "steamapps", "common", "Echoes of Aincrad", "EchoesofAincrad", "Content", "Paks");
                return Directory.Exists(paks) ? paks : null;
            }
        }
        return null;
    }

    /// <summary>Downloads the Oodle library the same way FModel does, from the OodleUE builds on GitHub.</summary>
    public static async Task<string> DownloadOodle()
    {
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, "oodle-data-shared.dll");
        using var client = new HttpClient();
        if (!await CUE4Parse.Compression.OodleHelper.DownloadOodleDllFromOodleUEAsync(client, path))
            throw new SetupException("Downloading Oodle failed. Check the internet connection, or choose an Oodle DLL by hand.");
        return path;
    }

    static string? FindOodle()
    {
        var names = new[] { "oodle-data-shared.dll", "oo2core_9_win64.dll" };
        return new[] { Folder, AppContext.BaseDirectory }
            .SelectMany(dir => names.Select(name => Path.Combine(dir, name)))
            .FirstOrDefault(File.Exists);
    }
}
