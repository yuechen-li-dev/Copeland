namespace Copeland.TS.Backend.CSharp;

internal static class IntegerRuntime
{
    internal static void Emit(CSharpTextWriter writer)
    {
        const string source = """
internal static int __cope_int_Divide(int left, int right)
{
    if (right == 0)
    {
        throw new global::System.InvalidOperationException("Integer division by zero.");
    }
    return unchecked((int)((long)left / right));
}

internal static int __cope_int_Remainder(int left, int right)
{
    if (right == 0)
    {
        throw new global::System.InvalidOperationException("Integer division by zero.");
    }
    return (int)((long)left % right);
}

internal static int __cope_AssignPrevious(ref int storage, int value)
{
    int previous = storage;
    storage = value;
    return previous;
}

internal static double __cope_AssignPrevious(ref double storage, double value)
{
    double previous = storage;
    storage = value;
    return previous;
}
""";
        foreach (string line in source.Split('\n'))
        {
            writer.WriteLine(line);
        }
    }
}
