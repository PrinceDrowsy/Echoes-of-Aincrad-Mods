using System.Diagnostics;

namespace Cardinal.Mods;

/// <summary>
/// Runs retoc (Truman Kilen and Archengius, MIT), which ships beside Cardinal, to convert between the game's
/// IoStore containers and the loose asset files UAssetAPI edits.
/// retoc downloads Oodle (oo2core_9_win64.dll) next to itself the first time it needs it, from the same OodleUE
/// builds as Cardinal's own download, and rejects any copy that doesn't match its pinned hash, so the player's own
/// Oodle file can't be handed to it.
/// </summary>
public static class Retoc
{
    const string EngineVersion = "UE5_3";

    /// <summary>
    /// Extracts one asset from the unmodded game into folder, at its path inside the game.
    /// retoc only reads the containers directly in the Paks folder, never ~mods, and a patch container wins over the base one.
    /// </summary>
    public static string Extract(Settings settings, string file, string folder)
    {
        var (ok, output) = Tools.Run("retoc.exe", "-a", settings.HexKey(), "to-legacy", "--version", EngineVersion, "--no-shaders",
            "--filter", file, settings.PaksFolder!, folder);
        var extracted = Path.Combine(folder, file);
        if (!ok || !File.Exists(extracted))
            throw new GameReadException($"{file} couldn't be extracted from the game files. {output}".Trim());
        return extracted;
    }

    /// <summary>
    /// Packs every asset under folder into a container: name.utoc, .ucas and .pak in output.
    /// Anything that isn't an asset, like a .locres, is left out; Repak packs those into the .pak.
    /// </summary>
    public static IReadOnlyList<string> Pack(string folder, string output, string name)
    {
        Directory.CreateDirectory(output);
        var utoc = Path.Combine(output, name + ".utoc");
        var (ok, text) = Tools.Run("retoc.exe", "to-zen", "--version", EngineVersion, folder, utoc);
        string[] files = [utoc, Path.ChangeExtension(utoc, ".ucas"), Path.ChangeExtension(utoc, ".pak")];
        if (!ok || !files.All(File.Exists))
            throw new IOException($"Packing the mod failed. {text}".Trim());
        return files;
    }
}

/// <summary>
/// Runs repak (Truman Kilen, MIT or Apache-2.0), which ships beside Cardinal, to pack loose files into a plain .pak.
/// The game reads its text from .pak files rather than IoStore containers, so text mods go in one too.
/// </summary>
public static class Repak
{
    /// <summary>
    /// Writes everything under folder into output/name.pak, replacing the empty .pak retoc writes beside a container.
    /// Uncompressed and unencrypted, in the same pak version the game's own .pak files use.
    /// </summary>
    public static string Pack(string folder, string output, string name)
    {
        Directory.CreateDirectory(output);
        var pak = Path.Combine(output, name + ".pak");
        var (ok, text) = Tools.Run("repak.exe", "pack", "--version", "V11", "--mount-point", "../../../", "--quiet", folder, pak);
        if (!ok || !File.Exists(pak))
            throw new IOException($"Packing the text failed. {text}".Trim());
        return pak;
    }
}

static class Tools
{
    public static string Path(string exe) => System.IO.Path.Combine(AppContext.BaseDirectory, exe);

    public static (bool Ok, string Output) Run(string exe, params string[] arguments)
    {
        if (!File.Exists(Path(exe)))
            throw new SetupException($"{exe} is missing. It should be next to Cardinal.exe in {AppContext.BaseDirectory}");
        var start = new ProcessStartInfo(Path(exe))
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
