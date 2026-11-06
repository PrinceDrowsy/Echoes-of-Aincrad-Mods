namespace Cardinal.Mods;

/// <summary>Two or more merged mods setting the same field to different values. The last one listed wins.</summary>
public sealed record MergeClash(string Table, string Row, string Field, IReadOnlyList<(string Mod, string Value)> Values)
{
    public string Winner => Values[^1].Mod;

    public override string ToString() =>
        $"{Table} / {Row} / {Field}: {string.Join(", ", Values.Select(v => $"{v.Mod} sets {v.Value}"))}. {Winner} wins.";
}

/// <summary>
/// Combines several mod projects into one merged patch, so mods that change the same game files all take effect
/// instead of one file replacing the others.
/// </summary>
public static class ModMerge
{
    public const string Name = "CardinalMerged";

    /// <summary>Later projects win where two change the same field. The result builds like any other project.</summary>
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
        var setters = new Dictionary<(string, string, string), List<(string Mod, string Value)>>();
        foreach (var project in projects)
            foreach (var edit in project.Edits)
            {
                var key = (edit.Table, edit.Row, edit.Field);
                if (!setters.TryGetValue(key, out var values))
                    setters[key] = values = [];
                values.Add((project.Name, edit.Value));
                merged.Set(edit);
            }

        var clashes = setters
            .Where(s => s.Value.Skip(1).Any(v => !ModEdit.Same(v.Value, s.Value[0].Value)))
            .Select(s => new MergeClash(s.Key.Item1, s.Key.Item2, s.Key.Item3, s.Value))
            .ToList();
        return (merged, clashes);
    }
}
