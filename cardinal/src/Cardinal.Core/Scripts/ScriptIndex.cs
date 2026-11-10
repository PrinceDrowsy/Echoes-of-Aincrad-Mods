namespace Cardinal.Scripts;

/// <summary>
/// One step of a script, or one line of a cutscene's dialogue (Id empty, TextKey set), as Find in scripts searches it.
/// Named is the sentence with every cast member named, kept only where it differs from Sentence.
/// </summary>
public sealed record IndexedStep(ScriptSource Source, int Number, string Id, string Kind, string Settings, string Sentence,
    string? Named, string? TextKey)
{
    public string Text(bool names) => names && Named is not null ? Named : Sentence;
}

/// <summary>
/// Every step of every script, read once, so a search over all of them is instant. Reading takes a while, since it
/// opens every script. Text is the game's own, without a project's text edits.
/// </summary>
public sealed class ScriptIndex
{
    public IReadOnlyList<IndexedStep> Steps { get; }

    ScriptIndex(IReadOnlyList<IndexedStep> steps) => Steps = steps;

    public static ScriptIndex Build(Game game, StepText text, IReadOnlyList<ScriptSource> sources)
    {
        var steps = new List<IndexedStep>();
        foreach (var source in sources)
        {
            var script = Script.Load(game, source);
            var plain = text.Describe(script, false);
            var named = text.Describe(script, true);
            foreach (var step in script.Steps)
            {
                var sentence = plain[step.Id];
                var withNames = named[step.Id].Text;
                steps.Add(new IndexedStep(source, step.Number, step.Id, Script.KindName(step.Kind), Script.Settings(step),
                    sentence.Text, withNames == sentence.Text ? null : withNames, sentence.TextKey));
            }
            foreach (var line in text.EventLines(script))
                steps.Add(new IndexedStep(source, 0, "", "", "", line.Text, null, line.Key));
        }
        return new ScriptIndex(steps);
    }

    /// <summary>The steps where every word of the query appears. With names off, names the story view hides can't be found either.</summary>
    public IEnumerable<IndexedStep> Find(string query, bool names)
    {
        var words = Words(query);
        return words.Length == 0 ? [] : Steps.Where(s => Matches(words, s.Text(names), s.Kind, s.Settings, s.Id, s.TextKey));
    }

    public static string[] Words(string query) => query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Whether every word appears in at least one of the fields, ignoring case.</summary>
    public static bool Matches(string[] words, params string?[] fields) =>
        words.All(word => fields.Any(f => f is not null && f.Contains(word, StringComparison.OrdinalIgnoreCase)));
}
