using Cardinal.Maps;
using Cardinal.Scripts;
using Cardinal.Tables;
using Cardinal.Text;
using Newtonsoft.Json.Linq;

namespace Cardinal.Tests;

// Values checked here are from game version 1.0.6; an update that changes them fails these on purpose.
[Collection("Game")]
public class GameTests(GameFixture fixture)
{
    internal const string ActiveSkills = "Skills/DT_ActiveSkillList", HealItems = "Items/HealItem", UseItems = "Items/UseItem";

    internal static string? Value(Game game, string table, string row, string field)
    {
        var loaded = TableCatalog.Discover(game).Single(s => s.Id == table).Load(game);
        var index = loaded.Rows.ToList().FindIndex(r => r.Key == row);
        return index >= 0 && loaded.Field(index, field) is JValue value ? Table.Cell(value) : null;
    }

    [Fact]
    public void EveryTableLoads()
    {
        var game = fixture.Require();
        var sources = TableCatalog.Discover(game);
        Assert.True(sources.Count >= 200, $"only {sources.Count} tables");
        var folder = Path.Combine(Path.GetTempPath(), "CardinalTests", Guid.NewGuid().ToString("N"));
        try
        {
            var results = Export.Tables(game, sources, folder);
            Assert.Empty(results.Where(r => r.Error is not null).Select(r => $"{r.Id}: {r.Error}"));
            Assert.True(File.Exists(Path.Combine(folder, "Skills", "DT_ActiveSkillList.csv")));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void ReadsKnownValues()
    {
        var game = fixture.Require();
        Assert.Equal("5", Value(game, ActiveSkills, "0", "CoolTime"));
        Assert.Equal("999", Value(game, HealItems, "1", "MaxStack"));
        Assert.Equal("ItemName_Heal_1", Value(game, HealItems, "1", "ItemKey"));
        Assert.False(string.IsNullOrWhiteSpace(game.Text("ItemName_Heal_1")));
        Assert.Null(game.Text("NoSuchKey_Anywhere"));
    }

    [Fact]
    public void EveryLanguageWritesBackByteForByte()
    {
        var game = fixture.Require();
        var cultures = GameText.Cultures(game);
        Assert.Equal(15, cultures.Count);
        Assert.Contains("ja-JP", cultures);
        foreach (var culture in cultures)
        {
            var bytes = game.Provider.SaveAsset(GameText.FilePath(culture));
            Assert.True(bytes.SequenceEqual(Locres.Read(bytes).Write()), culture);
        }
    }

    [Fact]
    public void ReadsBothFloors()
    {
        var game = fixture.Require();
        Assert.Equal(["WL01", "WL02"], FloorMap.Floors(game).Order());
        var map = FloorMap.Load(game, "WL02");
        Assert.True(map.Cells.Count > 1000);
        Assert.Contains(map.Layers, l => l.Name == "Measurements" && l.Developer && l.CellCount > 0);
        Assert.Contains(map.Layers, l => !l.Developer && l.CellCount > 0);
        Assert.NotEmpty(map.LoadMarkers(game, "Measurements"));
        Assert.Contains(FloorMap.Load(game, "WL01").Layers, l => l.Name == "GimmickTools" && l.CellCount > 0);
    }

    [Fact]
    public void ScriptsReadInStoryOrder()
    {
        var game = fixture.Require();
        var scripts = Script.List(game);
        Assert.True(scripts.Count >= 500, $"only {scripts.Count} scripts");
        var guard = scripts.Single(s => s.Name == "Flow_pev_01_01_17");
        Assert.Equal("/Game/ROD/Flow/FlowGraphs/Event/PEV/pev_01_01_17/Flow_pev_01_01_17", guard.Package);

        var script = Script.Load(game, guard);
        Assert.Equal(["Start", "Talk (Blueprint)", "RODTimer", "ShowTutorialWindow", "Finish"],
            script.Steps.Take(5).Select(s => Script.KindName(s.Kind)));
        var window = script.Steps[3];
        Assert.Equal("2DB3554C-4DB8679E-01354D89-95299F90", window.Id);
        Assert.Equal("TutorialCategory ETutorialCategory::Operation; TutorialID 13", Script.Settings(window));
        Assert.Equal("Completed → 5 Finish", script.Leads(window));
    }
}
