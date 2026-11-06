using Cardinal.Tables;
using Cardinal.Text;
using Newtonsoft.Json.Linq;
using UAssetAPI.Unversioned;

namespace Cardinal.Mods;

/// <summary>Something wrong with one change, named by what it changes. A blocking problem stops the build; the rest are warnings.</summary>
public sealed record EditProblem(string Change, string Problem, bool Blocks)
{
    public EditProblem(ModEdit edit, string problem, bool blocks) : this(edit.Label, problem, blocks) { }

    public EditProblem(TextEdit edit, string problem, bool blocks) : this($"text {edit.Label}", problem, blocks) { }

    public override string ToString() => $"{Change}: {Problem}";
}

/// <summary>Turns a mod project into the files the game loads from Paks\~mods.</summary>
public static class ModBuilder
{
    /// <summary>
    /// Applies every change to freshly extracted, unmodded files and packs them as project.FileName() in output:
    /// value changes into a .utoc and .ucas container, text into the .pak beside it. A text-only mod is just the .pak.
    /// Every change is checked first, so a project with a bad one builds nothing.
    /// </summary>
    public static IReadOnlyList<string> Build(Game game, Settings settings, ModProject project, string output)
    {
        if (project.Changes == 0)
            throw new EditException("The project has no changes to build.");
        // retoc and repak run from Cardinal's own folder, so a relative output would land there.
        output = Path.GetFullPath(output);

        var (plan, problems) = Plan(game, settings, project);
        var (text, textProblems) = PlanText(game, project, true);
        problems.AddRange(textProblems);
        if (problems.Count > 0)
            throw new EditException(string.Join(Environment.NewLine, problems));

        var stage = EditableAsset.WorkFolder();
        try
        {
            var files = new List<string>();
            if (plan.Count > 0)
            {
                var assets = Path.Combine(stage, "assets");
                foreach (var (asset, target, edit) in plan)
                    asset.Apply(target, edit);
                foreach (var asset in plan.Select(p => p.Asset).Distinct())
                    asset.Write(Path.Combine(assets, asset.File));
                files.AddRange(Retoc.Pack(assets, output, project.FileName()));
            }
            if (text.Count > 0)
            {
                var locres = Path.Combine(stage, "text");
                foreach (var (culture, file) in text)
                {
                    var path = Path.Combine(locres, GameText.FilePath(culture));
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllBytes(path, file.Write());
                }
                var pak = Repak.Pack(locres, output, project.FileName());
                if (!files.Contains(pak))
                    files.Add(pak);
            }
            return files;
        }
        finally
        {
            Directory.Delete(stage, true);
        }
    }

    /// <summary>
    /// Checks every change against the current unmodded game: changes that can no longer be made block a build, and
    /// changes whose original value has changed since they were made (usually after a game update) are warnings.
    /// </summary>
    public static IReadOnlyList<EditProblem> Check(Game game, Settings settings, ModProject project)
    {
        var (_, problems) = Plan(game, settings, project);
        var broken = problems.Select(p => p.Change).ToHashSet();
        var sources = TableCatalog.Discover(game).ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        var tables = new Dictionary<string, Table>();
        foreach (var edit in project.Edits.Where(e => e.Original is not null && !broken.Contains(e.Label)))
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

        var (text, textProblems) = PlanText(game, project, false);
        problems.AddRange(textProblems);
        foreach (var edit in project.TextEdits.Where(e => e.Original is not null))
            if (text.TryGetValue(edit.Culture, out var file) && Original(file, edit) is { } current && current != edit.Original)
                problems.Add(new EditProblem(edit,
                    $"the unmodded game's text here has changed since this edit was made (now \"{current}\"). Your text still applies.",
                    false));
        return problems;
    }

    static (List<(EditableAsset Asset, EditTarget Target, ModEdit Edit)> Plan, List<EditProblem> Problems) Plan(
        Game game, Settings settings, ModProject project)
    {
        var problems = new List<EditProblem>();
        var plan = new List<(EditableAsset, EditTarget, ModEdit)>();
        if (project.Edits.Count == 0)
            return (plan, problems);

        var sources = TableCatalog.Discover(game).ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        var mappings = new Usmap(settings.MappingsFile!);
        var assets = new Dictionary<string, EditableAsset>();
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

    /// <summary>
    /// Each touched language's whole text file from the unmodded game, with the project's edits applied when apply is set
    /// and nothing is wrong. A mod replaces a language's file whole, so it carries every line, not only the changed ones.
    /// </summary>
    static (Dictionary<string, Locres> Files, List<EditProblem> Problems) PlanText(Game game, ModProject project, bool apply)
    {
        var files = new Dictionary<string, Locres>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<EditProblem>();
        if (project.TextEdits.Count == 0)
            return (files, problems);

        var cultures = GameText.Cultures(game);
        foreach (var edit in project.TextEdits)
        {
            var culture = cultures.FirstOrDefault(c => c.Equals(edit.Culture, StringComparison.OrdinalIgnoreCase));
            if (culture is null)
            {
                problems.Add(new EditProblem(edit, $"the game has no text in {edit.Culture}.", true));
                continue;
            }
            if (!files.TryGetValue(culture, out var file))
                files[culture] = file = GameText.Load(game, culture);
            if (Original(file, edit) is null)
                problems.Add(new EditProblem(edit, "the game has no line with this key in this language.", true));
        }
        if (apply && problems.Count == 0)
            foreach (var edit in project.TextEdits)
                files[edit.Culture].Set(edit.Namespace, edit.Key, edit.Value);
        return (files, problems);
    }

    static string? Original(Locres file, TextEdit edit) => file.Get(edit.Namespace, edit.Key);

    public static string ModsFolder(Settings settings) => Path.Combine(settings.PaksFolder!, "~mods");

    /// <summary>The files a build of this project would replace in ~mods.</summary>
    public static IReadOnlyList<string> Installed(Settings settings, ModProject project) =>
        new[] { ".utoc", ".ucas", ".pak" }
            .Select(ext => Path.Combine(ModsFolder(settings), project.FileName() + ext))
            .Where(File.Exists)
            .ToList();

    /// <summary>
    /// Builds straight into ~mods. Refuses to replace an earlier build unless overwrite is set, and then removes
    /// it first, so a build that no longer has value changes doesn't leave an old container behind.
    /// </summary>
    public static IReadOnlyList<string> Install(Game game, Settings settings, ModProject project, bool overwrite)
    {
        var earlier = Installed(settings, project);
        if (!overwrite && earlier.Count > 0)
            throw new IOException($"{project.FileName()} is already in {ModsFolder(settings)}. Replace it to install this build.");
        var stage = EditableAsset.WorkFolder();
        try
        {
            // Built aside first, so a failed build leaves the earlier install in place.
            var built = Build(game, settings, project, stage);
            foreach (var file in earlier)
                File.Delete(file);
            Directory.CreateDirectory(ModsFolder(settings));
            return built.Select(file =>
            {
                var target = Path.Combine(ModsFolder(settings), Path.GetFileName(file));
                File.Move(file, target, true);
                return target;
            }).ToList();
        }
        finally
        {
            Directory.Delete(stage, true);
        }
    }
}
