using Cardinal.Tables;
using Newtonsoft.Json.Linq;
using UAssetAPI.Unversioned;

namespace Cardinal.Mods;

/// <summary>Something wrong with one edit. A blocking problem stops the build; the rest are warnings.</summary>
public sealed record EditProblem(ModEdit Edit, string Problem, bool Blocks)
{
    public override string ToString() => $"{Edit.Table} / {Edit.Row} / {Edit.Field}: {Problem}";
}

/// <summary>Turns a mod project into the three files the game loads from Paks\~mods.</summary>
public static class ModBuilder
{
    /// <summary>
    /// Applies every edit to freshly extracted, unmodded assets and packs them as project.FileName() in output.
    /// Every edit is checked first, so a project with a bad edit builds nothing.
    /// </summary>
    public static IReadOnlyList<string> Build(Game game, Settings settings, ModProject project, string output)
    {
        if (project.Edits.Count == 0)
            throw new EditException("The project has no changes to build.");

        var (plan, problems) = Plan(game, settings, project);
        if (problems.Count > 0)
            throw new EditException(string.Join(Environment.NewLine, problems));

        var stage = EditableAsset.WorkFolder();
        try
        {
            foreach (var (asset, target, edit) in plan)
                asset.Apply(target, edit);
            foreach (var asset in plan.Select(p => p.Asset).Distinct())
                asset.Write(Path.Combine(stage, asset.File));
            return Retoc.Pack(stage, output, project.FileName());
        }
        finally
        {
            Directory.Delete(stage, true);
        }
    }

    /// <summary>
    /// Checks every edit against the current unmodded game: edits that can no longer be made block a build, and
    /// edits whose original value has changed since they were made (usually after a game update) are warnings.
    /// </summary>
    public static IReadOnlyList<EditProblem> Check(Game game, Settings settings, ModProject project)
    {
        var (_, problems) = Plan(game, settings, project);
        var broken = problems.Select(p => p.Edit).ToHashSet();
        var sources = TableCatalog.Discover(game).ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        var tables = new Dictionary<string, Table>();
        foreach (var edit in project.Edits.Where(e => e.Original is not null && !broken.Contains(e)))
        {
            if (!tables.TryGetValue(edit.Table, out var table))
                tables[edit.Table] = table = sources[edit.Table].Load(game);
            var row = table.Rows.ToList().FindIndex(r => r.Key == edit.Row);
            var current = row >= 0 && table.Field(row, edit.Field) is JValue value ? Table.Cell(value) : null;
            if (current is not null && !ModEdit.Same(current, edit.Original))
                problems.Add(new EditProblem(edit,
                    $"the unmodded game now has {current} here, not {edit.Original} as when this change was made. Your value {edit.Value} still applies.",
                    false));
        }
        return problems;
    }

    static (List<(EditableAsset Asset, EditTarget Target, ModEdit Edit)> Plan, List<EditProblem> Problems) Plan(
        Game game, Settings settings, ModProject project)
    {
        var sources = TableCatalog.Discover(game).ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        var mappings = new Usmap(settings.MappingsFile!);
        var assets = new Dictionary<string, EditableAsset>();
        var problems = new List<EditProblem>();
        var plan = new List<(EditableAsset, EditTarget, ModEdit)>();

        foreach (var edit in project.Edits)
        {
            if (!sources.TryGetValue(edit.Table, out var source))
            {
                problems.Add(new EditProblem(edit, "no such table.", true));
                continue;
            }
            if (source.Target is not { } target)
            {
                problems.Add(new EditProblem(edit, "this table is read-only.", true));
                continue;
            }
            if (!assets.TryGetValue(target.File, out var asset))
                assets[target.File] = asset = EditableAsset.Load(settings, mappings, target.File);
            if (asset.Check(target, edit) is { } problem)
                problems.Add(new EditProblem(edit, problem, true));
            else
                plan.Add((asset, target, edit));
        }
        return (plan, problems);
    }

    public static string ModsFolder(Settings settings) => Path.Combine(settings.PaksFolder!, "~mods");

    /// <summary>The files a build of this project would replace in ~mods.</summary>
    public static IReadOnlyList<string> Installed(Settings settings, ModProject project) =>
        new[] { ".utoc", ".ucas", ".pak" }
            .Select(ext => Path.Combine(ModsFolder(settings), project.FileName() + ext))
            .Where(File.Exists)
            .ToList();

    /// <summary>Builds straight into ~mods. Refuses to replace an earlier build unless overwrite is set.</summary>
    public static IReadOnlyList<string> Install(Game game, Settings settings, ModProject project, bool overwrite)
    {
        if (!overwrite && Installed(settings, project).Count > 0)
            throw new IOException($"{project.FileName()} is already in {ModsFolder(settings)}. Replace it to install this build.");
        return Build(game, settings, project, ModsFolder(settings));
    }
}
