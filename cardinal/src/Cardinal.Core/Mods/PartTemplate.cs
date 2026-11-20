using System.Globalization;
using Cardinal.Tables;
using UAssetAPI.Unversioned;

namespace Cardinal.Mods;

/// <summary>
/// A new character creator part: its kind (pupil, eyebrow, eyeline, nose or jaw), the part it starts from, and its ID.
/// OwnMesh is set when the mod ships its own mesh under the part's name instead of a copy of From's.
/// </summary>
public sealed record NewPart(string Kind, int From, int Id, bool OwnMesh);

/// <summary>
/// The "new part" template. A creator option is a row in the kind's parts table and its thumbnail table, both holding
/// the ID, and a mesh named after the ID (SK_CHR_PUP030 for pupil 30) that's listed in the kind's mesh list. The game
/// finds the mesh by the number in its name among the listed meshes, so the list's order doesn't matter.
/// </summary>
public static class PartTemplate
{
    public sealed record PartKind(string Name, string Parts, string Thumbnails, string MeshList, string Folder, string Prefix)
    {
        public string Mesh(int id) => $"{Folder}{Prefix}{id.ToString("000", CultureInfo.InvariantCulture)}";
    }

    const string Lists = "/Game/ROD/DataAssets/AvatarParts/", Meshes = "/Game/ROD/CHR/Humans/Heads/";

    public static readonly IReadOnlyList<PartKind> Kinds =
    [
        new("pupil", "Avatar/PupilParts", "Other/DT_Pupil", Lists + "Heads/AvatarMesh_Pupil", Meshes + "PUP/", "SK_CHR_PUP"),
        new("eyebrow", "Avatar/EyebrowParts", "Other/DT_Eyebrow", Lists + "Heads/AvatarMesh_Eyebrows", Meshes + "EYB/", "SK_CHR_EYB"),
        new("eyeline", "Avatar/EyelineParts", "Other/DT_Eyeline", Lists + "Heads/AvatarMesh_Eyeline", Meshes + "EYE/", "SK_CHR_EYE"),
        new("nose", "Avatar/NoseParts", "Other/DT_Nose", Lists + "Heads/AvatarMesh_Nose", Meshes + "NOS/", "SK_CHR_NOS"),
        new("jaw", "Avatar/JawParts", "Other/DT_Jaw", Lists + "AvatarMesh_Head", Meshes + "JAW/", "SK_CHR_HJ"),
    ];

    public static PartKind Kind(string name) =>
        Kinds.FirstOrDefault(k => k.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new EditException($"{name} isn't a part kind. Part kinds: {string.Join(", ", Kinds.Select(k => k.Name))}.");

    /// <summary>
    /// The first ID after every one the kind's parts use, skipping numbers the game already has a mesh for, since the
    /// game ships meshes for parts it doesn't offer.
    /// </summary>
    public static int NextId(Game game, Table parts, ModProject project, PartKind kind)
    {
        var id = parts.Rows.Select(r => Number(r.Key))
            .Concat(project.AddedRows.Where(r => r.Table == kind.Parts).Select(r => Number(r.Key)))
            .DefaultIfEmpty(0).Max() + 1;
        while (game.Provider.Files.ContainsKey(AssetCopier.File(kind.Mesh(id))))
            id++;
        return id;
    }

    static int Number(string? text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    /// <summary>
    /// Adds the part to the project, after checking it against the unmodded game. Returns notes for the modder.
    /// Throws EditException when the part can't be added as given.
    /// </summary>
    public static IReadOnlyList<string> Add(Game game, Settings settings, ModProject project, IReadOnlyList<TableSource> sources, NewPart part)
    {
        var kind = Kind(part.Kind);
        var partsSource = sources.Single(s => s.Id == kind.Parts);
        var thumbnailSource = sources.Single(s => s.Id == kind.Thumbnails);
        var parts = partsSource.Load(game);
        var thumbnails = thumbnailSource.Load(game);
        var from = part.From.ToString(CultureInfo.InvariantCulture);
        var id = part.Id.ToString(CultureInfo.InvariantCulture);

        if (part.Id is <= 0 or > 999)
            throw new EditException("The part ID must be a whole number from 1 to 999.");
        if (parts.Rows.All(r => r.Key != from))
            throw new EditException($"The creator has no {kind.Name} {from} to start from.");
        if (parts.Rows.Any(r => r.Key == id) || project.FindRow(kind.Parts, id) is not null)
            throw new EditException($"{Capitalized(kind.Name)} {id} is already taken. The next free one is {NextId(game, parts, project, kind)}.");
        var mesh = kind.Mesh(part.Id);
        if (game.Provider.Files.ContainsKey(AssetCopier.File(mesh)))
            throw new EditException($"The game already has a mesh {mesh}, which the game would show for this ID. The next free one is {NextId(game, parts, project, kind)}.");
        // The game's thumbnail rows are keyed 0, 1, 2... with the part's ID in a field.
        var thumbnail = thumbnails.Rows.FirstOrDefault(r => r.Fields["ID"]?.ToString() == from)
                        ?? throw new EditException($"{kind.Thumbnails} has no thumbnail for {kind.Name} {from}.");
        var key = (thumbnails.Rows.Select(r => Number(r.Key))
            .Concat(project.AddedRows.Where(r => r.Table == kind.Thumbnails).Select(r => Number(r.Key)))
            .DefaultIfEmpty(-1).Max() + 1).ToString(CultureInfo.InvariantCulture);

        var rows = new[]
        {
            new RowAddition(kind.Parts, id, from, new Dictionary<string, string> { ["ID"] = id }),
            new RowAddition(kind.Thumbnails, key, thumbnail.Key, new Dictionary<string, string> { ["ID"] = id }),
        };
        var mappings = new Usmap(settings.MappingsFile!);
        foreach (var (row, source) in rows.Zip([partsSource, thumbnailSource]))
            if (EditableAsset.Load(settings, mappings, source.Target!.File).Check(source.Target, row) is { } problem)
                throw new EditException(problem);
        AssetCopy? copy = null;
        if (!part.OwnMesh)
        {
            copy = new AssetCopy(kind.Mesh(part.From), mesh);
            if (AssetCopier.Check(game, AssetRegistry.Read(game.Provider.SaveAsset(AssetRegistry.File)), copy) is { } problem)
                throw new EditException(problem);
        }

        foreach (var row in rows)
            project.SetRow(row);
        project.SetListItem(new ListItem(kind.MeshList, "AvatarMeshes", mesh));
        if (copy is not null)
            project.SetAsset(copy);

        return
        [
            part.OwnMesh
                ? $"The mod has to ship its own mesh as {mesh}, on the same skeleton as the game's {kind.Name}s; without it the part shows nothing."
                : $"Its mesh starts as a copy of {kind.Name} {from}'s, as {mesh}. To give it a mesh of its own, ship that mesh under the same name and add the part with --own-mesh instead.",
            $"Its thumbnail is {kind.Name} {from}'s until you change ThumbnailTexture in its {kind.Parts} row. Which of the two thumbnail tables the creator's tiles read hasn't been settled.",
            "Every part mod changes the same creator files, so two part mods only work together once merged.",
        ];
    }

    static string Capitalized(string text) => char.ToUpperInvariant(text[0]) + text[1..];
}
