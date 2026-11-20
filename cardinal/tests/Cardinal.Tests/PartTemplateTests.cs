using Cardinal.Mods;
using Cardinal.Tables;
using Newtonsoft.Json.Linq;

namespace Cardinal.Tests;

[Collection("Game")]
public sealed class PartTemplateTests(GameFixture fixture) : IDisposable
{
    public void Dispose() => fixture.ClearMods();

    [Fact]
    public void ANewPupilGetsItsRowsMeshAndListEntry()
    {
        var game = fixture.Require();
        fixture.ClearMods();
        var sources = TableCatalog.Discover(game);
        var kind = PartTemplate.Kind("pupil");
        var project = new ModProject { Name = "Pupil" };
        // The creator offers pupils 1-16, but the game ships meshes up to 29, so 30 is the first free number.
        var id = PartTemplate.NextId(game, sources.Single(s => s.Id == kind.Parts).Load(game), project, kind);
        Assert.Equal(30, id);
        PartTemplate.Add(game, fixture.Settings, project, sources, new NewPart("pupil", 1, id, false));
        Assert.Equal(31, PartTemplate.NextId(game, sources.Single(s => s.Id == kind.Parts).Load(game), project, kind));

        ModBuilder.Install(game, fixture.Settings, project, false);
        Assert.Empty(RegistryPatch.Build(fixture.Settings).Problems);

        using var modded = Game.Open(fixture.Settings, true);
        var tables = TableCatalog.Discover(modded);
        Assert.Contains(tables.Single(s => s.Id == kind.Parts).Load(modded).Rows, r => r.Fields["ID"]?.ToString() == "30");
        Assert.Contains(tables.Single(s => s.Id == kind.Thumbnails).Load(modded).Rows, r => r.Fields["ID"]?.ToString() == "30");
        Assert.True(modded.Provider.Files.ContainsKey(AssetCopier.File(kind.Mesh(30))));
        var meshes = (JArray)JObject.FromObject(modded.Provider.LoadPackage(AssetCopier.File(kind.MeshList)).GetExports().First()
            .Properties.ToDictionary(p => p.Name.Text, p => p.Tag?.GenericValue))["AvatarMeshes"]!;
        Assert.Equal("/Game/ROD/CHR/Humans/Heads/PUP/SK_CHR_PUP030.SK_CHR_PUP030", (string?)meshes[^1]!["AssetPathName"]);
    }

    [Theory]
    [InlineData("pupil", 1, 1)]
    [InlineData("pupil", 1, 17)]
    [InlineData("pupil", 99, 40)]
    [InlineData("pupil", 1, 1000)]
    [InlineData("ears", 1, 40)]
    public void TakenOrBadPartsAreRefused(string kind, int from, int id)
    {
        var game = fixture.Require();
        Assert.Throws<EditException>(() =>
            PartTemplate.Add(game, fixture.Settings, new ModProject(), TableCatalog.Discover(game), new NewPart(kind, from, id, false)));
    }
}
