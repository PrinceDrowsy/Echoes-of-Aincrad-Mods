using Cardinal.Mods;

namespace Cardinal.Tests;

public class ModMergeTests
{
    static ModProject Mod(string name, Action<ModProject> changes)
    {
        var project = new ModProject { Name = name };
        changes(project);
        return project;
    }

    [Fact]
    public void NeedsTwoMods() =>
        Assert.Throws<ArgumentException>(() => ModMerge.Merge([new ModProject()]));

    [Fact]
    public void TheLastModWins()
    {
        var (merged, clashes) = ModMerge.Merge([
            Mod("A", p => p.Set(new ModEdit("T", "1", "F", "2", "1"))),
            Mod("B", p => p.Set(new ModEdit("T", "1", "F", "3", "1"))),
        ]);
        Assert.Equal(ModProject.MergedPriority, merged.Priority);
        Assert.Equal("3", Assert.Single(merged.Edits).Value);
        var clash = Assert.Single(clashes);
        Assert.Equal("B", clash.Winner);
        Assert.Equal(["A", "B"], clash.Values.Select(v => v.Mod));
    }

    [Fact]
    public void EqualValuesDontClash()
    {
        var (merged, clashes) = ModMerge.Merge([
            Mod("A", p => p.Set(new ModEdit("T", "1", "F", "5"))),
            Mod("B", p => p.Set(new ModEdit("T", "1", "F", "5.0"))),
            Mod("C", p => p.Set(new ModEdit("T", "2", "F", "1"))),
        ]);
        Assert.Empty(clashes);
        Assert.Equal(2, merged.Edits.Count);
    }

    [Fact]
    public void TextClashes()
    {
        var (merged, clashes) = ModMerge.Merge([
            Mod("A", p => p.SetText(new TextEdit("en", "ST", "K", "a"))),
            Mod("B", p => p.SetText(new TextEdit("en", "ST", "K", "b"))),
        ]);
        Assert.Equal("b", Assert.Single(merged.TextEdits).Value);
        Assert.Single(clashes);
    }

    [Fact]
    public void TheSameRowAddedTwiceAlwaysClashes()
    {
        var row = new RowAddition("Items/HealItem", "9", "1", new() { ["ID"] = "9" });
        var (merged, clashes) = ModMerge.Merge([Mod("A", p => p.SetRow(row)), Mod("B", p => p.SetRow(row))]);
        Assert.Single(merged.AddedRows);
        Assert.Single(clashes);
    }

    [Fact]
    public void TranslationsOfTheSameEnglishCombine()
    {
        var (merged, clashes) = ModMerge.Merge([
            Mod("A", p => p.SetAddedText(new AddedText("ST", "K", new() { ["en"] = "Sword", ["ja-JP"] = "剣" }))),
            Mod("B", p => p.SetAddedText(new AddedText("ST", "K", new() { ["en"] = "Sword", ["de-DE"] = "Schwert" }))),
        ]);
        Assert.Equal(["en", "ja-JP", "de-DE"], Assert.Single(merged.AddedText).Text.Keys);
        Assert.Empty(clashes);
    }

    [Fact]
    public void DifferentEnglishReplacesTheTranslations()
    {
        var (merged, clashes) = ModMerge.Merge([
            Mod("A", p => p.SetAddedText(new AddedText("ST", "K", new() { ["en"] = "Sword", ["ja-JP"] = "剣" }))),
            Mod("B", p => p.SetAddedText(new AddedText("ST", "K", new() { ["en"] = "Shield" }))),
        ]);
        Assert.Equal(["en"], Assert.Single(merged.AddedText).Text.Keys);
        Assert.Single(clashes);
    }
}
