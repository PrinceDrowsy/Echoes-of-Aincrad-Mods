using System.Text;
using Cardinal.Text;

namespace Cardinal.Tests;

public class LocresTests
{
    /// <summary>
    /// A version 3 .locres laid out the way the engine writes one: each distinct text stored once, in first-use order,
    /// with its reference count. The hashes are placeholders, since Locres keeps them as stored.
    /// </summary>
    internal static byte[] File(params (string Ns, string Key, string Text)[] lines)
    {
        byte[] magic = [0x0E, 0x14, 0x74, 0x75, 0x67, 0x4A, 0x03, 0xFC, 0x4A, 0x15, 0x90, 0x9D, 0xC3, 0x37, 0x7F, 0x1B];
        var texts = lines.Select(l => l.Text).Distinct().ToList();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(magic);
        writer.Write((byte)3);
        writer.Write(0L);
        writer.Write((uint)lines.Length);
        var spaces = lines.GroupBy(l => l.Ns).ToList();
        writer.Write((uint)spaces.Count);
        foreach (var space in spaces)
        {
            writer.Write(1u);
            String(writer, space.Key);
            writer.Write((uint)space.Count());
            foreach (var line in space)
            {
                writer.Write(2u);
                String(writer, line.Key);
                writer.Write(3u);
                writer.Write(texts.IndexOf(line.Text));
            }
        }
        var stringsAt = stream.Position;
        writer.Write(texts.Count);
        foreach (var text in texts)
        {
            String(writer, text);
            writer.Write(lines.Count(l => l.Text == text));
        }
        stream.Position = magic.Length + 1;
        writer.Write(stringsAt);
        return stream.ToArray();
    }

    static void String(BinaryWriter writer, string text)
    {
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

    static readonly byte[] Sample = File(("ST", "A", "Sword"), ("ST", "B", "Sword"), ("ST", "C", "剣"), ("Other", "A", "Shield"));

    [Fact]
    public void UnchangedFileWritesBackByteForByte() =>
        Assert.Equal(Sample, Locres.Read(Sample).Write());

    [Fact]
    public void ReadsEveryLine()
    {
        var file = Locres.Read(Sample);
        Assert.Equal(4, file.Count);
        Assert.Equal("剣", file.Get("ST", "C"));
        Assert.Equal("Shield", file.Get("Other", "A"));
        Assert.Null(file.Get("ST", "Missing"));
    }

    [Fact]
    public void ChangingSharedTextLeavesTheOtherLine()
    {
        var file = Locres.Read(Sample);
        Assert.True(file.Set("ST", "A", "Dagger"));
        var again = Locres.Read(file.Write());
        Assert.Equal("Dagger", again.Get("ST", "A"));
        Assert.Equal("Sword", again.Get("ST", "B"));
    }

    [Fact]
    public void TextNoLineUsesIsDropped()
    {
        var file = Locres.Read(File(("ST", "A", "Old"), ("ST", "B", "Kept")));
        file.Set("ST", "A", "Kept");
        Assert.Equal(File(("ST", "A", "Kept"), ("ST", "B", "Kept")), file.Write());
    }

    [Fact]
    public void SettingAMissingLineFails() =>
        Assert.False(Locres.Read(Sample).Set("ST", "Missing", "x"));

    [Fact]
    public void AddsLinesAndNamespaces()
    {
        var file = Locres.Read(Sample);
        file.Add("ST", "New", "新しい", "New");
        file.Add("Fresh", "Key", "Line", "Line");
        file.Add("ST", "A", "Changed", "ignored");
        var again = Locres.Read(file.Write());
        Assert.Equal(6, again.Count);
        Assert.Equal("新しい", again.Get("ST", "New"));
        Assert.Equal("Line", again.Get("Fresh", "Key"));
        Assert.Equal("Changed", again.Get("ST", "A"));
    }

    [Fact]
    public void RejectsOtherFiles()
    {
        Assert.Throws<InvalidDataException>(() => Locres.Read(new byte[32]));
        var old = (byte[])Sample.Clone();
        old[16] = 2;
        Assert.Throws<InvalidDataException>(() => Locres.Read(old));
    }
}
