using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Copeland.TS.Mir;

/// <summary>
/// Computes a deterministic hash of normalized FLOW MIR. Source paths and
/// authoring syntax are absent from this representation by construction.
/// </summary>
public static class MirFlowSemanticHash
{
    public static string Compute(MirFlowDefinition flow)
    {
        ArgumentNullException.ThrowIfNull(flow);
        var canonical = new StringBuilder();
        AppendValue(canonical, flow);
        byte[] bytes = Encoding.UTF8.GetBytes(canonical.ToString());
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static void AppendValue(StringBuilder builder, object? value)
    {
        if (value is null)
        {
            builder.Append("null");
            return;
        }

        switch (value)
        {
            case string text:
                AppendString(builder, text);
                return;
            case bool boolean:
                builder.Append(boolean ? "true" : "false");
                return;
            case Enum enumeration:
                builder.Append(enumeration.GetType().FullName);
                builder.Append(':');
                builder.Append(enumeration.ToString());
                return;
            case IFormattable formattable when value is not IEnumerable:
                builder.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
                return;
            case IEnumerable sequence:
                builder.Append('[');
                bool first = true;
                foreach (object? item in sequence)
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }
                    AppendValue(builder, item);
                    first = false;
                }
                builder.Append(']');
                return;
        }

        Type type = value.GetType();
        builder.Append(type.FullName);
        builder.Append('{');
        PropertyInfo[] properties = type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();
        for (int index = 0; index < properties.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }
            AppendString(builder, properties[index].Name);
            builder.Append(':');
            AppendValue(builder, properties[index].GetValue(value));
        }
        builder.Append('}');
    }

    private static void AppendString(StringBuilder builder, string value)
    {
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
    }
}
