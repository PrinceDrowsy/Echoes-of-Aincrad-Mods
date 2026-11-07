using Cardinal.Mods;
using Cardinal.Tables;
using static Cardinal.Tests.GameTests;

namespace Cardinal.Tests;

[Collection("Game")]
public sealed class TownTemplateTests(GameFixture fixture) : IDisposable
{
    public void Dispose() => fixture.ClearMods();

    // Row 3 is Urbas, town 004.
    static NewTown Lindarth(int id) => new("3", id, "Lindarth");

    [Fact]
    public void ANewTownGetsItsRowNameAndAsset()
    {
        var game = fixture.Require();
        fixture.ClearMods();
        var sources = TableCatalog.Discover(game);
        var project = new ModProject { Name = "Lindarth" };
        var id = TownTemplate.NextId(sources.Single(s => s.Id == TownTemplate.Table).Load(game), project);
        Assert.Equal(11, id);
        TownTemplate.Add(game, fixture.Settings, project, sources, Lindarth(id));

        ModBuilder.Install(game, fixture.Settings, project, false);
        Assert.Empty(RegistryPatch.Build(fixture.Settings).Problems);

        using var modded = Game.Open(fixture.Settings, true);
        var row = Assert.Single(TableCatalog.Discover(modded).Single(s => s.Id == TownTemplate.Table).Load(modded).Rows,
            r => r.Fields["ID"]?.ToString() == "011");
        Assert.Equal("Lindarth", row.Fields["Name"]?.ToString());
        Assert.Equal(Value(game, TownTemplate.Table, "3", "StoryProgress"), row.Fields["StoryProgress"]?.ToString());
        Assert.Equal("Lindarth", modded.Text("AreaTitle_Lindarth"));
        Assert.True(AssetRegistry.Read(modded.Provider.SaveAsset(AssetRegistry.File)).Contains("/Game/ROD/DataAssets/Town/Town_011"));
    }

    [Theory]
    [InlineData(4, "Lindarth")]
    [InlineData(11, "Urbas")]
    [InlineData(11, "!!!")]
    [InlineData(1000, "Lindarth")]
    public void TakenOrBadTownsAreRefused(int id, string name)
    {
        var game = fixture.Require();
        Assert.Throws<EditException>(() =>
            TownTemplate.Add(game, fixture.Settings, new ModProject(), TableCatalog.Discover(game), new NewTown("3", id, name)));
    }
}
