namespace Copeland.Script.Codegen.CSharp;

internal static class CSharpNameMangler
{
    private static readonly HashSet<string> Keywords =
    [
        "class", "namespace", "public", "private", "protected", "internal", "static", "void", "return", "if", "else", "for", "while", "bool", "string", "double", "new", "var"
    ];

    public static string Mangle(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "_";

        var chars = name.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray();
        if (!char.IsLetter(chars[0]) && chars[0] != '_')
            return "_" + new string(chars);

        var candidate = new string(chars);
        return Keywords.Contains(candidate) ? "@" + candidate : candidate;
    }
}
