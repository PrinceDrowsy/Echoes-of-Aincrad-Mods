using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace Cardinal;

/// <summary>
/// A plain-text summary for bug reports. It never includes the AES key, and paths are shown relative to the
/// game folder, or as a bare file name when they're elsewhere, so nothing about the player's PC leaks into a report.
/// </summary>
public static class Diagnostics
{
    public static string Version { get; } =
        (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown")
        .Split('+')[0];

    public static string Report(Settings settings)
    {
        var text = new StringBuilder();
        text.AppendLine($"Cardinal {Version}");

        var paks = settings.PaksFolder is { } p && Directory.Exists(p) ? Path.GetFullPath(p) : null;
        var root = paks is null ? null : Path.GetFullPath(Path.Combine(paks, "..", "..", ".."));
        string Show(string? path) =>
            string.IsNullOrWhiteSpace(path) ? "not set"
            : !File.Exists(path) ? $"{Path.GetFileName(path)} (missing)"
            : root is not null && Path.GetFullPath(path).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? $"<game>/{Path.GetRelativePath(root, path).Replace('\\', '/')}"
                : $"{Path.GetFileName(path)} (outside the game folder)";

        if (paks is null)
            text.AppendLine("Game: Paks folder not set or missing");
        else
        {
            var exe = Path.Combine(root!, "EchoesofAincrad", "Binaries", "Win64", "EchoesofAincrad-Win64-Shipping.exe");
            text.AppendLine(File.Exists(exe)
                ? $"Game exe: {Bytes(new FileInfo(exe).Length)}, version {(FileVersionInfo.GetVersionInfo(exe).ProductVersion is { Length: > 0 } v ? v : "not stamped")}"
                : "Game exe: not found");
            foreach (var container in Directory.EnumerateFiles(paks, "*.utoc").Order())
                text.AppendLine($"  {Path.GetFileName(container)}: {Bytes(new FileInfo(container).Length)}");
        }

        text.AppendLine($"AES key: {(settings.HasValidKey ? "set" : "missing or malformed")}");
        text.AppendLine($"Mappings: {Show(settings.MappingsFile)}" +
                        (File.Exists(settings.MappingsFile) ? $", {File.GetLastWriteTime(settings.MappingsFile):yyyy-MM-dd HH:mm}" : ""));
        text.AppendLine($"Oodle: {Show(settings.OodleFile)}");
        foreach (var tool in new[] { "retoc", "repak" })
            text.AppendLine($"{tool}: {(File.Exists(Path.Combine(AppContext.BaseDirectory, tool + ".exe")) ? "present" : "missing")}");

        var problems = settings.Problems().ToList();
        text.AppendLine(problems.Count == 0 ? "Setup: complete" : "Setup problems:");
        foreach (var problem in problems)
            text.AppendLine($"  {problem}");

        if (paks is not null)
        {
            var installed = Directory.EnumerateDirectories(paks)
                .SelectMany(dir => Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                .Where(f => f.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".utoc", StringComparison.OrdinalIgnoreCase))
                .Order()
                .ToList();
            text.AppendLine(installed.Count == 0 ? "Mods under Paks: none" : "Mods under Paks:");
            foreach (var file in installed)
                text.AppendLine($"  {Path.GetRelativePath(paks, file).Replace('\\', '/')}: {Bytes(new FileInfo(file).Length)}");
        }
        return text.ToString();
    }

    // Reports get pasted into English bug trackers, so numbers shouldn't pick up the PC's own separators.
    static string Bytes(long count) => count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " bytes";
}
