using System.Runtime.InteropServices;
using System.Text.Json;

namespace Cardinal.Tests;

[CollectionDefinition("Game")]
public sealed class GameCollection : ICollectionFixture<GameFixture>;

/// <summary>
/// The tester's own game, opened once for every test that needs it, from CARDINAL_TEST_SETTINGS (a settings.json) or
/// Cardinal's saved settings. Its Paks folder is a folder of hard links to the real containers, so mods the tests
/// install never reach the game's own ~mods. Without a set-up game, those tests are skipped.
/// </summary>
public sealed class GameFixture : IDisposable
{
    readonly string? skip;
    readonly string? folder;
    readonly Game? game;

    public Settings Settings { get; } = new();

    public GameFixture()
    {
        var settings = Environment.GetEnvironmentVariable("CARDINAL_TEST_SETTINGS") is { Length: > 0 } file
            ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(file))!
            : Settings.Load();
        if (settings.Problems().FirstOrDefault() is { } problem)
        {
            skip = $"No game is set up for tests. {problem}";
            return;
        }

        folder = Path.Combine(Path.GetTempPath(), "CardinalTests", Guid.NewGuid().ToString("N"));
        var paks = Path.Combine(folder, "Paks");
        Directory.CreateDirectory(paks);
        foreach (var container in Directory.EnumerateFiles(settings.PaksFolder!))
            if (!CreateHardLink(Path.Combine(paks, Path.GetFileName(container)), container, IntPtr.Zero))
            {
                skip = $"The game's containers couldn't be linked into {paks}. Hard links need the temp folder on the game's drive.";
                return;
            }
        settings.PaksFolder = paks;
        // The checks expect the game's English, whatever language Cardinal is set to show.
        settings.TextLanguage = Cardinal.Text.GameText.English;
        Settings = settings;
        game = Game.Open(settings);
    }

    public Game Require()
    {
        if (skip is not null)
            Assert.Skip(skip);
        return game!;
    }

    public string Mods => Path.Combine(Settings.PaksFolder!, "~mods");

    public void ClearMods()
    {
        if (Directory.Exists(Mods))
            Directory.Delete(Mods, true);
    }

    public void Dispose()
    {
        game?.Dispose();
        if (folder is not null && Directory.Exists(folder))
            Directory.Delete(folder, true);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CreateHardLink(string link, string target, IntPtr security);
}
