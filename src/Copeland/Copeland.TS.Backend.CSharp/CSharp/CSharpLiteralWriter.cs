using System.Globalization;
using System.Text;

namespace Copeland.TS.Backend.CSharp;

internal static class CSharpLiteralWriter
{
    public static string Write(object? value)
    {
        return value switch
        {
            null => "null",
            bool b => b ? "true" : "false",
            string s => WriteString(s),
            int i => i.ToString("0.0", CultureInfo.InvariantCulture),
            long l => l.ToString("0.0", CultureInfo.InvariantCulture),
            float f => f.ToString("0.0###############", CultureInfo.InvariantCulture),
            double d => d.ToString("0.0###############", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null"
        };
    }

    private static string WriteString(string value)
    {
        var sb = new StringBuilder();
        sb.Append('"');
        foreach (var ch in value)
        {
            sb.Append(ch switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => ch.ToString()
            });
        }
        sb.Append('"');
        return sb.ToString();
    }
}
