using Cardinal.Mods;
using CUE4Parse.UE4.Assets.Exports;
using Newtonsoft.Json.Linq;

namespace Cardinal.Tests;

// Two mods each adding an asset must both be found: a whole registry per mod would let one hide the other.
[Collection("Game")]
public sealed class RegistryPatchTests(GameFixture fixture) : IDisposable
{
    const string Towns = "/Game/ROD/DataAssets/Town/", Quests = "/Game/ROD/DataAssets/Quests/Free/Free_Sub/";

    public void Dispose() => fixture.ClearMods();

    static ModProject Copying(string name, string from, string package)
    {
        var project = new ModProject { Name = name };
        project.SetAsset(new AssetCopy(from, package));
        return project;
    }

    [Fact]
    public void EveryInstalledModsAssetsEndUpInOneRegistry()
    {
        var game = fixture.Require();
        fixture.ClearMods();
        ModBuilder.Install(game, fixture.Settings, Copying("NewTown", Towns + "Town_004", Towns + "Town_011"), false);
        ModBuilder.Install(game, fixture.Settings, Copying("NewQuest", Quests + "QST_Free_20001", Quests + "QST_Free_20061"), false);

        var result = RegistryPatch.Build(fixture.Settings);
        Assert.Empty(result.Problems);
        Assert.Equal([Quests + "QST_Free_20061", Towns + "Town_011"], result.Added.Select(a => a.Package).Order());
        Assert.Equal(RegistryPatch.FileName + ".pak", Path.GetFileName(result.Pak));

        using var modded = Game.Open(fixture.Settings, true);
        var registry = AssetRegistry.Read(modded.Provider.SaveAsset(AssetRegistry.File));
        Assert.True(registry.Contains(Towns + "Town_011"));
        Assert.True(registry.Contains(Quests + "QST_Free_20061"));

        // The copies load under their own names with the source's values.
        var town = JObject.FromObject(modded.Load<UObject>(AssetCopier.File(Towns + "Town_011")));
        var original = JObject.FromObject(game.Load<UObject>(AssetCopier.File(Towns + "Town_004")));
        Assert.Equal("Town_011", town["Name"]!.ToString());
        Assert.Equal(original["Properties"]!.ToString(), town["Properties"]!.ToString());
        Assert.Equal("QST_Free_20061", modded.Load<UObject>(AssetCopier.File(Quests + "QST_Free_20061")).Name);
    }

    [Fact]
    public void WithNothingToListThePatchRemovesItself()
    {
        var game = fixture.Require();
        fixture.ClearMods();
        ModBuilder.Install(game, fixture.Settings, Copying("NewTown", Towns + "Town_004", Towns + "Town_011"), false);
        Assert.NotNull(RegistryPatch.Build(fixture.Settings).Pak);

        foreach (var file in Directory.EnumerateFiles(fixture.Mods, "NewTown_*"))
            File.Delete(file);
        var result = RegistryPatch.Build(fixture.Settings);
        Assert.Null(result.Pak);
        Assert.False(File.Exists(Path.Combine(fixture.Mods, RegistryPatch.FileName + ".pak")));
    }

    [Theory]
    [InlineData(Towns + "Town_004", Towns + "Town_001")]
    [InlineData(Towns + "Town_999", Towns + "Town_011")]
    [InlineData(Towns + "Town_004", Towns + "Town 011")]
    [InlineData("Town_004", Towns + "Town_011")]
    public void BadCopiesBlockTheBuild(string from, string package)
    {
        var game = fixture.Require();
        var problems = ModBuilder.Check(game, fixture.Settings, Copying("Bad", from, package));
        Assert.True(Assert.Single(problems).Blocks);
    }

    [Fact]
    public void MergingKeepsCopiesAndFlagsTheSameOneTwice()
    {
        var (merged, clashes) = ModMerge.Merge([
            Copying("A", Towns + "Town_004", Towns + "Town_011"),
            Copying("B", Towns + "Town_005", Towns + "Town_011"),
            Copying("C", Towns + "Town_004", Towns + "Town_012"),
        ]);
        Assert.Equal([Towns + "Town_011", Towns + "Town_012"], merged.AddedAssets.Select(a => a.Package).Order());
        Assert.Equal(Towns + "Town_005", merged.FindAsset(Towns + "Town_011")!.From);
        Assert.Equal("B", Assert.Single(clashes).Winner);
    }
}
