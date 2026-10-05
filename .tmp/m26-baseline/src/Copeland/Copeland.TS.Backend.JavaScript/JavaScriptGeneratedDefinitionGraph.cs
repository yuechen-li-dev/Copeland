namespace Copeland.TS.Backend.JavaScript;

/// <summary>
/// Small emitter-owned graph for compiler-generated top-level definitions.
/// It is deliberately not a JavaScript AST and does not inspect emitted text.
/// </summary>
internal sealed class JavaScriptGeneratedDefinitionGraph
{
    private readonly Dictionary<string, Definition> definitions = new(StringComparer.Ordinal);
    private string? currentDefinition;

    public string? CurrentDefinition => currentDefinition;

    public void Register(string stableId, string kind)
    {
        ArgumentException.ThrowIfNullOrEmpty(stableId);
        ArgumentException.ThrowIfNullOrEmpty(kind);
        if (!definitions.TryAdd(stableId, new Definition(stableId, kind)))
        {
            throw new InvalidOperationException($"Generated JavaScript definition '{stableId}' was registered more than once.");
        }
    }

    public void Begin(string stableId)
    {
        if (currentDefinition is not null)
        {
            throw new InvalidOperationException($"Generated JavaScript definition '{currentDefinition}' was not ended before '{stableId}' began.");
        }
        if (!definitions.ContainsKey(stableId))
        {
            throw new InvalidOperationException($"Generated JavaScript definition '{stableId}' was not registered.");
        }
        currentDefinition = stableId;
    }

    public void End(string stableId)
    {
        if (!string.Equals(currentDefinition, stableId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Generated JavaScript definition '{stableId}' does not own the active emission block.");
        }
        currentDefinition = null;
    }

    public void Reference(string stableId)
    {
        if (!definitions.ContainsKey(stableId))
        {
            throw new InvalidOperationException($"Generated JavaScript reference '{stableId}' was not registered.");
        }

        if (currentDefinition is null)
        {
            definitions[stableId].IsRoot = true;
            return;
        }

        definitions[currentDefinition].References.Add(stableId);
    }

    public IReadOnlySet<string> MarkReachable()
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(definitions.Values
            .Where(definition => definition.IsRoot)
            .Select(definition => definition.StableId)
            .OrderByDescending(stableId => stableId, StringComparer.Ordinal));

        while (pending.Count > 0)
        {
            string stableId = pending.Pop();
            if (!reachable.Add(stableId))
            {
                continue;
            }

            foreach (string reference in definitions[stableId].References.OrderByDescending(value => value, StringComparer.Ordinal))
            {
                if (!reachable.Contains(reference))
                {
                    pending.Push(reference);
                }
            }
        }

        return reachable;
    }

    public JavaScriptReachabilityReport CreateReport(
        bool enabled,
        IReadOnlySet<string> reachable,
        IReadOnlyDictionary<string, int> bytesByDefinition)
    {
        JavaScriptReachabilityDefinition[] items = definitions.Values
            .OrderBy(definition => definition.StableId, StringComparer.Ordinal)
            .Select(definition => new JavaScriptReachabilityDefinition(
                definition.StableId,
                definition.Kind,
                definition.IsRoot,
                reachable.Contains(definition.StableId),
                bytesByDefinition.GetValueOrDefault(definition.StableId),
                definition.References.OrderBy(value => value, StringComparer.Ordinal).ToArray()))
            .ToArray();
        JavaScriptReachabilityDefinition[] removed = items
            .Where(definition => !definition.IsReachable)
            .ToArray();
        return new JavaScriptReachabilityReport(
            enabled,
            items.Length,
            items.Length - removed.Length,
            enabled ? removed.Length : 0,
            enabled ? removed.Sum(definition => definition.EmittedBytes) : 0,
            items);
    }

    private sealed class Definition(string stableId, string kind)
    {
        public string StableId { get; } = stableId;
        public string Kind { get; } = kind;
        public bool IsRoot { get; set; }
        public HashSet<string> References { get; } = new(StringComparer.Ordinal);
    }
}
