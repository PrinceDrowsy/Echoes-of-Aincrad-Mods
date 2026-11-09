using Cardinal.Tables;
using CUE4Parse.UE4.Assets.Exports;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cardinal.Scripts;

/// <summary>One of the game's Flow graphs: the scripts behind story events, quests and tutorials.</summary>
public sealed record ScriptSource(string Folder, string Name, string File)
{
    /// <summary>The /Game path step changes in a project refer to it by.</summary>
    public string Package => "/Game/" + File[Game.ContentRoot.Length..^".uasset".Length];
}

/// <summary>Where one of a step's outputs leads: the next step's id and the input it arrives at.</summary>
public sealed record ScriptLink(string Output, string To, string Input);

/// <summary>
/// A step of a script. Kind is its class, Id its NodeGuid, Fields its own settings, and Text the English for any text
/// keys among them. Number is its place in story order, counted from the start step. A step that isn't Reachable has
/// nothing in the script leading to it, so it never runs as the script stands.
/// </summary>
public sealed record ScriptStep(int Number, string Kind, string Id, JObject Fields, IReadOnlyList<ScriptLink> Links,
    IReadOnlyDictionary<string, string> Text, bool Reachable);

public sealed class Script
{
    public const string Folder = "ROD/Flow/FlowGraphs/";

    // Every step stores these; they're shown as the step's id and links instead of as settings.
    static readonly HashSet<string> Bookkeeping = ["GraphNode", "NodeGuid", "Connections"];
    static readonly JsonSerializer Json = JsonSerializer.CreateDefault();

    public ScriptSource Source { get; }
    public IReadOnlyList<ScriptStep> Steps { get; }

    Script(ScriptSource source, IReadOnlyList<ScriptStep> steps)
    {
        Source = source;
        Steps = steps;
    }

    public static IReadOnlyList<ScriptSource> List(Game game) =>
        game.FindFiles(Folder, ".uasset").Select(file =>
        {
            var inside = file[(Game.ContentRoot + Folder).Length..^".uasset".Length];
            var cut = inside.LastIndexOf('/');
            return new ScriptSource(cut < 0 ? "" : inside[..cut], inside[(cut + 1)..], file);
        }).ToList();

    public static Script Load(Game game, ScriptSource source)
    {
        var found = new List<(string Kind, string Id, JObject Fields, List<ScriptLink> Links)>();
        foreach (var export in game.Package(source.File).GetExports())
        {
            var properties = (JObject.FromObject(export, Json)["Properties"] as JObject) ?? [];
            if (properties["NodeGuid"]?.ToString() is not { Length: > 0 } id)
                continue;
            var links = (properties["Connections"] as JArray ?? [])
                .Select(c => new ScriptLink((string)c["Key"]!, (string)c["Value"]!["NodeGuid"]!, (string)c["Value"]!["PinName"]!))
                .ToList();
            var fields = new JObject(properties.Properties().Where(p => !Bookkeeping.Contains(p.Name)));
            found.Add((export.ExportType, id, fields, links));
        }

        // Story order: breadth first from the start steps along their links, then anything nothing leads to.
        var byId = found.ToDictionary(s => s.Id);
        var order = new List<string>();
        var queue = new Queue<string>(found.Where(s => s.Kind.EndsWith("_Start")).Select(s => s.Id));
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (order.Contains(id) || !byId.TryGetValue(id, out var step))
                continue;
            order.Add(id);
            foreach (var link in step.Links)
                queue.Enqueue(link.To);
        }
        var reachable = order.ToHashSet();
        order.AddRange(found.Select(s => s.Id).Where(id => !reachable.Contains(id)));

        var steps = order.Select((id, i) =>
        {
            var step = byId[id];
            return new ScriptStep(i + 1, step.Kind, id, step.Fields, step.Links, Table.FindText(game, step.Fields),
                reachable.Contains(id));
        }).ToList();
        return new Script(source, steps);
    }

    /// <summary>A step's kind as people say it: FlowNode_ShowTutorialWindow reads as ShowTutorialWindow.</summary>
    public static string KindName(string kind)
    {
        var name = kind.StartsWith("FlowNode_") ? kind["FlowNode_".Length..] : kind;
        return name.EndsWith("_C") ? name[..^2] + " (Blueprint)" : name;
    }

    /// <summary>A step's settings on one line, with the English after any text key.</summary>
    public static string Settings(ScriptStep step) =>
        string.Join("; ", step.Fields.Properties().Select(p =>
            $"{p.Name} {Table.Cell(p.Value)}" + (step.Text.TryGetValue(p.Name, out var english) ? $" ({english})" : "")));

    /// <summary>
    /// Where a step leads when only some steps are shown: each output followed through hidden steps to the shown ones it
    /// reaches, numbered as shown. Plain continuations read "then 7"; branches name their output, as in "if True: 7".
    /// </summary>
    public string Then(ScriptStep step, Func<ScriptStep, bool> shown, Func<ScriptStep, int> number)
    {
        var byId = Steps.ToDictionary(s => s.Id);
        var parts = new List<string>();
        foreach (var group in step.Links.GroupBy(l => l.Output))
        {
            var reached = new SortedSet<int>();
            var seen = new HashSet<string>();
            var queue = new Queue<string>(group.Select(l => l.To));
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                if (!seen.Add(id) || !byId.TryGetValue(id, out var next))
                    continue;
                if (shown(next))
                    reached.Add(number(next));
                else
                    foreach (var link in next.Links)
                        queue.Enqueue(link.To);
            }
            if (reached.Count == 0)
                continue;
            var numbers = string.Join(", ", reached);
            parts.Add(group.Key switch
            {
                "Out" or "Completed" or "Spawned" or "Finish" or "Finished" => $"then {numbers}",
                "Started" => $"as it starts: {numbers}",
                _ => $"if {group.Key}: {numbers}",
            });
        }
        return string.Join("; ", parts);
    }

    /// <summary>Where a step leads, by the numbers of the steps its outputs reach.</summary>
    public string Leads(ScriptStep step) =>
        string.Join("; ", step.Links.Select(l =>
            $"{l.Output} → {(Steps.FirstOrDefault(s => s.Id == l.To) is { } next ? $"{next.Number} {KindName(next.Kind)}" : l.To)}"));
}
