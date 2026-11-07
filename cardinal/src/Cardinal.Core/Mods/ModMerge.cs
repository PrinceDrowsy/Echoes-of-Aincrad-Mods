namespace Cardinal.Mods;

/// <summary>
/// Two or more merged mods setting the same value or line of text differently, or adding the same row. The last one listed wins.
/// </summary>
public sealed record MergeClash(string Change, IReadOnlyList<(string Mod, string Value)> Values)
{
    public string Winner => Values[^1].Mod;

    public override string ToString() =>
        $"{Change}: {string.Join(", ", Values.Select(v => $"{v.Mod} sets {v.Value}"))}. {Winner} wins.";
}

/// <summary>
/// Combines several mod projects into one merged patch, so mods that change the same game files all take effect
/// instead of one file replacing the others. That matters most for text: every text mod replaces a whole language.
/// </summary>
public static class ModMerge
{
    public const string Name = "CardinalMerged";

    /// <summary>Later projects win where two change the same value or line. The result builds like any other project.</summary>
    public static (ModProject Merged, IReadOnlyList<MergeClash> Clashes) Merge(IReadOnlyList<ModProject> projects)
    {
        if (projects.Count < 2)
            throw new ArgumentException("Merging needs at least two mod projects.");

        var merged = new ModProject
        {
            Name = Name,
            Version = DateTime.Now.ToString("yyyy.M.d"),
            Description = "Merged by Cardinal from " + string.Join(", ", projects.Select(p => $"{p.Name} {p.Version}")),
            Priority = ModProject.MergedPriority,
        };
        var values = new Dictionary<string, List<(string Mod, string Value)>>();
        var text = new Dictionary<string, List<(string Mod, string Value)>>();
        var rows = new Dictionary<string, List<(string Mod, string Value)>>();
        foreach (var project in projects)
        {
            foreach (var edit in project.Edits)
            {
                Add(values, edit.Label, project.Name, edit.Value);
                merged.Set(edit);
            }
            foreach (var edit in project.TextEdits)
            {
                Add(text, "text " + edit.Label, project.Name, edit.Value);
                merged.SetText(edit);
            }
            // Two mods adding the same row would each expect it to be theirs, so that's always a clash, even with equal values.
            foreach (var row in project.AddedRows)
            {
                Add(rows, "new row " + row.Label, project.Name, $"copy of {row.From}" + string.Concat(row.Fields.Select(f => $", {f.Key} {f.Value}")));
                merged.SetRow(row);
            }
            foreach (var copy in project.AddedAssets)
            {
                Add(rows, "new asset " + copy.Label, project.Name, $"copy of {copy.From}");
                merged.SetAsset(copy);
            }
            foreach (var added in project.AddedText)
            {
                foreach (var (culture, line) in added.Text)
                    Add(text, $"new text {culture} / {added.Label}", project.Name, line);
                // Translations of different English would belong to something else, so they're only combined when the English matches.
                var earlier = merged.FindAddedText(added.Namespace, added.Key);
                merged.SetAddedText(earlier is null || earlier.English != added.English
                    ? added
                    : added with { Text = new Dictionary<string, string>(earlier.Text.Concat(added.Text).GroupBy(l => l.Key).Select(g => g.Last())) });
            }
        }

        var clashes = values.Where(s => s.Value.Skip(1).Any(v => !ModEdit.Same(v.Value, s.Value[0].Value)))
            .Concat(text.Where(s => s.Value.Skip(1).Any(v => v.Value != s.Value[0].Value)))
            .Concat(rows.Where(s => s.Value.Count > 1))
            .Select(s => new MergeClash(s.Key, s.Value))
            .ToList();
        return (merged, clashes);
    }

    static void Add(Dictionary<string, List<(string, string)>> setters, string change, string mod, string value)
    {
        if (!setters.TryGetValue(change, out var list))
            setters[change] = list = [];
        list.Add((mod, value));
    }
}
