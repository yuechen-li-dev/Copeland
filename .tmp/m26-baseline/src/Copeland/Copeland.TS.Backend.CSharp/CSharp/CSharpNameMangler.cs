namespace Copeland.TS.Backend.CSharp;

internal static class CSharpNameMangler
{
    private static readonly HashSet<string> Keywords =
    [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while"
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
