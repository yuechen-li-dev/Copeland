namespace Copeland.TS.Backend.JavaScript;

internal static class NativeStringRuntime
{
    internal static void Emit(JavaScriptTextWriter writer)
    {
        const string source = """
function __cope_native_MutableListCreate() {
    return [];
}

function __cope_native_MutableListAppend(list, value) {
    list.push(value);
}

function __cope_native_MutableArrayFilled(length, initializer) {
    if (length < 0) throw new Error("Copeland mutable array length cannot be negative.");
    return new Array(length).fill(initializer);
}

function __cope_native_StringSplit(value, separator) {
    return value.split(separator);
}

function __cope_native_StringIndexOf(value, search) {
    return value.indexOf(search);
}

function __cope_native_StringCodeAt(value, index) {
    if (index < 0 || index >= value.length) {
        throw new Error("String index is out of bounds.");
    }
    return value.charCodeAt(index);
}

function __cope_native_StringSlice(value, start, end) {
    return value.slice(start, end);
}

function __cope_native_StringJoin(parts, separator) {
    return parts.join(separator);
}

function __cope_native_StringFromCode(code) {
    if (code < 0 || code > 65535) throw new Error("Invalid UTF-16 code unit.");
    return String.fromCharCode(code);
}
""";
        foreach (string line in source.Split('\n'))
        {
            writer.WriteLine(line);
        }
    }
}
