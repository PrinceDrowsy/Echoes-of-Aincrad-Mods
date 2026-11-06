using System.Text;
using Cardinal.Mods;

namespace Cardinal.Text;

/// <summary>What importing a translation did: lines changed, and lines that need a look.</summary>
public sealed record ImportResult(int Changed, int Unchanged, IReadOnlyList<string> Missing, IReadOnlyList<string> EnglishChanged);

/// <summary>Changing the game's text in a mod project, and the CSV round trip translators work with.</summary>
public static class TextEditing
{
    // Column names are documented in the README; translators' tools and scripts rely on them.
    static readonly string[] CsvColumns = ["Namespace", "Key", "English", "Text"];

    /// <summary>
    /// Sets one line in the project, or removes the project's change when the text matches the unmodded game again.
    /// Returns the game's original text. Throws when the language has no such line.
    /// </summary>
    public static string Set(ModProject project, Locres unmodded, string culture, string ns, string key, string text)
    {
        var original = unmodded.Get(ns, key)
                       ?? throw new EditException($"{culture} has no line {ns} / {key}.");
        if (text == original)
            project.RemoveText(culture, ns, key);
        else
            project.SetText(new TextEdit(culture, ns, key, text, original));
        return original;
    }

    /// <summary>
    /// Sets one language's line for text the project adds. Empty text in a language other than English removes that
    /// language's line, so it shows the English again. Returns false when the project doesn't add that key.
    /// </summary>
    public static bool SetAdded(ModProject project, string culture, string ns, string key, string text)
    {
        if (project.FindAddedText(ns, key) is not { } added)
            return false;
        var lines = new Dictionary<string, string>(added.Text);
        if (culture == GameText.English && string.IsNullOrWhiteSpace(text))
            throw new EditException("Text the mod adds needs English: every language without its own line shows it.");
        if (string.IsNullOrWhiteSpace(text))
            lines.Remove(culture);
        else
            lines[culture] = text;
        project.SetAddedText(added with { Text = lines });
        return true;
    }

    /// <summary>The line as the project shows it: its edit if it has one, otherwise the unmodded game's text.</summary>
    public static string Current(ModProject project, string culture, TextLine line) =>
        project.FindText(culture, line.Namespace, line.Key)?.Value ?? line.Text;

    /// <summary>
    /// Writes every line of a language next to its English, with the project's edits already applied, so a
    /// translator can pick up where the project left off.
    /// </summary>
    public static void ExportCsv(ModProject project, Locres english, Locres language, string culture, string path)
    {
        var rows = language.Lines
            .Select(line => new[] { line.Namespace, line.Key, english.Get(line.Namespace, line.Key) ?? "", Current(project, culture, line) })
            .ToList();
        Export.WriteCsv(path, CsvColumns, rows);
    }

    /// <summary>
    /// Reads a CSV written by ExportCsv back into the project. Lines the game doesn't have are skipped and listed,
    /// and lines whose English has changed since the export are imported but listed, since the translation may be stale.
    /// </summary>
    public static ImportResult ImportCsv(ModProject project, Locres english, Locres language, string culture, string path)
    {
        var table = ReadCsv(File.ReadAllText(path, Encoding.UTF8));
        if (table.Count == 0 || !table[0].Take(CsvColumns.Length).SequenceEqual(CsvColumns, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"{Path.GetFileName(path)} doesn't start with the columns {string.Join(", ", CsvColumns)}. Export a language first to get the layout.");

        int changed = 0, unchanged = 0;
        var missing = new List<string>();
        var stale = new List<string>();
        foreach (var row in table.Skip(1).Where(r => r.Count >= CsvColumns.Length))
        {
            var (ns, key, exportedEnglish, text) = (row[0], row[1], row[2], row[3]);
            if (language.Get(ns, key) is null)
            {
                missing.Add($"{ns} / {key}");
                continue;
            }
            if ((english.Get(ns, key) ?? "") != exportedEnglish)
                stale.Add($"{ns} / {key}");
            var before = project.FindText(culture, ns, key)?.Value ?? language.Get(ns, key);
            Set(project, language, culture, ns, key, text);
            if (before == text)
                unchanged++;
            else
                changed++;
        }
        return new ImportResult(changed, unchanged, missing, stale);
    }

    // RFC 4180: commas separate fields, quotes wrap fields holding commas, quotes or line breaks, and "" is a quote.
    static List<List<string>> ReadCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c != '"')
                    field.Append(c);
                else if (i + 1 < text.Length && text[i + 1] == '"')
                    field.Append(text[++i]);
                else
                    quoted = false;
            }
            else if (c == '"')
                quoted = true;
            else if (c == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = [];
            }
            else
                field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        return rows;
    }
}
