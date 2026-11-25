using Copeland.TS.Mir;

namespace Copeland.TS.Backend.JavaScript;

/// <summary>Projects public Node values at the ESM boundary while preserving internal MIR carriers.</summary>
internal sealed class NodeInteropEmitter(IReadOnlyList<MirRecordDefinition> definitions, string prefix)
{
    private readonly Dictionary<MirRecordTypeId, MirRecordDefinition> records = definitions.ToDictionary(record => record.Id);
    private readonly Dictionary<MirRecordTypeId, int> projections = [];

    public static string? Emit(
        IReadOnlyList<MirRecordDefinition> definitions,
        MirProjectModule module,
        IReadOnlyList<MirModuleExport> exports,
        List<string> lines,
        string moduleSpecifier)
    {
        string prefix = "__cope_node_";
        while (lines.Any(line => line.Contains(prefix, StringComparison.Ordinal)))
        {
            prefix += "_";
        }
        var emitter = new NodeInteropEmitter(definitions, prefix);
        var bindings = new List<(string Wrapper, string Export)>();
        var functions = module.Functions.ToDictionary(function => function.Name, StringComparer.Ordinal);
        foreach (MirModuleExport export in exports)
        {
            if (!functions.TryGetValue(export.RuntimeName!, out MirFunction? function) || !emitter.Supports(function))
            {
                continue;
            }
            string wrapper = $"{prefix}export_{bindings.Count}";
            emitter.EmitWrapper(lines, wrapper, function);
            bindings.Add((wrapper, JavaScriptIdentifierEncoder.Encode(export.Name)));
        }
        if (bindings.Count == 0)
        {
            return null;
        }
        emitter.EmitProjections(lines);
        string localExports = string.Join(", ", bindings.Select(binding => binding.Wrapper));
        string facadeExports = string.Join(", ", bindings.Select(binding => $"{binding.Wrapper} as {binding.Export}"));
        lines.Add($"export {{ {localExports} }};");
        return $"export {{ {facadeExports} }} from {Quote(moduleSpecifier)};{Environment.NewLine}";
    }

    private bool Supports(MirFunction function)
        => !function.IsAsync && !function.IsRemote && !function.IsGenerator && function.ComponentState is null
            && (IsVoid(function.ReturnType) || SupportsValue(function.ReturnType, []))
            && function.Parameters.All(parameter => parameter.Type is MirCallableType callable
                ? callable.Parameters.All(input => SupportsValue(input.Type, []))
                    && (IsVoid(callable.ReturnType) || SupportsValue(callable.ReturnType, []))
                : SupportsValue(parameter.Type, []));

    private bool SupportsValue(MirType type, HashSet<MirRecordTypeId> activeRecords)
    {
        if (type is MirArrayType array)
        {
            return SupportsValue(array.ElementType, activeRecords);
        }
        if (type is MirRecordType recordType)
        {
            if (!records.TryGetValue(recordType.RecordTypeId, out MirRecordDefinition? record)
                || record.IsClass || !activeRecords.Add(record.Id))
            {
                return false;
            }
            bool supported = record.Fields.All(field => field.IsPublic && SupportsValue(field.Type, activeRecords));
            activeRecords.Remove(record.Id);
            return supported;
        }
        return type is MirNamedType { Identifier: "boolean" or "int" or "long" or "float" or "number" or "string" };
    }

    private void EmitWrapper(List<string> lines, string wrapper, MirFunction function)
    {
        string[] parameters = function.Parameters.Select((_, index) => $"{prefix}arg_{index}").ToArray();
        string[] arguments = function.Parameters.Select((parameter, index) => parameter.Type is MirCallableType callback
            ? WrapCallback(callback, parameters[index])
            : Project(parameter.Type, parameters[index], ingress: true)).ToArray();
        string invocation = $"{JavaScriptIdentifierEncoder.Encode(function.Name)}({string.Join(", ", arguments)})";
        lines.Add($"function {wrapper}({string.Join(", ", parameters)}) {{");
        lines.Add(IsVoid(function.ReturnType) ? $"    {invocation};" : $"    return {Project(function.ReturnType, invocation, ingress: false)};");
        lines.Add("}");
    }

    private string WrapCallback(MirCallableType callback, string value)
    {
        string signature = Quote(JavaScriptBackend.CallableTypeIdentity(callback));
        string[] parameters = callback.Parameters.Select((_, index) => $"input{index}").ToArray();
        string[] arguments = callback.Parameters.Select((parameter, index) => Project(parameter.Type, parameters[index], ingress: false)).ToArray();
        string call = $"{value}({string.Join(", ", arguments)})";
        string result = IsVoid(callback.ReturnType) ? $"{{ {call}; }}" : Project(callback.ReturnType, call, ingress: true);
        return $"__cope_callable_host({signature}, ({prefix}check({value}, \"function\"), ({string.Join(", ", parameters)}) => {result}))";
    }

    private string Project(MirType type, string value, bool ingress)
    {
        if (type is MirArrayType array)
        {
            return $"{prefix}array({value}, item => {Project(array.ElementType, "item", ingress)})";
        }
        if (type is MirRecordType record)
        {
            if (!projections.TryGetValue(record.RecordTypeId, out int index))
            {
                index = projections.Count;
                projections.Add(record.RecordTypeId, index);
            }
            return $"{prefix}record_{(ingress ? "in" : "out")}_{index}({value})";
        }
        return ingress ? $"{prefix}check({value}, {Quote(type.Identifier)})" : value;
    }

    private void EmitProjections(List<string> lines)
    {
        lines.Add("""
            function __cope_node_check(value, type) {
                let valid;
                switch (type) {
                    case "int": valid = Number.isInteger(value) && value >= -2147483648 && value <= 2147483647; break;
                    case "long": valid = typeof value === "bigint" && value >= -9223372036854775808n && value <= 9223372036854775807n; break;
                    case "float":
                    case "number": valid = typeof value === "number"; break;
                    default: valid = typeof value === type;
                }
                if (!valid) throw new TypeError("Copeland Node interop expected " + type);
                return value;
            }
            function __cope_node_array(value, convert) {
                if (!Array.isArray(value)) throw new TypeError("Copeland Node interop expected an array");
                const length = value.length;
                const items = new Array(length);
                for (let index = 0; index < length; index++) {
                    if (!Object.hasOwn(value, index)) throw new TypeError("Copeland Node interop expected a dense array");
                    items[index] = convert(value[index]);
                }
                return items;
            }
            """.Replace("__cope_node_", prefix, StringComparison.Ordinal));
        // Projecting a field may discover another record. Defer emitting it to the next pass.
        var emitted = new HashSet<MirRecordTypeId>();
        while (emitted.Count < projections.Count)
        {
            foreach ((MirRecordTypeId id, int index) in projections.Where(pair => !emitted.Contains(pair.Key)).ToArray())
            {
                EmitRecord(lines, records[id], index);
                emitted.Add(id);
            }
        }
    }

    private void EmitRecord(List<string> lines, MirRecordDefinition record, int index)
    {
        string keys = string.Join(", ", record.Fields.Select(field => Quote(field.Name)));
        lines.Add($"function {prefix}record_in_{index}(value) {{");
        lines.Add($"    if (value === null || typeof value !== \"object\" || Array.isArray(value) || [{keys}].some(key => !Object.hasOwn(value, key))) {{");
        lines.Add($"        throw new TypeError({Quote($"Copeland Node interop expected fields for {record.Name}")});");
        lines.Add("    }");
        string inputs = string.Join(", ", record.Fields.Select(field => Project(field.Type, $"value[{Quote(field.Name)}]", ingress: true)));
        lines.Add($"    return {JavaScriptBackend.GetRecordFactoryName(record.Id)}({inputs});");
        lines.Add("}");

        lines.Add($"function {prefix}record_out_{index}(value) {{");
        lines.Add("    return {");
        foreach (MirRecordFieldDefinition field in record.Fields)
        {
            string member = $"value.{JavaScriptBackend.ProductionRecordFieldName(record, field)}";
            lines.Add($"        [{Quote(field.Name)}]: {Project(field.Type, member, ingress: false)},");
        }
        lines.Add("    };");
        lines.Add("}");
    }

    private static bool IsVoid(MirType type) => type is MirNamedType { Identifier: "void" };

    private static string Quote(string value) => JavaScriptLiteralWriter.WriteString(value);
}
