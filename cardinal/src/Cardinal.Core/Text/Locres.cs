using System.Text;

namespace Cardinal.Text;

/// <summary>One translated line: the namespace and key that identify it, and its text in this language.</summary>
public sealed record TextLine(string Namespace, string Key, string Text);

/// <summary>
/// A .locres file: every line of one language, keyed by namespace and key. Only the current format (version 3,
/// used since UE 4.22) is supported. The key and source hashes are kept exactly as stored rather than recomputed,
/// so a file with no changes writes back byte for byte, and changing a line's text never needs a new hash.
/// </summary>
public sealed class Locres
{
    static readonly byte[] Magic = [0x0E, 0x14, 0x74, 0x75, 0x67, 0x4A, 0x03, 0xFC, 0x4A, 0x15, 0x90, 0x9D, 0xC3, 0x37, 0x7F, 0x1B];
    const byte SupportedVersion = 3;

    sealed class Entry(uint keyHash, string key, uint sourceHash, int index)
    {
        public uint KeyHash { get; } = keyHash;
        public string Key { get; } = key;
        public uint SourceHash { get; } = sourceHash;
        public int Index { get; set; } = index;
    }

    sealed record Namespace(uint Hash, string Name, List<Entry> Entries);

    readonly List<Namespace> namespaces;
    readonly List<string> strings;
    readonly List<int> refs;
    readonly Dictionary<(string, string), Entry> byKey = [];

    Locres(List<Namespace> namespaces, List<string> strings, List<int> refs)
    {
        this.namespaces = namespaces;
        this.strings = strings;
        this.refs = refs;
        foreach (var ns in namespaces)
            foreach (var entry in ns.Entries)
                byKey[(ns.Name, entry.Key)] = entry;
    }

    public int Count => byKey.Count;

    public IEnumerable<TextLine> Lines =>
        namespaces.SelectMany(ns => ns.Entries.Select(e => new TextLine(ns.Name, e.Key, strings[e.Index])));

    public string? Get(string ns, string key) => byKey.TryGetValue((ns, key), out var entry) ? strings[entry.Index] : null;

    /// <summary>Changes an existing line. Returns false when the language has no line with that namespace and key.</summary>
    public bool Set(string ns, string key, string text)
    {
        if (!byKey.TryGetValue((ns, key), out var entry))
            return false;
        if (strings[entry.Index] == text)
            return true;
        refs[entry.Index]--;
        // Identical text is stored once and shared, as the engine writes it.
        var index = strings.IndexOf(text);
        if (index < 0)
        {
            index = strings.Count;
            strings.Add(text);
            refs.Add(0);
        }
        refs[index]++;
        entry.Index = index;
        return true;
    }

    /// <summary>
    /// Adds a line the language doesn't have yet, or changes it if it does. Source is the text the line translates (the
    /// string table's English): the engine skips a translation whose stored source hash doesn't match it.
    /// </summary>
    public void Add(string ns, string key, string text, string source)
    {
        if (Set(ns, key, text))
            return;
        var space = namespaces.FirstOrDefault(n => n.Name == ns);
        if (space is null)
            namespaces.Add(space = new Namespace(KeyHash(ns), ns, []));
        var index = strings.IndexOf(text);
        if (index < 0)
        {
            index = strings.Count;
            strings.Add(text);
            refs.Add(0);
        }
        refs[index]++;
        var entry = new Entry(KeyHash(key), key, SourceHash(source), index);
        space.Entries.Add(entry);
        byKey[(ns, key)] = entry;
    }

    // Namespaces and keys are hashed as UTF-16 with CityHash64, folded to 32 bits the way the engine's GetTypeHash does.
    static uint KeyHash(string text)
    {
        var hash = CUE4Parse.Utils.CityHash.CityHash64(Encoding.Unicode.GetBytes(text));
        return (uint)hash + (uint)(hash >> 32) * 23;
    }

    static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(i =>
    {
        var c = (uint)i;
        for (var bit = 0; bit < 8; bit++)
            c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    // The engine's FCrc::StrCrc32, which feeds every character in as four bytes.
    static uint SourceHash(string text)
    {
        var crc = ~0u;
        foreach (var c in text)
        {
            uint value = c;
            for (var i = 0; i < 4; i++)
            {
                crc = (crc >> 8) ^ CrcTable[(crc ^ value) & 0xFF];
                value >>= 8;
            }
        }
        return ~crc;
    }

    public static Locres Read(byte[] data)
    {
        using var reader = new BinaryReader(new MemoryStream(data), Encoding.Unicode);
        if (!reader.ReadBytes(Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("This isn't a .locres file Cardinal can read (no version header).");
        var version = reader.ReadByte();
        if (version != SupportedVersion)
            throw new InvalidDataException($"This .locres file is version {version}; Cardinal reads version {SupportedVersion}.");

        var stringsAt = reader.ReadInt64();
        var body = reader.BaseStream.Position;
        reader.BaseStream.Position = stringsAt;
        var count = reader.ReadInt32();
        var strings = new List<string>(count);
        var refs = new List<int>(count);
        for (var i = 0; i < count; i++)
        {
            strings.Add(ReadString(reader));
            refs.Add(reader.ReadInt32());
        }
        reader.BaseStream.Position = body;

        reader.ReadUInt32(); // total entries, recounted on write
        var namespaces = new List<Namespace>();
        var namespaceCount = reader.ReadUInt32();
        for (var n = 0; n < namespaceCount; n++)
        {
            var ns = new Namespace(reader.ReadUInt32(), ReadString(reader), []);
            var keys = reader.ReadUInt32();
            for (var k = 0; k < keys; k++)
                ns.Entries.Add(new Entry(reader.ReadUInt32(), ReadString(reader), reader.ReadUInt32(), reader.ReadInt32()));
            namespaces.Add(ns);
        }
        return new Locres(namespaces, strings, refs);
    }

    public byte[] Write()
    {
        // Text that no line uses any more is dropped, and the rest keep their order.
        var used = byKey.Values.Select(e => e.Index).ToHashSet();
        var kept = Enumerable.Range(0, strings.Count).Where(i => refs[i] > 0 || used.Contains(i)).ToList();
        var remap = new Dictionary<int, int>();
        for (var i = 0; i < kept.Count; i++)
            remap[kept[i]] = i;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode);
        writer.Write(Magic);
        writer.Write(SupportedVersion);
        var offsetAt = stream.Position;
        writer.Write(0L);
        writer.Write((uint)byKey.Count);
        writer.Write((uint)namespaces.Count);
        foreach (var ns in namespaces)
        {
            writer.Write(ns.Hash);
            WriteString(writer, ns.Name);
            writer.Write((uint)ns.Entries.Count);
            foreach (var entry in ns.Entries)
            {
                writer.Write(entry.KeyHash);
                WriteString(writer, entry.Key);
                writer.Write(entry.SourceHash);
                writer.Write(remap[entry.Index]);
            }
        }

        var stringsAt = stream.Position;
        writer.Write(kept.Count);
        foreach (var i in kept)
        {
            WriteString(writer, strings[i]);
            writer.Write(refs[i]);
        }
        stream.Position = offsetAt;
        writer.Write(stringsAt);
        return stream.ToArray();
    }

    // Unreal's FString: a positive length means one byte per character, negative means UTF-16; both count a closing zero.
    static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length == 0)
            return "";
        if (length > 0)
            return Encoding.Latin1.GetString(reader.ReadBytes(length), 0, length - 1);
        var bytes = reader.ReadBytes(-length * 2);
        return Encoding.Unicode.GetString(bytes, 0, bytes.Length - 2);
    }

    // Unreal writes text as single bytes when every character is plain ASCII, and as UTF-16 otherwise.
    static void WriteString(BinaryWriter writer, string text)
    {
        if (text.Length == 0)
        {
            writer.Write(0);
            return;
        }
        if (text.All(c => c <= 0x7F))
        {
            writer.Write(text.Length + 1);
            writer.Write(Encoding.ASCII.GetBytes(text));
            writer.Write((byte)0);
        }
        else
        {
            writer.Write(-(text.Length + 1));
            writer.Write(Encoding.Unicode.GetBytes(text));
            writer.Write((short)0);
        }
    }
}
