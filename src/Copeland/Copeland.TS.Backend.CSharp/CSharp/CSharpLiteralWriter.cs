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
            int i => i.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture) + "L",
            float f => f.ToString("0.0###############", CultureInfo.InvariantCulture),
            double d => WriteDouble(d),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null"
        };
    }

    private static string WriteDouble(double value)
    {
        if (double.IsNaN(value))
        {
            string bits = BitConverter.DoubleToUInt64Bits(value).ToString("X16", CultureInfo.InvariantCulture);
            return $"global::System.BitConverter.UInt64BitsToDouble(0x{bits}UL)";
        }

        if (double.IsPositiveInfinity(value))
        {
            return "global::System.Double.PositiveInfinity";
        }

        if (double.IsNegativeInfinity(value))
        {
            return "global::System.Double.NegativeInfinity";
        }

        if (BitConverter.DoubleToUInt64Bits(value) == 0x8000000000000000)
        {
            return "-0.0";
        }

        string text = value.ToString("R", CultureInfo.InvariantCulture);
        if (!text.Contains('.', StringComparison.Ordinal)
            && !text.Contains('E', StringComparison.OrdinalIgnoreCase))
        {
            text += ".0";
        }

        return text;
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
