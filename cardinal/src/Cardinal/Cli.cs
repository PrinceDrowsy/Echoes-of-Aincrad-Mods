using System.IO;
using Cardinal.Maps;
using Cardinal.Mods;
using Cardinal.Tables;
using UAssetAPI.Unversioned;

namespace Cardinal;

/// <summary>The command-line mode. Same readers, exporters and builder as the window.</summary>
static class Cli
{
    const int Ok = 0, Usage = 1, Setup = 2, ReadFailed = 3, WriteFailed = 4;

    const string Help = """
        Cardinal 0.2 - reads Echoes of Aincrad's game files and builds data mods. Run with no arguments for the window.

        Commands:
          setup [--paks DIR] [--key HEX] [--usmap FILE] [--oodle FILE] [--download-oodle]
                Saves settings for later runs and reports anything still missing.
          list tables              Every table: id, row source, and "editable" or "read-only".
          list floors              Floors with a World Partition map.
          list layers FLOOR        A floor's data layers: name, parent, developer, cell count.
          export tables --out DIR [--only ID[,ID...]]
                JSON and CSV per table in DIR/Group/, plus DIR/tables.json.
          export map FLOOR|all --out DIR
                DIR/FLOOR.json, FLOOR-cells.csv and FLOOR-markers.csv.
          set PROJECT TABLE ROW FIELD VALUE
                Checks one change against the game and saves it in the mod project (made if missing).
                Setting a field back to its original value removes the change.
          build PROJECT --out DIR
          build PROJECT --install [--force]
                Builds the project into NAME_P.utoc, .ucas and .pak, in DIR or straight into Paks\~mods.
                --force replaces an earlier build there.

        --paks, --key, --usmap and --oodle also work on any command, for that run only.
        --with-mods reads the game as installed mods change it (Paks\~mods) instead of unmodded.
        Builds and set always start from the unmodded game.

        Exit codes: 0 ok, 1 bad arguments or a change that can't be made, 2 setup missing or wrong,
        3 a game file couldn't be read, 4 writing failed or a build is already installed.
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
                2 when Word(0) == "list" && Word(1) == "tables" => WithGame(options, (game, _) => ListTables(game)),
                2 when Word(0) == "list" && Word(1) == "floors" => WithGame(options, (game, _) => Lines(FloorMap.Floors(game))),
                3 when Word(0) == "list" && Word(1) == "layers" => WithGame(options, (game, _) => ListLayers(game, w[2])),
                2 when Word(0) == "export" && Word(1) == "tables" => WithGame(options, (game, _) => ExportTables(game, options)),
                3 when Word(0) == "export" && Word(1) == "map" => WithGame(options, (game, _) => ExportMap(game, w[2], options)),
                6 when Word(0) == "set" => WithGame(options, (game, settings) => SetValue(game, settings, w[1], w[2], w[3], w[4], w[5])),
                2 when Word(0) == "build" => WithGame(options, (game, settings) => Build(game, settings, w[1], options)),
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
        {
            try
            {
                return command(game, settings);
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

    static int SetValue(Game game, Settings settings, string projectPath, string tableId, string row, string field, string value)
    {
        var source = TableCatalog.Discover(game).FirstOrDefault(s => s.Id.Equals(tableId, StringComparison.OrdinalIgnoreCase))
                     ?? throw new ArgumentException($"No table named {tableId}. Run Cardinal list tables.");
        var target = source.Target ?? throw new EditException($"{source.Id} is read-only.");
        var table = source.Load(game);
        var index = table.Rows.ToList().FindIndex(r => r.Key == row);
        if (index < 0)
            throw new EditException($"{source.Id} has no row {row}.");

        var original = table.Field(index, field) is Newtonsoft.Json.Linq.JValue v ? Table.Cell(v) : null;
        var edit = new ModEdit(source.Id, row, field, value, original);
        var asset = EditableAsset.Load(settings, new Usmap(settings.MappingsFile!), target.File);
        if (asset.Check(target, edit) is { } problem)
            throw new EditException(problem);

        var project = File.Exists(projectPath)
            ? ModProject.Load(projectPath)
            : new ModProject { Name = Path.GetFileNameWithoutExtension(projectPath) };
        var unchanged = ModEdit.Same(value, original);
        if (unchanged)
            project.Remove(source.Id, row, field);
        else
            project.Set(edit);
        project.Save(projectPath);
        Console.WriteLine(unchanged
            ? $"{source.Id} / {row} / {field} is back to its original value; change removed."
            : $"{source.Id} / {row} / {field}: {original} -> {value}. {project.Edits.Count} changes in {projectPath}");
        return Ok;
    }

    static int Build(Game game, Settings settings, string projectPath, Options options)
    {
        if (!File.Exists(projectPath))
            throw new ArgumentException($"No mod project at {projectPath}.");
        var project = ModProject.Load(projectPath);
        IReadOnlyList<string> files;
        if (options.Has("install"))
            files = ModBuilder.Install(game, settings, project, options.Has("force"));
        else
            files = ModBuilder.Build(game, settings, project, options.Required("out"));
        Console.WriteLine($"Built {project.Edits.Count} changes into:");
        return Lines(files);
    }

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
        static readonly HashSet<string> Flags = ["download-oodle", "with-mods", "install", "force"];
        static readonly HashSet<string> Valued = ["paks", "key", "usmap", "oodle", "out", "only"];

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

        public string Required(string name) =>
            Value(name) ?? throw new ArgumentException($"--{name} is required for this command.");

        public Settings Apply(Settings settings)
        {
            settings.PaksFolder = Value("paks") ?? settings.PaksFolder;
            settings.AesKey = Value("key") ?? settings.AesKey;
            settings.MappingsFile = Value("usmap") ?? settings.MappingsFile;
            settings.OodleFile = Value("oodle") ?? settings.OodleFile;
            return settings;
        }
    }
}
