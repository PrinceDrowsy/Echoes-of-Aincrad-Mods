using System.Text.RegularExpressions;
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

    public EditProblem(RowAddition row, string problem) : this($"new row {row.Label}", problem, true) { }

    public EditProblem(AddedText text, string problem) : this($"new text {text.Label}", problem, true) { }

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

        // An earlier build here could leave a container that this build, with no value changes, wouldn't replace.
        Directory.CreateDirectory(output);
        foreach (var ext in Extensions)
            File.Delete(Path.Combine(output, project.FileName() + ext));
        var stage = EditableAsset.WorkFolder();
        try
        {
            var files = new List<string>();
            if (plan.Assets.Count > 0)
            {
                var assets = Path.Combine(stage, "assets");
                plan.Steps.ForEach(step => step());
                foreach (var asset in plan.Assets)
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

    /// <summary>The game assets a project changes, and the steps that change them, in the order they run.</summary>
    sealed record AssetPlan(List<EditableAsset> Assets, List<Action> Steps);

    // Added rows go in before value edits, so a new row is always a copy of the game's own row, whatever else the mod changes.
    static (AssetPlan Plan, List<EditProblem> Problems) Plan(Game game, Settings settings, ModProject project)
    {
        var problems = new List<EditProblem>();
        var plan = new AssetPlan([], []);
        if (project.Edits.Count + project.AddedRows.Count + project.AddedText.Count == 0)
            return (plan, problems);

        var sources = TableCatalog.Discover(game).ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        var mappings = new Usmap(settings.MappingsFile!);
        var assets = new Dictionary<string, EditableAsset>();
        EditableAsset Asset(string file)
        {
            if (!assets.TryGetValue(file, out var asset))
            {
                assets[file] = asset = EditableAsset.Load(settings, mappings, file);
                plan.Assets.Add(asset);
            }
            return asset;
        }
        EditTarget? Target(string table, Action<string> fail)
        {
            if (!sources.TryGetValue(table, out var source))
                fail("no such table.");
            else if (source.Target is null)
                fail("this table is read-only.");
            return sources.GetValueOrDefault(table)?.Target;
        }

        foreach (var row in project.AddedRows)
        {
            if (Target(row.Table, p => problems.Add(new EditProblem(row, p))) is not { } target)
                continue;
            var asset = Asset(target.File);
            if (asset.Check(target, row) is { } problem)
                problems.Add(new EditProblem(row, problem));
            else
                plan.Steps.Add(() => asset.Apply(target, row));
        }

        foreach (var edit in project.Edits)
        {
            if (Target(edit.Table, p => problems.Add(new EditProblem(edit, p, true))) is not { } target)
                continue;
            var asset = Asset(target.File);
            if (asset.Check(target, edit) is { } problem)
                problems.Add(new EditProblem(edit, problem, true));
            else
                plan.Steps.Add(() => asset.Apply(target, edit));
        }

        foreach (var text in project.AddedText)
        {
            if (text.Namespace != Game.TextNamespace)
                problems.Add(new EditProblem(text, $"new text can only go in {Game.TextNamespace}, the game's main string table."));
            else if (string.IsNullOrWhiteSpace(text.English))
                problems.Add(new EditProblem(text, "it has no English text. Every language without its own falls back to the English."));
            else
            {
                var table = Asset(Game.TextTablePath);
                if (table.HasText(text.Key))
                    problems.Add(new EditProblem(text, "the game already has text with this key, so the mod would hide the game's own."));
                else
                    plan.Steps.Add(() => table.AddText(text.Key, text.English));
            }
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
        // English needs no line for added text: the string table holds the English, and it shows wherever a language has no line.
        var translations = project.AddedText
            .SelectMany(t => t.Text.Where(l => l.Key != GameText.English).Select(l => (Added: t, Culture: l.Key, Line: l.Value)))
            .ToList();
        if (project.TextEdits.Count + translations.Count == 0)
            return (files, problems);

        var cultures = GameText.Cultures(game);
        Locres? Language(string code, Action<string> fail)
        {
            var culture = cultures.FirstOrDefault(c => c.Equals(code, StringComparison.OrdinalIgnoreCase));
            if (culture is null)
            {
                fail($"the game has no text in {code}.");
                return null;
            }
            if (!files.TryGetValue(culture, out var file))
                files[culture] = file = GameText.Load(game, culture);
            return file;
        }

        foreach (var edit in project.TextEdits)
            if (Language(edit.Culture, p => problems.Add(new EditProblem(edit, p, true))) is { } file && Original(file, edit) is null)
                problems.Add(new EditProblem(edit, "the game has no line with this key in this language.", true));
        foreach (var (added, culture, _) in translations)
            Language(culture, p => problems.Add(new EditProblem(added, p)));

        if (apply && problems.Count == 0)
        {
            foreach (var edit in project.TextEdits)
                files[edit.Culture].Set(edit.Namespace, edit.Key, edit.Value);
            foreach (var (added, culture, line) in translations)
                files[culture].Add(added.Namespace, added.Key, line, added.English);
        }
        return (files, problems);
    }

    static string? Original(Locres file, TextEdit edit) => file.Get(edit.Namespace, edit.Key);

    static readonly string[] Extensions = [".utoc", ".ucas", ".pak"];

    public static string ModsFolder(Settings settings) => Path.Combine(settings.PaksFolder!, "~mods");

    /// <summary>The project's builds in ~mods, at any priority, since a build from before a priority change would still load.</summary>
    public static IReadOnlyList<string> Installed(Settings settings, ModProject project)
    {
        if (!Directory.Exists(ModsFolder(settings)))
            return [];
        var build = new Regex($@"^{Regex.Escape(project.BaseName())}_\d+_P$", RegexOptions.IgnoreCase);
        return Directory.EnumerateFiles(ModsFolder(settings))
            .Where(file => Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
                           && build.IsMatch(Path.GetFileNameWithoutExtension(file)))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Builds straight into ~mods. Refuses to replace an earlier build unless overwrite is set, and then removes
    /// it first, so a build that no longer has value changes doesn't leave an old container behind.
    /// </summary>
    public static IReadOnlyList<string> Install(Game game, Settings settings, ModProject project, bool overwrite)
    {
        var earlier = Installed(settings, project);
        if (!overwrite && earlier.Count > 0)
            throw new IOException($"{Builds(earlier)} {(earlier.Count == 1 ? "is" : "are")} already in {ModsFolder(settings)}. Replace them to install this build.");
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

    /// <summary>Installed build files by name, such as "MyMod_1000_P (.pak, .ucas, .utoc)".</summary>
    public static string Builds(IEnumerable<string> files) =>
        string.Join(", ", files.GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
            .Select(g => $"{g.Key} ({string.Join(", ", g.Select(Path.GetExtension))})"));
}
