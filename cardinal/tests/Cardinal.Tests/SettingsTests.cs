namespace Cardinal.Tests;

public class SettingsTests
{
    static readonly string Key = new('a', 64);

    static bool Valid(string? key) => new Settings { AesKey = key }.HasValidKey;

    [Fact]
    public void Keys()
    {
        Assert.True(Valid(Key));
        Assert.True(Valid("0x" + Key));
        Assert.True(Valid($" 0X{Key} "));
        Assert.False(Valid(Key[1..]));
        Assert.False(Valid(Key[1..] + "g"));
        Assert.False(Valid(null));
    }

    [Fact]
    public void HexKeyAlwaysHasItsPrefix()
    {
        Assert.Equal("0x" + Key, new Settings { AesKey = Key }.HexKey());
        Assert.Equal("0X" + Key, new Settings { AesKey = $" 0X{Key} " }.HexKey());
    }

    [Fact]
    public void EmptySettingsListEveryProblem() =>
        Assert.Equal(4, new Settings().Problems().Count());

    [Fact]
    public void DiagnosticsNeverShowTheKeyOrPrivatePaths()
    {
        var folder = Path.Combine(Path.GetTempPath(), "CardinalTests", Guid.NewGuid().ToString("N"));
        var paks = Path.Combine(folder, "Game", "EchoesofAincrad", "Content", "Paks");
        var mappings = Path.Combine(folder, "secret.usmap");
        Directory.CreateDirectory(paks);
        File.WriteAllText(mappings, "");
        try
        {
            var report = Diagnostics.Report(new Settings { AesKey = Key, PaksFolder = paks, MappingsFile = mappings });
            Assert.DoesNotContain(Key, report);
            Assert.DoesNotContain(folder, report, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("AES key: set", report);
            Assert.Contains("secret.usmap (outside the game folder)", report);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
