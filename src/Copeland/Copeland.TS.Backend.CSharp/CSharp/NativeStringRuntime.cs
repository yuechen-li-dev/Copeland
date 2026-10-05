namespace Copeland.TS.Backend.CSharp;

internal static class NativeStringRuntime
{
    internal static void Emit(CSharpTextWriter writer)
    {
        const string source = """
internal static T[] __cope_native_MutableArrayFilled<T>(int length, T initializer)
{
    if (length < 0) throw new global::System.ArgumentOutOfRangeException(nameof(length), "Copeland mutable array length cannot be negative.");
    var result = new T[length];
    global::System.Array.Fill(result, initializer);
    return result;
}

internal static string[] __cope_native_StringSplit(string value, string separator)
{
    if (separator.Length == 0)
    {
        var result = new string[value.Length];
        for (int index = 0; index < value.Length; index++)
        {
            result[index] = value[index].ToString();
        }
        return result;
    }
    return value.Split(separator, global::System.StringSplitOptions.None);
}

internal static int __cope_native_StringIndexOf(string value, string search)
{
    return value.IndexOf(search, global::System.StringComparison.Ordinal);
}

internal static int __cope_native_StringCodeAt(string value, int index)
{
    if (unchecked((uint)index) >= (uint)value.Length)
    {
        throw new global::System.InvalidOperationException("String index is out of bounds.");
    }
    return value[index];
}

internal static string __cope_native_StringSlice(string value, int start, int end)
{
    int length = value.Length;
    start = start < 0 ? global::System.Math.Max(length + start, 0) : global::System.Math.Min(start, length);
    end = end < 0 ? global::System.Math.Max(length + end, 0) : global::System.Math.Min(end, length);
    return value.Substring(start, global::System.Math.Max(end - start, 0));
}

internal static string __cope_native_StringJoin(string[] parts, string separator)
{
    return string.Join(separator, parts);
}
""";
        foreach (string line in source.Split('\n'))
        {
            writer.WriteLine(line);
        }
    }
}
