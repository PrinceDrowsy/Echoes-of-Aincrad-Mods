using System.Globalization;
using System.Text.RegularExpressions;
using Cardinal.Scripts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cardinal.Mods;

/// <summary>Turns a project's step rules into the step changes they make in the game as it is now.</summary>
public static partial class NodeRules
{
    public sealed record Expanded(NodeRule Rule, NodeChange Change);

    /// <summary>
    /// A change for every step each rule matches, problems with steps a rule can't change, and the rules that match
    /// nothing. A step the project changes on its own is left to that change, and a step two rules match goes to the
    /// first, as listed.
    /// </summary>
    public static (List<Expanded> Changes, List<(NodeRule Rule, string Problem)> Problems, List<NodeRule> Unmatched) Expand(
        Game game, ModProject project)
    {
        var changes = new List<Expanded>();
        var problems = new List<(NodeRule, string)>();
        if (project.NodeRules.Count == 0)
            return (changes, problems, []);
        var taken = project.NodeChanges.Select(c => (c.Asset.ToLowerInvariant(), c.Node)).ToHashSet();
        var matched = new HashSet<NodeRule>();
        foreach (var source in Script.List(game))
        {
            var rules = project.NodeRules.Where(r => r.Folder is not { Length: > 0 } folder
                                                     || source.Folder.StartsWith(folder, StringComparison.OrdinalIgnoreCase)).ToList();
            if (rules.Count == 0)
                continue;
            foreach (var step in Script.Load(game, source).Steps)
            {
                if (rules.FirstOrDefault(r => r.Match == step.Kind && (r.Where ?? []).All(w => Same(Value(step.Fields, w.Key), w.Value)))
                    is not { } rule || !taken.Add((source.Package.ToLowerInvariant(), step.Id)))
                    continue;
                matched.Add(rule);
                var fields = rule.Fields.ToDictionary(f => f.Key, f => Copied(f.Value) is { } path ? Value(step.Fields, path) : f.Value);
                if (fields.FirstOrDefault(f => f.Value is null).Key is { } missing)
                    problems.Add((rule, $"step {step.Id} in {source.Package} has no {Copied(rule.Fields[missing])} to copy into {missing}."));
                else
                    changes.Add(new Expanded(rule, new NodeChange(source.Package, step.Id, rule.Kind, fields!, rule.Outputs)));
            }
        }
        return (changes, problems, project.NodeRules.Where(r => !matched.Contains(r)).ToList());
    }

    // A field left at its default isn't stored, and a rule writes that as an empty value.
    static bool Same(string? value, string wanted) =>
        (value ?? "").Equals(wanted, StringComparison.OrdinalIgnoreCase)
        || value is not null && value.EndsWith("::" + wanted, StringComparison.OrdinalIgnoreCase);

    static string? Value(JObject fields, string path) => fields.SelectToken(path) switch
    {
        null => null,
        JValue { Type: JTokenType.Boolean } b => (bool)b! ? "true" : "false",
        JValue v => Convert.ToString(v.Value, CultureInfo.InvariantCulture),
        var other => other.ToString(Formatting.None),
    };

    static string? Copied(string value) => CopyPattern().Match(value) is { Success: true } m ? m.Groups[1].Value : null;

    [GeneratedRegex(@"^\{([^{}]+)\}$")]
    private static partial Regex CopyPattern();
}
