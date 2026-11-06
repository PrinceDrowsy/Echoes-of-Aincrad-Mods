using System.Text;

namespace Cardinal.Mods;

/// <summary>
/// The game's AssetRegistry.bin, the index a cooked game finds its primary assets (towns, worlds, quests) in, since it
/// never scans its folders. Only the layout this game ships is read: version 17 with the text-first tag store. The parts
/// an addition touches are read into lists and the rest is kept as stored, so an unchanged file writes back byte for byte.
/// </summary>
public sealed class AssetRegistry
{
    public const string File = "EchoesofAincrad/AssetRegistry.bin";

    const int SupportedVersion = 17;
    const uint TextFirstMagic = 0x12345679u, EndMagic = 0x87654321u, NumberedBit = 0x80000000u;
    // A value ID keeps its type in the low 3 bits; 2 is a numberless name.
    const uint NumberlessNameValue = 2;

    readonly record struct Name(uint Index, uint Number);

    sealed record Asset(Name PackagePath, Name ClassPackage, Name ClassName, Name PackageName, Name AssetName, ulong TagMap, byte[] Rest);

    byte[] header = [];
    ulong hashVersion;
    readonly List<string> names = [];
    readonly List<bool> wide = [];
    readonly List<ulong> hashes = [];
    readonly Dictionary<string, int> nameIndex = [];

    uint storeMagic;
    readonly int[] counts = new int[11];
    byte[] texts = [];
    readonly List<uint> numberlessNames = [];
    byte[] namesToStrings = [];
    readonly List<(uint Key, uint Value)> numberlessPairs = [];
    byte[] numberedPairs = [];

    readonly List<Asset> assets = [];
    readonly Dictionary<string, int> assetIndex = [];
    byte[] tail = [];

    public int Count => assets.Count;

    public bool Contains(string package) => assetIndex.ContainsKey(package);

    /// <summary>
    /// Adds an asset entry for package (such as /Game/ROD/DataAssets/Town/Town_011) copied from an existing one, so it
    /// keeps that asset's class, tags and chunk, with its own name as its PrimaryAssetName.
    /// </summary>
    public void AddCopy(string from, string package)
    {
        if (!assetIndex.TryGetValue(from, out var index))
            throw new EditException($"The game has no asset {from} to copy.");
        if (Contains(package))
            throw new EditException($"The game already has an asset {package}.");
        var source = assets[index];
        // Every map this game ships has numberless keys: the top bit of the handle, then the pair count and first pair.
        if (source.TagMap >> 63 == 0)
            throw new EditException($"{from} keeps its tags in a way Cardinal can't copy yet.");
        var count = (int)((source.TagMap >> 32) & 0xFFFF);
        var begin = (int)(uint)source.TagMap;

        var assetName = Add(package[(package.LastIndexOf('/') + 1)..]);
        var pairs = numberlessPairs.GetRange(begin, count).Select(pair =>
        {
            if (names[(int)pair.Key] != "PrimaryAssetName")
                return pair;
            if ((pair.Value & 7) != NumberlessNameValue)
                throw new EditException($"{from}'s PrimaryAssetName isn't stored as a name.");
            numberlessNames.Add(assetName.Index);
            return (pair.Key, ((uint)(numberlessNames.Count - 1) << 3) | NumberlessNameValue);
        }).ToList();
        var first = numberlessPairs.Count;
        numberlessPairs.AddRange(pairs);

        assetIndex[package] = assets.Count;
        assets.Add(source with
        {
            PackageName = Add(package),
            AssetName = assetName,
            TagMap = (1UL << 63) | ((ulong)count << 32) | (uint)first,
        });
    }

    Name Add(string text)
    {
        if (!nameIndex.TryGetValue(text, out var index))
        {
            nameIndex[text] = index = names.Count;
            names.Add(text);
            wide.Add(text.Any(c => c > 0x7F));
            hashes.Add(NameHash(text));
        }
        return new Name((uint)index, 0);
    }

    // The engine's FNameHash: CityHash64 of the name with only ASCII lower-cased, as single bytes or UTF-16 like the name.
    internal static ulong NameHash(string text)
    {
        var lower = string.Create(text.Length, text, (span, s) =>
        {
            for (var i = 0; i < s.Length; i++)
                span[i] = s[i] < 0x80 ? char.ToLowerInvariant(s[i]) : s[i];
        });
        return CUE4Parse.Utils.CityHash.CityHash64(lower.Any(c => c > 0x7F) ? Encoding.Unicode.GetBytes(lower) : Encoding.Latin1.GetBytes(lower));
    }

    internal bool HashesMatch() => names.Select((n, i) => NameHash(n) == hashes[i]).All(x => x);

    public static AssetRegistry Read(byte[] data)
    {
        var r = new AssetRegistry();
        using var reader = new BinaryReader(new MemoryStream(data));
        // A version GUID, the version, and bFilterEditorOnlyData stored as 4 bytes.
        r.header = reader.ReadBytes(24);
        var version = BitConverter.ToInt32(r.header, 16);
        if (version != SupportedVersion)
            throw new InvalidDataException($"This AssetRegistry.bin is version {version}; Cardinal reads version {SupportedVersion}.");

        var nameCount = reader.ReadInt32();
        reader.ReadUInt32(); // total string bytes, recounted on write
        r.hashVersion = reader.ReadUInt64();
        for (var i = 0; i < nameCount; i++)
            r.hashes.Add(reader.ReadUInt64());
        var lengths = new int[nameCount];
        for (var i = 0; i < nameCount; i++)
        {
            var high = reader.ReadByte();
            r.wide.Add((high & 0x80) != 0);
            lengths[i] = ((high & 0x7F) << 8) | reader.ReadByte();
        }
        for (var i = 0; i < nameCount; i++)
        {
            var text = r.wide[i] ? Encoding.Unicode.GetString(reader.ReadBytes(lengths[i] * 2)) : Encoding.Latin1.GetString(reader.ReadBytes(lengths[i]));
            r.names.Add(text);
            r.nameIndex.TryAdd(text, i);
        }

        r.storeMagic = reader.ReadUInt32();
        if (r.storeMagic != TextFirstMagic)
            throw new InvalidDataException("This AssetRegistry.bin's tag store isn't in the layout Cardinal reads.");
        for (var i = 0; i < r.counts.Length; i++)
            r.counts[i] = reader.ReadInt32();
        var start = reader.BaseStream.Position;
        reader.ReadUInt32(); // the texts' byte size
        for (var i = 0; i < r.counts[4]; i++)
            SkipString(reader);
        r.texts = data[(int)start..(int)reader.BaseStream.Position];
        for (var i = 0; i < r.counts[0]; i++)
            r.numberlessNames.Add(reader.ReadUInt32());
        start = reader.BaseStream.Position;
        for (var i = 0; i < r.counts[1]; i++)
            SkipName(reader);
        reader.BaseStream.Position += r.counts[2] * 16L; // numberless export paths: four name indices each
        for (var i = 0; i < r.counts[3] * 4; i++)
            SkipName(reader); // export paths: class package, class, object, package
        reader.BaseStream.Position += r.counts[5] * 4L + r.counts[6] * 4L + r.counts[7] + r.counts[8] * 2L;
        r.namesToStrings = data[(int)start..(int)reader.BaseStream.Position];
        for (var i = 0; i < r.counts[9]; i++)
            r.numberlessPairs.Add((reader.ReadUInt32(), reader.ReadUInt32()));
        start = reader.BaseStream.Position;
        for (var i = 0; i < r.counts[10]; i++)
        {
            SkipName(reader);
            reader.ReadUInt32();
        }
        r.numberedPairs = data[(int)start..(int)reader.BaseStream.Position];
        if (reader.ReadUInt32() != EndMagic)
            throw new InvalidDataException("This AssetRegistry.bin's tag store doesn't end where expected.");

        var assetCount = reader.ReadInt32();
        for (var i = 0; i < assetCount; i++)
        {
            var path = ReadName(reader);
            var classPackage = ReadName(reader);
            var className = ReadName(reader);
            var package = ReadName(reader);
            var asset = ReadName(reader);
            var tags = reader.ReadUInt64();
            start = reader.BaseStream.Position;
            var bundles = reader.ReadInt32();
            for (var b = 0; b < bundles; b++)
            {
                SkipName(reader);
                var paths = reader.ReadInt32();
                for (var p = 0; p < paths; p++)
                {
                    SkipName(reader);
                    SkipName(reader);
                    SkipString(reader);
                }
            }
            var chunks = reader.ReadInt32();
            reader.BaseStream.Position += chunks * 4L + 4; // chunk IDs, then package flags
            r.assetIndex.TryAdd(r.Text(package), r.assets.Count);
            r.assets.Add(new Asset(path, classPackage, className, package, asset, tags, data[(int)start..(int)reader.BaseStream.Position]));
        }
        // The dependency section and package data: both empty in this game's file, and nothing an addition needs.
        r.tail = data[(int)reader.BaseStream.Position..];
        return r;
    }

    public byte[] Write()
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write(header);
        w.Write(names.Count);
        w.Write((uint)names.Select((n, i) => wide[i] ? n.Length * 2 : n.Length).Sum());
        w.Write(hashVersion);
        hashes.ForEach(w.Write);
        for (var i = 0; i < names.Count; i++)
        {
            w.Write((byte)((wide[i] ? 0x80 : 0) | (names[i].Length >> 8)));
            w.Write((byte)names[i].Length);
        }
        for (var i = 0; i < names.Count; i++)
            w.Write(wide[i] ? Encoding.Unicode.GetBytes(names[i]) : Encoding.Latin1.GetBytes(names[i]));

        w.Write(storeMagic);
        counts[0] = numberlessNames.Count;
        counts[9] = numberlessPairs.Count;
        foreach (var count in counts)
            w.Write(count);
        w.Write(texts);
        numberlessNames.ForEach(w.Write);
        w.Write(namesToStrings);
        foreach (var (key, value) in numberlessPairs)
        {
            w.Write(key);
            w.Write(value);
        }
        w.Write(numberedPairs);
        w.Write(EndMagic);

        w.Write(assets.Count);
        foreach (var a in assets)
        {
            foreach (var name in new[] { a.PackagePath, a.ClassPackage, a.ClassName, a.PackageName, a.AssetName })
                WriteName(w, name);
            w.Write(a.TagMap);
            w.Write(a.Rest);
        }
        w.Write(tail);
        return stream.ToArray();
    }

    string Text(Name name) => name.Number == 0 ? names[(int)name.Index] : $"{names[(int)name.Index]}_{name.Number - 1}";

    static Name ReadName(BinaryReader reader)
    {
        var index = reader.ReadUInt32();
        return (index & NumberedBit) != 0 ? new Name(index & ~NumberedBit, reader.ReadUInt32()) : new Name(index, 0);
    }

    static void WriteName(BinaryWriter writer, Name name)
    {
        if (name.Number == 0)
            writer.Write(name.Index);
        else
        {
            writer.Write(name.Index | NumberedBit);
            writer.Write(name.Number);
        }
    }

    static void SkipName(BinaryReader reader)
    {
        if ((reader.ReadUInt32() & NumberedBit) != 0)
            reader.ReadUInt32();
    }

    // An FString: a positive length counts single bytes, a negative one UTF-16 characters.
    static void SkipString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        reader.BaseStream.Position += length < 0 ? -length * 2L : length;
    }
}
