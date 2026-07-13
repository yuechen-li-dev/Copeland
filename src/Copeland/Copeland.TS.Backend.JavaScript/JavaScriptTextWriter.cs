using System.Text;

namespace Copeland.TS.Backend.JavaScript;

internal sealed class JavaScriptTextWriter
{
    private readonly StringBuilder builder = new();
    private int indent;

    public void WriteLine(string text = "")
    {
        if (text.Length > 0)
        {
            builder.Append(' ', indent * 4);
        }

        builder.Append(text).Append('\n');
    }

    public void Indent()
    {
        indent += 1;
    }

    public void Unindent()
    {
        indent -= 1;
    }

    public override string ToString()
    {
        return builder.ToString();
    }
}
