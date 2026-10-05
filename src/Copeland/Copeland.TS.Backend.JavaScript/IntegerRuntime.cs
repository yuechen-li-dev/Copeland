namespace Copeland.TS.Backend.JavaScript;

internal static class IntegerRuntime
{
    internal static void Emit(JavaScriptTextWriter writer)
    {
        const string source = """
function __cope_int_Divide(left, right) {
    if (right === 0) {
        throw new Error("Integer division by zero.");
    }
    return Math.trunc(left / right) | 0;
}

function __cope_int_Remainder(left, right) {
    if (right === 0) {
        throw new Error("Integer division by zero.");
    }
    return (left % right) | 0;
}
""";
        foreach (string line in source.Split('\n'))
        {
            writer.WriteLine(line);
        }
    }
}
