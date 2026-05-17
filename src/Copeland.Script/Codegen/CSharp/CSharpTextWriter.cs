using System.Text;

namespace Copeland.Script.Codegen.CSharp;

internal sealed class CSharpTextWriter
{
    private readonly StringBuilder _sb = new();
    private int _indent;

    public void WriteLine(string text = "")
    {
        if (text.Length > 0)
            _sb.Append(' ', _indent * 4);
        _sb.Append(text).Append('\n');
    }

    public void Indent() => _indent++;
    public void Unindent() => _indent--;
    public override string ToString() => _sb.ToString();
}
