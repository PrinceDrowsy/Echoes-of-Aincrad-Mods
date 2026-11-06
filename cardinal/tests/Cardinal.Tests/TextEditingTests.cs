using Cardinal.Mods;
using Cardinal.Text;

namespace Cardinal.Tests;

public sealed class TextEditingTests : IDisposable
{
    readonly string csv = Path.GetTempFileName();
    static readonly Locres English = Locres.Read(LocresTests.File(("ST", "A", "Sword"), ("ST", "B", "Shield, round")));
    readonly Locres german = Locres.Read(LocresTests.File(("ST", "A", "Schwert"), ("ST", "B", "Schild")));
    readonly ModProject project = new();

    public void Dispose() => File.Delete(csv);

    [Fact]
    public void SettingTheOriginalTextRemovesTheEdit()
    {
        Assert.Equal("Schwert", TextEditing.Set(project, german, "de-DE", "ST", "A", "Klinge"));
        Assert.Equal("Schwert", Assert.Single(project.TextEdits).Original);
        TextEditing.Set(project, german, "de-DE", "ST", "A", "Schwert");
        Assert.Empty(project.TextEdits);
    }

    [Fact]
    public void SettingAMissingLineFails() =>
        Assert.Throws<EditException>(() => TextEditing.Set(project, german, "de-DE", "ST", "Missing", "x"));

    [Fact]
    public void AddedTextKeepsItsEnglish()
    {
        project.SetAddedText(new AddedText("ST", "K", new() { ["en"] = "Sword", ["de-DE"] = "Schwert" }));
        Assert.Throws<EditException>(() => TextEditing.SetAdded(project, "en", "ST", "K", " "));
        Assert.True(TextEditing.SetAdded(project, "de-DE", "ST", "K", ""));
        Assert.Equal(["en"], Assert.Single(project.AddedText).Text.Keys);
        Assert.False(TextEditing.SetAdded(project, "de-DE", "ST", "Other", "x"));
    }

    [Fact]
    public void ACsvRoundTripKeepsAwkwardText()
    {
        TextEditing.ExportCsv(project, English, german, "de-DE", csv);
        var text = File.ReadAllText(csv).Replace("Schild", "\"Schild, \"\"rund\"\"\nzwei Zeilen\"");
        File.WriteAllText(csv, text);

        var result = TextEditing.ImportCsv(project, English, german, "de-DE", csv);
        Assert.Equal((1, 1), (result.Changed, result.Unchanged));
        Assert.Empty(result.Missing);
        Assert.Empty(result.EnglishChanged);
        Assert.Equal("Schild, \"rund\"\nzwei Zeilen", Assert.Single(project.TextEdits).Value);
    }

    [Fact]
    public void ImportListsMissingAndStaleLines()
    {
        TextEditing.ExportCsv(project, English, german, "de-DE", csv);
        File.AppendAllText(csv, "ST,Gone,Old,Alt\r\n");
        var newer = Locres.Read(LocresTests.File(("ST", "A", "Longsword"), ("ST", "B", "Shield, round")));

        var result = TextEditing.ImportCsv(project, newer, german, "de-DE", csv);
        Assert.Equal(["ST / Gone"], result.Missing);
        Assert.Equal(["ST / A"], result.EnglishChanged);
    }

    [Fact]
    public void ImportRefusesOtherCsvs()
    {
        File.WriteAllText(csv, "Key,Value\r\nA,B\r\n");
        Assert.Throws<ArgumentException>(() => TextEditing.ImportCsv(project, English, german, "de-DE", csv));
    }
}
