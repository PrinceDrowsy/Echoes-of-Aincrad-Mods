using System.IO;
using Cardinal.Maps;
using Cardinal.Mods;
using Cardinal.Tables;
using Cardinal.Text;
using UAssetAPI.Unversioned;

namespace Cardinal;

/// <summary>The command-line mode. Same readers, exporters and builder as the window.</summary>
static class Cli
{
    const int Ok = 0, Usage = 1, Setup = 2, ReadFailed = 3, WriteFailed = 4, Found = 5;

    static readonly string Help = $"""
        Cardinal {Diagnostics.Version} - reads Echoes of Aincrad's game files and builds data, text and new-item mods. Run with no arguments for the window.

        Commands:
          setup [--paks DIR] [--key HEX] [--usmap FILE] [--oodle FILE] [--download-oodle]
                Saves settings for later runs and reports anything still missing.
          diagnostics              Setup, game version and installed mods for a bug report. Never shows the key.
          list tables              Every table: id, row source, and "editable" or "read-only".
          list floors              Floors with a World Partition map.
          list layers FLOOR        A floor's data layers: name, parent, developer, cell count.
          list cultures            Languages the game has text for: code, name, number of lines.
          export tables --out DIR [--only ID[,ID...]]
                JSON and CSV per table in DIR/Group/, plus DIR/tables.json.
          export map FLOOR|all --out DIR
                DIR/FLOOR.json, FLOOR-cells.csv and FLOOR-markers.csv.
          set PROJECT TABLE ROW FIELD VALUE
                Checks one change against the game and saves it in the mod project (made if missing).
                Setting a field back to its original value removes the change.
          text get CULTURE KEY
                Every line with that key in that language: namespace, key, text.
          text set PROJECT CULTURE NAMESPACE KEY TEXT
                Changes one line of text in the mod project (made if missing). The game's own text removes the change.
                For a key the project adds, sets that language's line; empty text makes the language show the English.
          new item PROJECT --from ROW --name TEXT --description TEXT [--id ID] [--category TABLE]
                Adds an item that starts as a copy of item ROW, with its own ID (the next free one if left out)
                and its own English name and description. --category is the item table, such as Items/UseItem,
                needed when ROW is an ID in more than one of them.
          new town PROJECT --from ROW --name TEXT [--id ID]
                Adds a town that starts as a copy of DT_TownList row ROW, with its own town ID (the next free one
                if left out) and English name, and a copy of that town's Town_ asset.
          new asset PROJECT FROM PACKAGE
                Adds a copy of the game asset FROM as the new asset PACKAGE, both /Game paths, such as
                /Game/ROD/DataAssets/Town/Town_004 and /Game/ROD/DataAssets/Town/Town_011.
          text export CULTURE --out FILE [--project PROJECT]
                CSV of every line in that language next to its English, with the project's changes applied.
          text import CULTURE FILE PROJECT
                Reads a translated CSV from text export back into the project, and lists lines that don't
                exist in the game or whose English changed since the export.
          check PROJECT
                Re-checks every change against the current game, for example after a game update.
          build PROJECT --out DIR
          build PROJECT --install [--force]
                Builds the project into NAME_PRIORITY_P.utoc and .ucas for value changes and NAME_PRIORITY_P.pak,
                which holds changed text, in DIR or straight into Paks\~mods. --force replaces an earlier build there.
                Installing a project with new assets also updates the registry patch.
          merge PROJECT PROJECT... --out DIR
          merge PROJECT PROJECT... --install [--force] [--remove-single-builds]
                Combines the projects into one {ModMerge.Name}_{ModProject.MergedPriority}_P patch. Where two change the
                value or line of text, the one listed later wins. --remove-single-builds deletes their own builds from ~mods.
          conflicts
                Lists files that more than one installed mod replaces, and which copy the game loads.
          patch
                Rebuilds {RegistryPatch.FileName}.pak from every installed mod's new assets, so the game finds all
                of them. Run it after installing or removing mods that add assets.

        --paks, --key, --usmap and --oodle also work on any command, for that run only.
        --with-mods reads the game as installed mods change it (Paks\~mods) instead of unmodded.
        set, text set, text import, new item, new town, new asset, check, build and merge always work from the unmodded game.

        Exit codes: 0 ok, 1 bad arguments or a change that can't be made, 2 setup missing or wrong,
        3 a game file couldn't be read, 4 writing failed or a build is already installed,
        5 check, conflicts or patch found something to look at.
        """;

    public static int Run(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            var w = options.Words;
            string Word(int i) => w[i].ToLowerInvariant();
            return w.Count switch
            {
                0 => Print(Help),
                1 when Word(0) == "help" => Print(Help),
                1 when Word(0) == "setup" => RunSetup(options),
                1 when Word(0) == "diagnostics" => Print(Diagnostics.Report(options.Apply(Settings.Load()))),
                1 when Word(0) == "conflicts" => ListConflicts(options),
                2 when Word(0) == "list" && Word(1) == "tables" => WithGame(options, (game, _) => ListTables(game)),
                2 when Word(0) == "list" && Word(1) == "floors" => WithGame(options, (game, _) => Lines(FloorMap.Floors(game))),
                3 when Word(0) == "list" && Word(1) == "layers" => WithGame(options, (game, _) => ListLayers(game, w[2])),
                2 when Word(0) == "list" && Word(1) == "cultures" => WithGame(options, (game, _) => ListCultures(game)),
                4 when Word(0) == "text" && Word(1) == "get" => WithGame(options, (game, _) => GetText(game, w[2], w[3])),
                7 when Word(0) == "text" && Word(1) == "set" => WithUnmoddedGame(options, (game, _) => SetText(game, w[2], w[3], w[4], w[5], w[6])),
                3 when Word(0) == "text" && Word(1) == "export" => WithGame(options, (game, _) => ExportText(game, w[2], options)),
                5 when Word(0) == "text" && Word(1) == "import" => WithUnmoddedGame(options, (game, _) => ImportText(game, w[2], w[3], w[4])),
                2 when Word(0) == "export" && Word(1) == "tables" => WithGame(options, (game, _) => ExportTables(game, options)),
                3 when Word(0) == "export" && Word(1) == "map" => WithGame(options, (game, _) => ExportMap(game, w[2], options)),
                3 when Word(0) == "new" && Word(1) == "item" => WithUnmoddedGame(options, (game, settings) => NewItem(game, settings, w[2], options)),
                3 when Word(0) == "new" && Word(1) == "town" => WithUnmoddedGame(options, (game, settings) => NewTown(game, settings, w[2], options)),
                5 when Word(0) == "new" && Word(1) == "asset" => WithUnmoddedGame(options, (game, _) => NewAsset(game, w[2], w[3], w[4])),
                1 when Word(0) == "patch" => Guarded(() => Patch(options.Apply(Settings.Load()))),
                6 when Word(0) == "set" => WithUnmoddedGame(options, (game, settings) => SetValue(game, settings, w[1], w[2], w[3], w[4], w[5])),
                2 when Word(0) == "check" => WithUnmoddedGame(options, (game, settings) => Check(game, settings, w[1])),
                2 when Word(0) == "build" => WithUnmoddedGame(options, (game, settings) => Build(game, settings, w[1], options)),
                >= 3 when Word(0) == "merge" => WithUnmoddedGame(options, (game, settings) => Merge(game, settings, w.Skip(1).ToList(), options)),
                _ => Fail(Usage, "Unknown command. Run Cardinal help for the list."),
            };
        }
        catch (ArgumentException e)
        {
            return Fail(Usage, e.Message);
        }
    }

    static int RunSetup(Options options)
    {
        var settings = options.Apply(Settings.Load());
        if (options.Has("download-oodle"))
            settings.OodleFile = Settings.DownloadOodle().GetAwaiter().GetResult();
        settings.Save();
        var problems = settings.Problems().ToList();
        Console.WriteLine($"Settings saved to {Settings.Folder}");
        problems.ForEach(p => Console.Error.WriteLine(p));
        return problems.Count == 0 ? Ok : Setup;
    }

    static int WithGame(Options options, Func<Game, Settings, int> command)
    {
        var settings = options.Apply(Settings.Load());
        Game game;
        try
        {
            game = Game.Open(settings, options.Has("with-mods"));
        }
        catch (Exception e)
        {
            return Fail(Setup, e.Message);
        }
        using (game)
            return Guarded(() => command(game, settings));
    }

    static int WithUnmoddedGame(Options options, Func<Game, Settings, int> command) =>
        options.Has("with-mods")
            ? Fail(Usage, "This command always works from the unmodded game, so it can't take --with-mods.")
            : WithGame(options, command);

    static int Guarded(Func<int> command)
    {
        try
        {
            return command();
        }
        catch (Exception e) when (e is ArgumentException or EditException)
        {
            return Fail(Usage, e.Message);
        }
        catch (SetupException e)
        {
            return Fail(Setup, e.Message);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Fail(WriteFailed, e.Message);
        }
        catch (Exception e)
        {
            return Fail(ReadFailed, e.Message);
        }
    }

    static int ListTables(Game game) =>
        Lines(TableCatalog.Discover(game).Select(s => $"{s.Id}\t{s.Source}\t{(s.Target is null ? "read-only" : "editable")}"));

    static int ListLayers(Game game, string floor) =>
        Lines(FloorMap.Load(game, floor).Layers.Select(l => $"{l.Name}\t{l.Parent}\t{(l.Developer ? "developer" : "world")}\t{l.CellCount}"));

    static int ExportTables(Game game, Options options)
    {
        var sources = TableCatalog.Discover(game);
        if (options.Value("only") is { } only)
        {
            var wanted = only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unknown = wanted.Except(sources.Select(s => s.Id), StringComparer.OrdinalIgnoreCase).ToList();
            if (unknown.Count > 0)
                throw new ArgumentException($"No table named {string.Join(", ", unknown)}. Run Cardinal list tables.");
            sources = sources.Where(s => wanted.Contains(s.Id)).ToList();
        }
        var results = Export.Tables(game, sources, options.Required("out"));
        foreach (var failed in results.Where(r => r.Error is not null))
            Console.Error.WriteLine($"{failed.Id}: {failed.Error}");
        Console.WriteLine($"Exported {results.Count(r => r.Error is null)} of {results.Count} tables to {options.Required("out")}");
        return results.Any(r => r.Error is not null) ? ReadFailed : Ok;
    }

    static int ExportMap(Game game, string floor, Options options)
    {
        IReadOnlyList<string> floors = floor.Equals("all", StringComparison.OrdinalIgnoreCase) ? FloorMap.Floors(game) : [floor];
        foreach (var id in floors)
        {
            var map = Export.Floor(game, id, options.Required("out"));
            Console.WriteLine($"{map.Id}: {map.Cells.Count} cells, {map.Layers.Count} layers");
        }
        return Ok;
    }

    static int ListCultures(Game game) =>
        Lines(GameText.Cultures(game).Select(c => $"{c}\t{GameText.Name(c)}\t{GameText.Load(game, c).Count}"));

    static string Culture(Game game, string culture) =>
        GameText.Find(game, culture) ?? throw new ArgumentException($"The game has no text in {culture}. Run Cardinal list cultures.");

    static ModProject OpenOrNew(string path) =>
        File.Exists(path) ? ModProject.Load(path) : new ModProject { Name = Path.GetFileNameWithoutExtension(path) };

    static int GetText(Game game, string culture, string key)
    {
        var lines = GameText.Load(game, Culture(game, culture)).Lines.Where(l => l.Key == key).ToList();
        if (lines.Count == 0 && game.SourceText(key) is { } english)
            return Print($"{Game.TextNamespace}\t{key}\t{english}\t(the string table's English: {culture} has no line of its own, so it shows this)");
        if (lines.Count == 0)
            throw new EditException($"{culture} has no line with the key {key}.");
        return Lines(lines.Select(l => $"{l.Namespace}\t{l.Key}\t{l.Text}"));
    }

    static int SetText(Game game, string projectPath, string culture, string ns, string key, string text)
    {
        culture = Culture(game, culture);
        var project = OpenOrNew(projectPath);
        if (TextEditing.SetAdded(project, culture, ns, key, text))
        {
            project.Save(projectPath);
            Console.WriteLine($"{culture} / {ns} / {key}: set for the text this project adds.");
            return Ok;
        }
        var original = TextEditing.Set(project, GameText.Load(game, culture), culture, ns, key, text);
        project.Save(projectPath);
        Console.WriteLine(text == original
            ? $"{culture} / {ns} / {key} is back to the game's own text; change removed."
            : $"{culture} / {ns} / {key}: {original} -> {text}. {project.Changes} changes in {projectPath}");
        return Ok;
    }

    static int ExportText(Game game, string culture, Options options)
    {
        culture = Culture(game, culture);
        var project = options.Value("project") is { } path ? LoadProject(path) : new ModProject();
        var output = options.Required("out");
        TextEditing.ExportCsv(project, GameText.Load(game, GameText.English), GameText.Load(game, culture), culture, output);
        Console.WriteLine($"Saved {culture} next to its English to {output}");
        return Ok;
    }

    static int ImportText(Game game, string culture, string csvPath, string projectPath)
    {
        culture = Culture(game, culture);
        if (!File.Exists(csvPath))
            throw new ArgumentException($"No file at {csvPath}.");
        var project = OpenOrNew(projectPath);
        var result = TextEditing.ImportCsv(project, GameText.Load(game, GameText.English), GameText.Load(game, culture), culture, csvPath);
        project.Save(projectPath);
        foreach (var line in result.Missing)
            Console.WriteLine($"not in the game  {line}");
        foreach (var line in result.EnglishChanged)
            Console.WriteLine($"English changed  {line}");
        Console.WriteLine($"Imported {result.Changed} changed lines into {projectPath} ({result.Unchanged} already matched). " +
                          $"Not in the game: {result.Missing.Count}. English changed since the export: {result.EnglishChanged.Count}.");
        return result.Missing.Count + result.EnglishChanged.Count == 0 ? Ok : Found;
    }

    static int SetValue(Game game, Settings settings, string projectPath, string tableId, string row, string field, string value)
    {
        var source = TableCatalog.Discover(game).FirstOrDefault(s => s.Id.Equals(tableId, StringComparison.OrdinalIgnoreCase))
                     ?? throw new ArgumentException($"No table named {tableId}. Run Cardinal list tables.");
        var target = source.Target ?? throw new EditException($"{source.Id} is read-only.");
        var table = source.Load(game);
        var index = table.Rows.ToList().FindIndex(r => r.Key == row);
        if (index < 0)
            return SetAdded(settings, projectPath, source, target, row, field, value);

        var original = table.Field(index, field) is Newtonsoft.Json.Linq.JValue v ? Table.Cell(v) : null;
        var edit = new ModEdit(source.Id, row, field, value, original);
        var asset = EditableAsset.Load(settings, new Usmap(settings.MappingsFile!), target.File);
        if (asset.Check(target, edit) is { } problem)
            throw new EditException(problem);

        var project = OpenOrNew(projectPath);
        var unchanged = ModEdit.Same(value, original);
        if (unchanged)
            project.Remove(source.Id, row, field);
        else
            project.Set(edit);
        project.Save(projectPath);
        Console.WriteLine(unchanged
            ? $"{source.Id} / {row} / {field} is back to its original value; change removed."
            : $"{source.Id} / {row} / {field}: {original} -> {value}. {project.Changes} changes in {projectPath}");
        return Ok;
    }

    // A value of a row the project adds goes into the addition itself, as in the window.
    static int SetAdded(Settings settings, string projectPath, TableSource source, EditTarget target, string row, string field, string value)
    {
        var project = OpenOrNew(projectPath);
        var added = project.FindRow(source.Id, row) ?? throw new EditException($"{source.Id} has no row {row}, and the project doesn't add one.");
        var changed = added with { Fields = new Dictionary<string, string>(added.Fields) { [field] = value } };
        if (EditableAsset.Load(settings, new Usmap(settings.MappingsFile!), target.File).Check(target, changed) is { } problem)
            throw new EditException(problem);
        project.SetRow(changed);
        project.Save(projectPath);
        return Print($"{source.Id} / {row} (new row) / {field}: {value}. {project.Changes} changes in {projectPath}");
    }

    static int NewItem(Game game, Settings settings, string projectPath, Options options)
    {
        var sources = TableCatalog.Discover(game);
        var from = options.Required("from");
        var kinds = ItemTemplate.Kinds(sources);
        TableSource kind;
        if (options.Value("category") is { } category)
            kind = kinds.FirstOrDefault(k => k.Id.Equals(category, StringComparison.OrdinalIgnoreCase))
                   ?? throw new ArgumentException($"{category} isn't an item kind. Item kinds: {string.Join(", ", kinds.Select(k => k.Id))}");
        else
        {
            var holding = kinds.Where(k => k.Load(game).Rows.Any(r => r.Key == from)).ToList();
            kind = holding.Count == 1
                ? holding[0]
                : throw new ArgumentException(holding.Count == 0
                    ? $"No item kind has an item {from}."
                    : $"Item {from} exists in {holding.Count} kinds, so say which with --category: {string.Join(", ", holding.Select(k => k.Id))}");
        }

        var project = OpenOrNew(projectPath);
        var id = options.Value("id") is { } text
            ? int.TryParse(text, out var parsed) ? parsed : throw new ArgumentException("--id needs a whole number.")
            : ItemTemplate.NextId(kind.Load(game), project, kind.Id);
        var item = new NewItem(kind.Id, from, id, options.Required("name"), options.Required("description"));
        var notes = ItemTemplate.Add(game, settings, project, sources, item);
        project.Save(projectPath);
        Console.WriteLine($"Added item {id} to {kind.Id} in {projectPath}. Its text keys are in the project; set other languages with text set.");
        return Lines(notes);
    }

    static int Build(Game game, Settings settings, string projectPath, Options options)
    {
        var project = LoadProject(projectPath);
        var files = options.Has("install")
            ? ModBuilder.Install(game, settings, project, options.Has("force"))
            : ModBuilder.Build(game, settings, project, options.Required("out"));
        Console.WriteLine($"Built {project.Changes} changes into:");
        Lines(files);
        return options.Has("install") && project.AddedAssets.Count > 0 ? Patch(settings) : Ok;
    }

    static int NewTown(Game game, Settings settings, string projectPath, Options options)
    {
        var sources = TableCatalog.Discover(game);
        var project = OpenOrNew(projectPath);
        var id = options.Value("id") is { } text
            ? int.TryParse(text, out var parsed) ? parsed : throw new ArgumentException("--id needs a whole number.")
            : TownTemplate.NextId(sources.Single(s => s.Id == TownTemplate.Table).Load(game), project);
        var name = options.Required("name");
        var notes = TownTemplate.Add(game, settings, project, sources, new NewTown(options.Required("from"), id, name));
        project.Save(projectPath);
        Console.WriteLine($"Added the town {name} with ID {id:000} to {projectPath}.");
        return Lines(notes);
    }

    static int NewAsset(Game game, string projectPath, string from, string package)
    {
        var project = OpenOrNew(projectPath);
        var copy = new AssetCopy(from, package);
        if (AssetCopier.Check(game, AssetRegistry.Read(game.Provider.SaveAsset(AssetRegistry.File)), copy) is { } problem)
            throw new EditException($"{package}: {problem}");
        project.SetAsset(copy);
        project.Save(projectPath);
        return Print($"Added {package} as a copy of {from}. {project.Changes} changes in {projectPath}");
    }

    // Prints what the registry patch did. Problems exit with Found, since the assets they name won't be found in game.
    static int Patch(Settings settings)
    {
        var result = RegistryPatch.Build(settings);
        foreach (var copy in result.Added)
            Console.WriteLine($"listed {copy.Package} (copy of {copy.From})");
        foreach (var problem in result.Problems)
            Console.WriteLine($"problem  {problem}");
        Console.WriteLine(result.Pak is null
            ? "No installed mod adds assets, so no registry patch is needed. Any old one was removed."
            : $"Registry patch with {result.Added.Count} new assets: {result.Pak}");
        return result.Problems.Count == 0 ? Ok : Found;
    }

    static ModProject LoadProject(string path) =>
        File.Exists(path) ? ModProject.Load(path) : throw new ArgumentException($"No mod project at {path}.");

    static int Check(Game game, Settings settings, string projectPath)
    {
        var project = LoadProject(projectPath);
        var problems = ModBuilder.Check(game, settings, project);
        foreach (var problem in problems)
            Console.WriteLine($"{(problem.Blocks ? "can't build" : "changed   ")}  {problem}");
        Console.WriteLine(problems.Count == 0
            ? $"All {project.Changes} changes still fit the game."
            : $"Of {project.Changes} changes, can't be made any more: {problems.Count(p => p.Blocks)}. Original value changed: {problems.Count(p => !p.Blocks)}.");
        return problems.Count == 0 ? Ok : Found;
    }

    static int Merge(Game game, Settings settings, IReadOnlyList<string> projectPaths, Options options)
    {
        var projects = projectPaths.Select(LoadProject).ToList();
        var (merged, clashes) = ModMerge.Merge(projects);
        foreach (var clash in clashes)
            Console.WriteLine(clash);
        var files = options.Has("install")
            ? ModBuilder.Install(game, settings, merged, options.Has("force"))
            : ModBuilder.Build(game, settings, merged, options.Required("out"));
        Console.WriteLine($"Merged {projects.Count} projects, {merged.Changes} changes, {clashes.Count} decided by order, into:");
        Lines(files);
        if (options.Has("install") && options.Has("remove-single-builds"))
            foreach (var file in projects.SelectMany(p => ModBuilder.Installed(settings, p)))
            {
                File.Delete(file);
                Console.WriteLine($"Removed {file}");
            }
        return options.Has("install") && merged.AddedAssets.Count > 0 ? Patch(settings) : Ok;
    }

    static int ListConflicts(Options options) => Guarded(() =>
    {
        var conflicts = ModConflicts.Scan(options.Apply(Settings.Load()));
        foreach (var mod in conflicts.Mods)
            Console.WriteLine($"mod\t{mod.Name}\t{mod.Assets.Count} {(mod.Assets.Count == 1 ? "file" : "files")}");
        foreach (var conflict in conflicts.Conflicts)
            Console.WriteLine($"conflict\t{conflict.Asset}\t{conflict.Outcome()}");
        Console.WriteLine($"{conflicts.Mods.Count} mods installed, {conflicts.Conflicts.Count} files with a conflict.");
        return conflicts.Conflicts.Count == 0 ? Ok : Found;
    });

    static int Lines(IEnumerable<string> lines)
    {
        foreach (var line in lines)
            Console.WriteLine(line);
        return Ok;
    }

    static int Print(string text)
    {
        Console.WriteLine(text);
        return Ok;
    }

    static int Fail(int code, string message)
    {
        Console.Error.WriteLine(message);
        return code;
    }

    sealed class Options
    {
        static readonly HashSet<string> Flags = ["download-oodle", "with-mods", "install", "force", "remove-single-builds"];
        static readonly HashSet<string> Valued = ["paks", "key", "usmap", "oodle", "out", "only", "project", "from", "id", "name", "description", "category"];

        // Kept as typed: row keys, values and paths are case-sensitive. Command words are compared case-insensitively.
        public List<string> Words { get; } = [];
        readonly Dictionary<string, string?> values = [];

        public static Options Parse(string[] args)
        {
            var options = new Options();
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] is "--help" or "-h" or "/?")
                    options.Words.Add("help");
                else if (!args[i].StartsWith("--"))
                    options.Words.Add(args[i]);
                else if (Flags.Contains(args[i][2..]))
                    options.values[args[i][2..]] = null;
                else if (Valued.Contains(args[i][2..]) && i + 1 < args.Length)
                    options.values[args[i][2..]] = args[++i];
                else
                    throw new ArgumentException($"Unknown option or missing value: {args[i]}");
            }
            return options;
        }

        public bool Has(string name) => values.ContainsKey(name);

        public string? Value(string name) => values.GetValueOrDefault(name);

        string? Full(string name) => Value(name) is { } path ? Path.GetFullPath(path) : null;

        public string Required(string name) =>
            Value(name) ?? throw new ArgumentException($"--{name} is required for this command.");

        public Settings Apply(Settings settings)
        {
            // Full paths, since retoc and repak run from Cardinal's own folder.
            settings.PaksFolder = Full("paks") ?? settings.PaksFolder;
            settings.AesKey = Value("key") ?? settings.AesKey;
            settings.MappingsFile = Full("usmap") ?? settings.MappingsFile;
            settings.OodleFile = Full("oodle") ?? settings.OodleFile;
            return settings;
        }
    }
}
