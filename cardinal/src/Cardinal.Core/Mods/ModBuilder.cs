using Cardinal.Tables;
using UAssetAPI.Unversioned;

namespace Cardinal.Mods;

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

        var sources = TableCatalog.Discover(game).ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        var mappings = new Usmap(settings.MappingsFile!);
        var assets = new Dictionary<string, EditableAsset>();
        var problems = new List<string>();
        var plan = new List<(EditableAsset Asset, EditTarget Target, ModEdit Edit)>();

        foreach (var edit in project.Edits)
        {
            if (!sources.TryGetValue(edit.Table, out var source))
            {
                problems.Add($"{edit.Table}: no such table.");
                continue;
            }
            if (source.Target is not { } target)
            {
                problems.Add($"{edit.Table}: this table is read-only.");
                continue;
            }
            if (!assets.TryGetValue(target.File, out var asset))
                assets[target.File] = asset = EditableAsset.Load(settings, mappings, target.File);
            if (asset.Check(target, edit) is { } problem)
                problems.Add($"{edit.Table} / {edit.Row} / {edit.Field}: {problem}");
            else
                plan.Add((asset, target, edit));
        }
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
