using System.Text;

namespace Copeland.TS.Sidecar;

/// <summary>The compact, newline-delimited TSON envelope used by local workers.</summary>
public static class SidecarWire
{
    public const int MaximumFrameBytes = 1024 * 1024;
    private const string Prefix = "const $schema: string = \"copeland://interop/transport/v1\"; record Envelope { correlation: string; kind: string; operation: string; payload: string; } const $value = $record.Envelope({";

    public static string Write(string correlation, string kind, string operation, string payload)
        => Prefix + "\"correlation\":\"" + Escape(correlation) + "\",\"kind\":\"" + Escape(kind)
            + "\",\"operation\":\"" + Escape(operation) + "\",\"payload\":\"" + Escape(payload) + "\",});";

    private static string Escape(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

    public static bool TryRead(string value, out string correlation, out string kind, out string operation, out string payload)
    {
        correlation = kind = operation = payload = string.Empty;
        int position = 0;
        bool valid = value.StartsWith(Prefix, StringComparison.Ordinal)
            && ReadField(value, ref position, Prefix, "\"correlation\":", out correlation)
            && ReadField(value, ref position, string.Empty, "\"kind\":", out kind)
            && ReadField(value, ref position, string.Empty, "\"operation\":", out operation)
            && ReadField(value, ref position, string.Empty, "\"payload\":", out payload)
            && position <= value.Length - 3
            && string.CompareOrdinal(value, position, "});", 0, 3) == 0
            && position + 3 == value.Length;
        return valid;
    }

    private static bool ReadField(string value, ref int position, string prefix, string label, out string result)
    {
        result = string.Empty;
        if (prefix.Length > 0)
        {
            position = prefix.Length;
        }

        if (position > value.Length - label.Length || string.CompareOrdinal(value, position, label, 0, label.Length) != 0)
        {
            return false;
        }

        position += label.Length;
        if (position >= value.Length || value[position++] != '"')
        {
            return false;
        }

        var builder = new StringBuilder();
        while (position < value.Length)
        {
            char current = value[position++];
            if (current == '"')
            {
                if (position >= value.Length || value[position++] != ',')
                {
                    return false;
                }

                result = builder.ToString();
                return true;
            }

            if (current != '\\')
            {
                builder.Append(current);
                continue;
            }

            if (position >= value.Length)
            {
                return false;
            }

            char escape = value[position++];
            if (escape == 'n') builder.Append('\n');
            else if (escape == 'r') builder.Append('\r');
            else if (escape == 't') builder.Append('\t');
            else if (escape == '"' || escape == '\\') builder.Append(escape);
            else return false;
        }

        return false;
    }
}
