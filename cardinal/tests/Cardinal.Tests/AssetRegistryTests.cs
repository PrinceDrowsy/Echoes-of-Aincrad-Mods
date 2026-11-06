using Cardinal.Mods;
using CUE4Parse.UE4.AssetRegistry;
using CUE4Parse.UE4.Readers;

namespace Cardinal.Tests;

[Collection("Game")]
public class AssetRegistryTests(GameFixture fixture)
{
    const string Town = "/Game/ROD/DataAssets/Town/Town_004", NewTown = "/Game/ROD/DataAssets/Town/Town_011";

    byte[] GameFile() => fixture.Require().Provider.SaveAsset(AssetRegistry.File);

    [Fact]
    public void WritesBackByteForByte()
    {
        var bytes = GameFile();
        var registry = AssetRegistry.Read(bytes);
        Assert.True(registry.HashesMatch());
        Assert.True(bytes.AsSpan().SequenceEqual(registry.Write()));
    }

    [Fact]
    public void ACopiedAssetReadsBackWithItsOwnName()
    {
        var registry = AssetRegistry.Read(GameFile());
        var before = registry.Count;
        registry.AddCopy(Town, NewTown);
        var written = registry.Write();

        var again = AssetRegistry.Read(written);
        Assert.Equal(before + 1, again.Count);
        Assert.True(again.Contains(NewTown));
        Assert.True(again.HashesMatch());
        Assert.True(written.AsSpan().SequenceEqual(again.Write()));

        // CUE4Parse reads the file independently, the way the engine's own reader would.
        var state = new FAssetRegistryState(new FByteArchive("registry", written, fixture.Require().Provider.Versions));
        var entry = Assert.Single(state.PreallocatedAssetDataBuffers, a => a.PackageName.Text == NewTown);
        var original = Assert.Single(state.PreallocatedAssetDataBuffers, a => a.PackageName.Text == Town);
        Assert.Equal("Town_011", entry.AssetName.Text);
        Assert.Equal(original.AssetClass.Text, entry.AssetClass.Text);
        Assert.Equal("Town_011", entry.TagsAndValues.Single(t => t.Key.Text == "PrimaryAssetName").Value);
        Assert.Equal("Town", entry.TagsAndValues.Single(t => t.Key.Text == "PrimaryAssetType").Value);
        Assert.Equal("Town_004", original.TagsAndValues.Single(t => t.Key.Text == "PrimaryAssetName").Value);
    }

    [Fact]
    public void RefusesMissingAndTakenNames()
    {
        var registry = AssetRegistry.Read(GameFile());
        Assert.Throws<EditException>(() => registry.AddCopy("/Game/Nowhere/Nothing", NewTown));
        Assert.Throws<EditException>(() => registry.AddCopy(Town, "/Game/ROD/DataAssets/Town/Town_001"));
    }

    [Fact]
    public void NameHashesFollowTheEngine()
    {
        Assert.Equal(AssetRegistry.NameHash("Town_011"), AssetRegistry.NameHash("TOWN_011"));
        Assert.NotEqual(AssetRegistry.NameHash("Town_011"), AssetRegistry.NameHash("Town_012"));
    }
}
