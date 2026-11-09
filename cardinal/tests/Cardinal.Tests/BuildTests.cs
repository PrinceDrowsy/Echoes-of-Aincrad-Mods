using Cardinal.Mods;
using Cardinal.Tables;
using Cardinal.Text;
using Newtonsoft.Json.Linq;
using static Cardinal.Tests.GameTests;

namespace Cardinal.Tests;

// Builds real mods into the fixture's linked Paks folder and reads them back the way the game would load them.
[Collection("Game")]
public sealed class BuildTests(GameFixture fixture) : IDisposable
{
    public void Dispose() => fixture.ClearMods();

    static ModProject CoolTime(string name, string value)
    {
        var project = new ModProject { Name = name };
        project.Set(new ModEdit(ActiveSkills, "0", "CoolTime", value, "5"));
        return project;
    }

    [Fact]
    public void AnInstalledModChangesValuesTextAndItems()
    {
        var game = fixture.Require();
        fixture.ClearMods();
        var project = CoolTime("Test", "1");
        project.Set(new ModEdit(HealItems, "1", "MaxStack", "500", "999"));
        TextEditing.Set(project, GameText.Load(game, GameText.English), GameText.English, Game.TextNamespace, "ItemName_Heal_1", "Test Gift");
        var sources = TableCatalog.Discover(game);
        var id = ItemTemplate.NextId(sources.Single(s => s.Id == UseItems).Load(game), project, UseItems);
        ItemTemplate.Add(game, fixture.Settings, project, sources,
            new NewItem(UseItems, "1", id, "Test Item", "A test.") { Translations = { ["ja-JP"] = ("テスト", "テストです。") } });
        Assert.Throws<EditException>(() => ItemTemplate.Add(game, fixture.Settings, project, sources, new NewItem(HealItems, "1", 2, "Pickup", "A pickup.")));

        Assert.Empty(ModBuilder.Check(game, fixture.Settings, project));
        var files = ModBuilder.Install(game, fixture.Settings, project, false);
        Assert.Equal(["Test_1000_P.pak", "Test_1000_P.ucas", "Test_1000_P.utoc"], files.Select(Path.GetFileName).Order());
        Assert.Throws<IOException>(() => ModBuilder.Install(game, fixture.Settings, project, false));

        using var modded = Game.Open(fixture.Settings, true);
        var key = id.ToString();
        Assert.Equal("1", Value(modded, ActiveSkills, "0", "CoolTime"));
        Assert.Equal("500", Value(modded, HealItems, "1", "MaxStack"));
        Assert.Equal("Test Gift", modded.Text("ItemName_Heal_1"));
        Assert.Equal(Value(game, UseItems, "1", "MaxStack"), Value(modded, UseItems, key, "MaxStack"));
        var nameKey = Value(modded, UseItems, key, "ItemKey")!;
        Assert.Equal("Test Item", modded.Text(nameKey));
        Assert.Equal("テスト", GameText.Load(modded, "ja-JP").Get(Game.TextNamespace, nameKey));
    }

    [Fact]
    public void ReinstallingAtANewPriorityRemovesTheOldBuild()
    {
        var game = fixture.Require();
        fixture.ClearMods();
        var project = CoolTime("Moved", "2");
        ModBuilder.Install(game, fixture.Settings, project, false);
        project.Priority = 2000;
        Assert.Equal(3, ModBuilder.Installed(fixture.Settings, project).Count);
        Assert.Throws<IOException>(() => ModBuilder.Install(game, fixture.Settings, project, false));
        ModBuilder.Install(game, fixture.Settings, project, true);
        Assert.Equal(["Moved_2000_P.pak", "Moved_2000_P.ucas", "Moved_2000_P.utoc"],
            Directory.GetFiles(fixture.Mods).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void ATextOnlyModIsJustAPak()
    {
        var game = fixture.Require();
        var project = new ModProject { Name = "Words" };
        TextEditing.Set(project, GameText.Load(game, "ja-JP"), "ja-JP", Game.TextNamespace, "ItemName_Heal_1", "テスト");
        var folder = Path.Combine(Path.GetTempPath(), "CardinalTests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            foreach (var ext in new[] { ".utoc", ".ucas" })
                File.WriteAllText(Path.Combine(folder, "Words_1000_P" + ext), "an earlier build");
            Assert.Equal(["Words_1000_P.pak"], ModBuilder.Build(game, fixture.Settings, project, folder).Select(Path.GetFileName));
            Assert.Equal(["Words_1000_P.pak"], Directory.GetFiles(folder).Select(Path.GetFileName));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void CheckSeparatesWarningsFromBlockingProblems()
    {
        var game = fixture.Require();
        var project = new ModProject { Name = "Drift" };
        project.Set(new ModEdit(ActiveSkills, "0", "CoolTime", "2", "4"));
        project.Set(new ModEdit(HealItems, "999999", "MaxStack", "5"));
        project.Set(new ModEdit(HealItems, "1", "MaxStack", "many"));
        project.Set(new ModEdit("Nowhere/Nothing", "1", "Field", "5"));
        project.SetText(new TextEdit("xx-XX", Game.TextNamespace, "ItemName_Heal_1", "?"));

        var problems = ModBuilder.Check(game, fixture.Settings, project);
        Assert.Equal(4, problems.Count(p => p.Blocks));
        Assert.Equal($"{ActiveSkills} / 0 / CoolTime", Assert.Single(problems, p => !p.Blocks).Change);
        Assert.Throws<EditException>(() => ModBuilder.Build(game, fixture.Settings, project, Path.GetTempPath()));
    }

    [Fact]
    public void AListCanOnlyBeEmptied()
    {
        var game = fixture.Require();
        fixture.ClearMods();
        const string tutorials = "Other/DT_TutorialUISystemList";
        JArray Pages(Game from)
        {
            var loaded = TableCatalog.Discover(from).Single(s => s.Id == tutorials).Load(from);
            return Assert.IsType<JArray>(loaded.Field(loaded.Rows.ToList().FindIndex(r => r.Key == "7"), "TutorialWindow"));
        }
        Assert.NotEmpty(Pages(game));

        var rejected = new ModProject { Name = "Pages" };
        rejected.Set(new ModEdit(tutorials, "7", "TutorialWindow", "[{}]"));
        Assert.True(Assert.Single(ModBuilder.Check(game, fixture.Settings, rejected)).Blocks);

        var project = new ModProject { Name = "Quiet" };
        project.Set(new ModEdit(tutorials, "7", "TutorialWindow", "[]"));
        Assert.Empty(ModBuilder.Check(game, fixture.Settings, project));
        ModBuilder.Install(game, fixture.Settings, project, false);
        using var modded = Game.Open(fixture.Settings, true);
        Assert.Empty(Pages(modded));
    }

    [Fact]
    public void AFlowStepCanBecomeAnotherKind()
    {
        var game = fixture.Require();
        fixture.ClearMods();
        // Guard's tutorial window in the beta, turned into the quiet tutorial step the game uses elsewhere.
        const string graph = "/Game/ROD/Flow/FlowGraphs/Event/PEV/pev_01_01_17/Flow_pev_01_01_17";
        const string step = "2DB3554C-4DB8679E-01354D89-95299F90";
        NodeChange Change(string kind, Dictionary<string, string> fields) =>
            new(graph, step, kind, fields, new() { ["Completed"] = "Out" });

        var wrong = new ModProject { Name = "Wrong" };
        wrong.SetNode(Change("FlowNode_Nothing", []));
        wrong.SetNode(Change("FlowNode_TutorialGuide", []) with { Node = "00000000-00000000-00000000-00000000" });
        wrong.SetNode(Change("FlowNode_TutorialGuide", new() { ["bLoud"] = "true" }) with { Asset = graph.Replace("17", "16") + "x" });
        Assert.Equal(3, ModBuilder.Check(game, fixture.Settings, wrong).Count(p => p.Blocks));

        var project = new ModProject { Name = "Quiet" };
        project.SetNode(Change("FlowNode_TutorialGuide", new() { ["bShow"] = "true", ["bShowInfoMessage"] = "true" }));
        Assert.Empty(ModBuilder.Check(game, fixture.Settings, project));
        ModBuilder.Install(game, fixture.Settings, project, false);

        using var modded = Game.Open(fixture.Settings, true);
        var node = modded.Provider.LoadPackage(Cardinal.Mods.AssetCopier.File(graph)).GetExports()
            .Single(e => JObject.FromObject(e.Properties.ToDictionary(p => p.Name.Text, p => p.Tag?.GenericValue))["NodeGuid"]?.ToString() == step);
        Assert.Equal("FlowNode_TutorialGuide", node.ExportType);
        var fields = JObject.FromObject(node.Properties.ToDictionary(p => p.Name.Text, p => p.Tag?.GenericValue));
        Assert.Equal("ETutorialCategory::Operation", (string?)fields["TutorialCategory"]);
        Assert.Equal(13, (int?)fields["TutorialID"]);
        Assert.True((bool?)fields["bShow"]);
        Assert.True((bool?)fields["bShowInfoMessage"]);
        var next = Assert.Single(fields["Connections"]!);
        Assert.Equal("Out", (string?)next["Key"]);
        Assert.Equal("FA92D775-47096F42-36F68683-723CA16C", (string?)next["Value"]!["NodeGuid"]);
    }

    [Fact]
    public void ModsAtOnePriorityTieUntilMerged()
    {
        var game = fixture.Require();
        fixture.ClearMods();
        var mods = new[] { CoolTime("A", "2"), CoolTime("B", "3") };
        foreach (var mod in mods)
            ModBuilder.Install(game, fixture.Settings, mod, false);

        var scan = ModConflicts.Scan(fixture.Settings);
        Assert.Equal(["~mods/A_1000_P", "~mods/B_1000_P"], scan.Mods.Select(m => m.Name));
        Assert.Null(Assert.Single(scan.Conflicts).Winner);

        var (merged, clashes) = ModMerge.Merge(mods);
        Assert.Single(clashes);
        ModBuilder.Install(game, fixture.Settings, merged, false);
        scan = ModConflicts.Scan(fixture.Settings);
        Assert.Equal("~mods/CardinalMerged_9999_P", Assert.Single(scan.Conflicts).Winner?.Name);
        using var modded = Game.Open(fixture.Settings, true);
        Assert.Equal("3", Value(modded, ActiveSkills, "0", "CoolTime"));
    }
}
