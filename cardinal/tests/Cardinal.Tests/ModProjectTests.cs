using Cardinal.Mods;

namespace Cardinal.Tests;

public class ModProjectTests
{
    [Theory]
    [InlineData("5", "5.0")]
    [InlineData("0.5", ".5")]
    [InlineData("True", "true")]
    [InlineData("Name", "Name")]
    public void SameValues(string a, string b) => Assert.True(ModEdit.Same(a, b));

    [Theory]
    [InlineData("5", "6")]
    [InlineData("Name", "name")]
    [InlineData("5", null)]
    public void DifferentValues(string a, string? b) => Assert.False(ModEdit.Same(a, b));

    [Fact]
    public void ALaterEditKeepsTheFirstOriginal()
    {
        var project = new ModProject();
        project.Set(new ModEdit("T", "1", "F", "2", "1"));
        project.Set(new ModEdit("T", "1", "F", "3", "2"));
        var edit = Assert.Single(project.Edits);
        Assert.Equal(("3", "1"), (edit.Value, edit.Original));
        Assert.True(project.Remove("T", "1", "F"));
        Assert.False(project.Remove("T", "1", "F"));
    }

    [Fact]
    public void ALaterTextEditKeepsTheFirstOriginal()
    {
        var project = new ModProject();
        project.SetText(new TextEdit("en", "ST", "K", "b", "a"));
        project.SetText(new TextEdit("en", "ST", "K", "c", "b"));
        Assert.Equal("a", Assert.Single(project.TextEdits).Original);
    }

    [Theory]
    [InlineData("Quick Recovery", 1000, "QuickRecovery_1000_P")]
    [InlineData("Mine_P", 5, "Mine_5_P")]
    [InlineData("Mine_12_P", 5, "Mine_5_P")]
    [InlineData("!!!", 1000, "CardinalMod_1000_P")]
    public void FileNames(string name, int priority, string expected) =>
        Assert.Equal(expected, new ModProject { Name = name, Priority = priority }.FileName());

    [Fact]
    public void SavesAndLoads()
    {
        var path = Path.GetTempFileName();
        try
        {
            var project = new ModProject { Name = "Test", FormatVersion = 1, Priority = 42 };
            project.Set(new ModEdit("T", "1", "F", "2", "1"));
            project.SetText(new TextEdit("ja-JP", "ST", "K", "剣", "盾"));
            project.SetRow(new RowAddition("Items/HealItem", "9", "1", new() { ["ID"] = "9" }));
            project.SetAddedText(new AddedText("ST", "K9", new() { ["en"] = "Nine" }));
            project.SetAsset(new AssetCopy("/Game/A/Town_004", "/Game/A/Town_011"));
            project.Save(path);

            Assert.Contains("剣", File.ReadAllText(path));
            var loaded = ModProject.Load(path);
            Assert.Equal(ModProject.CurrentFormat, loaded.FormatVersion);
            Assert.Equal(42, loaded.Priority);
            Assert.Equal(project.Edits, loaded.Edits);
            Assert.Equal(project.TextEdits, loaded.TextEdits);
            Assert.Equal("9", Assert.Single(loaded.AddedRows).Fields["ID"]);
            Assert.Equal("Nine", Assert.Single(loaded.AddedText).English);
            Assert.Equal("Town_011", Assert.Single(loaded.AddedAssets).Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("{ \"formatVersion\": 99 }")]
    [InlineData("{ \"priority\": 9999 }")]
    [InlineData("{ \"priority\": 0 }")]
    [InlineData("not json")]
    [InlineData("null")]
    public void RefusesBadFiles(string json)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            Assert.Throws<ArgumentException>(() => ModProject.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
