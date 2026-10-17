using System.Text.RegularExpressions;

namespace Copeland.TS.Workspace;

public sealed record SourceProfileDiagnostic(string Code, string Message, int Position, int Length);

public sealed record SourceFileProfile(
    string? Name,
    int Start,
    int End,
    IReadOnlyList<SourceProfileDiagnostic> Diagnostics)
{
    public string? Owner => Name switch
    {
        null => null,
        "Legacy" => "tsc",
        _ => "tscl",
    };

    public string Mask(string source)
    {
        if (End == 0)
        {
            return source;
        }
        char[] result = source.ToCharArray();
        int prefix = source.Length > 0 && source[0] == '\uFEFF' ? 1 : 0;
        if (prefix == 1)
        {
            result[0] = ' ';
        }
        if (source.AsSpan(prefix).StartsWith("#!"))
        {
            while (prefix < source.Length && source[prefix] is not '\r' and not '\n')
            {
                result[prefix++] = ' ';
            }
        }
        for (int index = Start; index < End; index += 1)
        {
            if (result[index] is not '\r' and not '\n')
            {
                result[index] = ' ';
            }
        }
        return new string(result);
    }

    public SourceProfileDiagnostic? CheckOwner(string? owner)
    {
        if (owner is null || Owner is null || owner == Owner)
        {
            return null;
        }
        return new("COPE-PROFILE-0003", $"Profile '{Name}' requires {Owner}, but project ownership selects {owner}. Change the profile or the project ownership rule.", Start, Math.Max(1, End - Start));
    }

    public SourceProfileDiagnostic? CheckTarget(string? backend, string? runtime = null)
    {
        bool incompatible = Name switch
        {
            "Legacy" => backend is not null,
            "JavaScript" => backend is not null && backend != "javascript",
            "Dotnet" => backend is not null && backend != "csharp",
            "Dotnet-NativeAOT" => (backend is not null && backend != "csharp")
                || (runtime is not null && runtime != "nativeaot"),
            _ => false,
        };
        if (!incompatible)
        {
            return null;
        }
        return new("COPE-PROFILE-0004", $"Profile '{Name}' cannot compile for backend '{backend}', runtime '{runtime ?? "unspecified"}'. Legacy sources must be delegated to tsc; Copeland sources must match the selected target.", Start, Math.Max(1, End - Start));
    }
}

/// <summary>
/// Reads only the file prologue, without interpreting either language's body.
/// The reserved alias must precede imports and ordinary declarations.
/// </summary>
public static class SourceFileProfiles
{
    private static readonly Regex Trivia = new(@"\G(?:[\s\uFEFF]+|//[^\r\n]*(?:\r?\n|$)|/\*[\s\S]*?\*/)*", RegexOptions.CultureInvariant);
    private static readonly Regex HeaderName = new(@"\G(?:export\s+)?type\s+CopelandProfile\b", RegexOptions.CultureInvariant);
    private static readonly Regex Header = new("\\G(?:export\\s+)?type\\s+CopelandProfile\\s*=\\s*([\"'])([^\"'\\\\\r\n]*)\\1\\s*;", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Names = ["Legacy", "Copeland", "JavaScript", "Dotnet", "Dotnet-NativeAOT"];

    public static SourceFileProfile Read(string source)
    {
        int position = source.Length > 0 && source[0] == '\uFEFF' ? 1 : 0;
        if (source.AsSpan(position).StartsWith("#!"))
        {
            int newline = source.IndexOf('\n', position);
            position = newline < 0 ? source.Length : newline + 1;
        }
        position += Trivia.Match(source, position).Length;
        int start = position;
        string? name = null;
        var diagnostics = new List<SourceProfileDiagnostic>();
        while (HeaderName.IsMatch(source, position))
        {
            Match header = Header.Match(source, position);
            if (!header.Success)
            {
                diagnostics.Add(new("COPE-PROFILE-0001", "Use a file header such as export type CopelandProfile = \"JavaScript\"; before imports and declarations. Only one literal profile name is supported.", position, HeaderName.Match(source, position).Length));
                return new(name, start, position, diagnostics);
            }
            string selected = header.Groups[2].Value;
            if (name is not null)
            {
                diagnostics.Add(new("COPE-PROFILE-0002", "A source file may declare only one CopelandProfile header.", position, header.Length));
            }
            else if (!Names.Contains(selected))
            {
                diagnostics.Add(new("COPE-PROFILE-0001", $"Unknown profile '{selected}'. Use Legacy, Copeland, JavaScript, Dotnet, or Dotnet-NativeAOT.", position, header.Length));
            }
            name ??= selected;
            position += header.Length;
            position += Trivia.Match(source, position).Length;
        }
        return new(name, start, name is null ? 0 : position, diagnostics);
    }
}
