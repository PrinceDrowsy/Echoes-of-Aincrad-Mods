using System.IO;
using Cardinal.Maps;
using Cardinal.Tables;

namespace Cardinal;

/// <summary>The command-line mode. Same readers and exporters as the window.</summary>
static class Cli
{
    const int Ok = 0, Usage = 1, Setup = 2, ReadFailed = 3, WriteFailed = 4;

    const string Help = """
        Cardinal 0.1 - reads Echoes of Aincrad's game files. Run with no arguments for the window.

        Commands:
          setup [--paks DIR] [--key HEX] [--usmap FILE] [--oodle FILE] [--download-oodle]
                Saves settings for later runs and reports anything still missing.
          list tables              Every table: id, row source.
          list floors              Floors with a World Partition map.
          list layers FLOOR        A floor's data layers: name, parent, developer, cell count.
          export tables --out DIR [--only ID[,ID...]]
                JSON and CSV per table in DIR/Group/, plus DIR/tables.json.
          export map FLOOR|all --out DIR
                DIR/FLOOR.json, FLOOR-cells.csv and FLOOR-markers.csv.

        --paks, --key, --usmap and --oodle also work on any command, for that run only.

        Exit codes: 0 ok, 1 bad arguments, 2 setup missing or wrong, 3 a game file couldn't be read, 4 writing failed.
        """;

    public static int Run(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            return options.Words switch
            {
                [] or ["help"] => Print(Help),
                ["setup"] => RunSetup(options),
                ["list", "tables"] => WithGame(options, ListTables),
                ["list", "floors"] => WithGame(options, game => Lines(FloorMap.Floors(game))),
                ["list", "layers", var floor] => WithGame(options, game => ListLayers(game, floor)),
                ["export", "tables"] => WithGame(options, game => ExportTables(game, options)),
                ["export", "map", var floor] => WithGame(options, game => ExportMap(game, floor, options)),
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

    static int WithGame(Options options, Func<Game, int> command)
    {
        Game game;
        try
        {
            game = Game.Open(options.Apply(Settings.Load()));
        }
        catch (Exception e)
        {
            return Fail(Setup, e.Message);
        }
        using (game)
        {
            try
            {
                return command(game);
            }
            catch (ArgumentException e)
            {
                return Fail(Usage, e.Message);
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
        Lines(TableCatalog.Discover(game).Select(s => $"{s.Id}\t{s.Source}"));

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
        IReadOnlyList<string> floors = floor == "all" ? FloorMap.Floors(game) : [floor];
        foreach (var id in floors)
        {
            var map = Export.Floor(game, id, options.Required("out"));
            Console.WriteLine($"{map.Id}: {map.Cells.Count} cells, {map.Layers.Count} layers");
        }
        return Ok;
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
        static readonly HashSet<string> Flags = ["download-oodle"];
        static readonly HashSet<string> Valued = ["paks", "key", "usmap", "oodle", "out", "only"];

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
                    options.Words.Add(args[i].ToLowerInvariant());
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
