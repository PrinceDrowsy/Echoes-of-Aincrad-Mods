using System.Diagnostics;

namespace Cardinal.Mods;

/// <summary>
/// Runs retoc (Truman Kilen and Archengius, MIT), which ships beside Cardinal, to convert between the game's
/// IoStore containers and the loose asset files UAssetAPI edits.
/// retoc downloads Oodle (oo2core_9_win64.dll) next to itself the first time it needs it.
/// </summary>
public static class Retoc
{
    const string EngineVersion = "UE5_3";

    static string Exe => Path.Combine(AppContext.BaseDirectory, "retoc.exe");

    /// <summary>
    /// Extracts one asset from the unmodded game into folder, at its path inside the game.
    /// retoc only reads the containers directly in the Paks folder, never ~mods, and a patch container wins over the base one.
    /// </summary>
    public static string Extract(Settings settings, string file, string folder)
    {
        var (ok, output) = Run("-a", settings.HexKey(), "to-legacy", "--version", EngineVersion, "--no-shaders",
            "--filter", file, settings.PaksFolder!, folder);
        var extracted = Path.Combine(folder, file);
        if (!ok || !File.Exists(extracted))
            throw new GameReadException($"{file} couldn't be extracted from the game files. {output}".Trim());
        return extracted;
    }

    /// <summary>Packs every asset under folder into a container: name.utoc, .ucas and .pak in output.</summary>
    public static IReadOnlyList<string> Pack(string folder, string output, string name)
    {
        Directory.CreateDirectory(output);
        var utoc = Path.Combine(output, name + ".utoc");
        var (ok, text) = Run("to-zen", "--version", EngineVersion, folder, utoc);
        string[] files = [utoc, Path.ChangeExtension(utoc, ".ucas"), Path.ChangeExtension(utoc, ".pak")];
        if (!ok || !files.All(File.Exists))
            throw new IOException($"Packing the mod failed. {text}".Trim());
        return files;
    }

    static (bool Ok, string Output) Run(params string[] arguments)
    {
        if (!File.Exists(Exe))
            throw new SetupException($"retoc.exe is missing. It should be next to Cardinal.exe in {AppContext.BaseDirectory}");
        var start = new ProcessStartInfo(Exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode == 0, (output + error.Result).Trim());
    }
}

/// <summary>A game file couldn't be read or converted.</summary>
public class GameReadException(string message) : Exception(message);
