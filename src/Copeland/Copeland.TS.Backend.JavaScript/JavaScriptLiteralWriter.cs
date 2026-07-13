using System.Globalization;

namespace Copeland.TS.Backend.JavaScript;

internal static class JavaScriptLiteralWriter
{
    public static string WriteNumber(object value)
    {
        return value switch
        {
            int number => number.ToString(CultureInfo.InvariantCulture),
            long number => number.ToString(CultureInfo.InvariantCulture),
            float number => number.ToString("R", CultureInfo.InvariantCulture),
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Unsupported numeric literal runtime type: {value.GetType().FullName}.")
        };
    }
}
