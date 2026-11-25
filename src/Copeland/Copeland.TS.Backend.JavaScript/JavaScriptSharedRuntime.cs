namespace Copeland.TS.Backend.JavaScript;

internal sealed class JavaScriptSharedRuntime
{
    private static readonly string[] DeclarationPrefixes = ["function ", "function* ", "const ", "let "];
    private readonly HashSet<string> bindings = new(StringComparer.Ordinal);

    public JavaScriptSharedRuntime(string source)
    {
        // These lines come from the compiler's runtime writers. Their top-level
        // bindings start in column zero, including in the raw runtime templates.
        using var reader = new StringReader(source);
        while (reader.ReadLine() is { } line)
        {
            foreach (string prefix in DeclarationPrefixes)
            {
                if (line.StartsWith(prefix, StringComparison.Ordinal))
                {
                    int end = IdentifierEnd(line, prefix.Length);
                    bindings.Add(line[prefix.Length..end]);
                    break;
                }
            }
        }
        Source = bindings.Count == 0
            ? string.Empty
            : source + $"export {{ {string.Join(", ", bindings.Order(StringComparer.Ordinal))} }};\n";
    }

    public string Source { get; }

    public string GetImports(IEnumerable<string> moduleSource)
    {
        // Matching extra identifiers in a comment or literal only adds an
        // import. The runtime is already a static dependency of this module.
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        var lookup = bindings.GetAlternateLookup<ReadOnlySpan<char>>();
        foreach (string source in moduleSource)
        {
            for (int index = 0; index < source.Length;)
            {
                if (!IsIdentifierPart(source[index]))
                {
                    index++;
                    continue;
                }
                int end = IdentifierEnd(source, index);
                if (lookup.TryGetValue(source.AsSpan(index, end - index), out string? binding))
                {
                    referenced.Add(binding);
                }
                index = end;
            }
        }
        return string.Join(", ", referenced.Order(StringComparer.Ordinal));
    }

    private static int IdentifierEnd(string source, int start)
    {
        while (start < source.Length && IsIdentifierPart(source[start]))
        {
            start++;
        }
        return start;
    }

    private static bool IsIdentifierPart(char character)
        => char.IsAsciiLetterOrDigit(character) || character is '_' or '$';
}
