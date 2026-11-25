using Copeland.TS.Mir;

namespace Copeland.TS.Backend.JavaScript;

/// <summary>Projects public Node values at the ESM boundary while preserving internal MIR carriers.</summary>
internal sealed class NodeInteropEmitter(MirProgram program, string prefix, string check, string arrayProjection)
{
    private readonly Dictionary<MirRecordTypeId, MirRecordDefinition> records = program.Records.ToDictionary(record => record.Id);
    private readonly Dictionary<string, MirEnum> enums = program.Enums.ToDictionary(item => item.Name, StringComparer.Ordinal);
    private readonly Dictionary<MirType, int> projectionIndices = [];
    private readonly List<MirType> projections = [];

    public sealed record Facade(string Source, string Declarations);

    private sealed record Field(string Name, MirType Type);
    private sealed record Variant(string Tag, IReadOnlyList<Field> Fields, bool NestedPayload);

    public static IReadOnlySet<string> FindArraySnapshotCandidates(MirProgram program, IReadOnlySet<string> boundaryFunctions)
    {
        var emitter = new NodeInteropEmitter(program, string.Empty, string.Empty, string.Empty);
        return program.Functions
            .Where(function => boundaryFunctions.Contains(function.Name) && CanFuseArraySnapshot(function) && emitter.Supports(function))
            .Select(function => function.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    public static Facade? Emit(
        MirProgram program,
        MirProjectModule module,
        IReadOnlyList<MirModuleExport> exports,
        List<string> lines,
        string moduleSpecifier,
        JavaScriptCompilation compilation)
    {
        string prefix = "__cope_node_";
        while (lines.Any(line => line.Contains(prefix, StringComparison.Ordinal)))
        {
            prefix += "_";
        }
        string? snapshot = exports.Any(export => compilation.NodeArraySnapshotFunctions.Contains(export.RuntimeName!))
            ? compilation.NodeArraySnapshotHelper : null;
        JavaScriptNodeProjectionBindings? shared = compilation.SharedNodeProjections;
        string check = shared?.Check ?? prefix + "check";
        string arrayProjection = shared?.CopyArray ?? snapshot ?? prefix + "array";
        var emitter = new NodeInteropEmitter(program, prefix, check, arrayProjection);
        var declarations = new List<string>();
        var bindings = new List<(string Wrapper, string Export)>();
        bool declaresDocument = false;
        var functions = module.Functions.ToDictionary(function => function.Name, StringComparer.Ordinal);
        foreach (MirModuleExport export in exports)
        {
            if (!functions.TryGetValue(export.RuntimeName!, out MirFunction? function) || !emitter.Supports(function))
            {
                continue;
            }
            string wrapper = $"{prefix}export_{bindings.Count}";
            string? functionSnapshot = compilation.NodeArraySnapshotFunctions.Contains(function.Name) ? snapshot : null;
            emitter.EmitWrapper(lines, wrapper, function, functionSnapshot);
            if (IsDocument(function.ReturnType))
            {
                declaresDocument = true;
            }
            declarations.Add($"declare function {wrapper}({emitter.TypeParameters(function.Parameters.Select(parameter => parameter.Type))}): {emitter.TypeScript(function.ReturnType)};");
            bindings.Add((wrapper, JavaScriptIdentifierEncoder.Encode(export.Name)));
        }
        if (bindings.Count == 0)
        {
            return null;
        }
        if (shared is null)
        {
            lines.Add(ProjectionRuntime(check, arrayProjection));
        }
        else if (snapshot is not null)
        {
            // The module-private function also selects the optimized return path.
            lines.Add($"function {snapshot}(value) {{ return {arrayProjection}(value); }}");
        }
        emitter.EmitProjections(lines, declarations);
        if (declaresDocument)
        {
            declarations.Add($"type {prefix}DocumentNode = Readonly<{{ tag: \"Document\" | \"Paragraph\" | \"Strong\" | \"Emphasis\" | \"InlineCode\" | \"Item\" | \"Quote\" | \"Callout\" | \"Break\" | \"ThematicBreak\" }}>"
                + " | Readonly<{ tag: \"Heading\"; payload: Readonly<{ level: string }> }>"
                + " | Readonly<{ tag: \"List\"; payload: Readonly<{ kind: string }> }>"
                + " | Readonly<{ tag: \"CodeBlock\"; payload: Readonly<{ language: string }> }>"
                + " | Readonly<{ tag: \"Link\"; payload: Readonly<{ target: string }> }>;");
        }
        string localExports = string.Join(", ", bindings.Select(binding => binding.Wrapper));
        string facadeExports = string.Join(", ", bindings.Select(binding => $"{binding.Wrapper} as {binding.Export}"));
        lines.Add($"export {{ {localExports} }};");
        declarations.Add($"export {{ {facadeExports} }};");
        return new Facade($"export {{ {facadeExports} }} from {Quote(moduleSpecifier)};{Environment.NewLine}",
            string.Join(Environment.NewLine, declarations) + Environment.NewLine);
    }

    private bool Supports(MirFunction function)
        => !function.IsAsync && !function.IsRemote && !function.IsGenerator && function.ComponentState is null
            && (IsVoid(function.ReturnType) || IsDocument(function.ReturnType) || SupportsValue(function.ReturnType, []))
            && function.Parameters.All(parameter => parameter.Type is MirCallableType callable
                ? callable.Parameters.All(input => SupportsValue(input.Type, []))
                    && (IsVoid(callable.ReturnType) || SupportsValue(callable.ReturnType, []))
                : SupportsValue(parameter.Type, []));

    private static bool IsScalar(MirType type)
        => type is MirNamedType { Identifier: "boolean" or "int" or "long" or "float" or "number" or "string" };

    private static bool IsDocument(MirType type) => type is MirNamedType { Identifier: "Document" };

    private static bool CanFuseArraySnapshot(MirFunction function)
    {
        if (function.ReturnType is not MirArrayType array || !IsScalar(array.ElementType)
            || function.Body.Count == 0
            || function.Body[^1] is not MirReturnStatement
            {
                Expression: MirMutableArrayFreezeExpression { Receiver: MirVariableExpression receiver },
            })
        {
            return false;
        }

        // The terminal freeze has a plain compiler-allocated receiver. The Node path can
        // take its own snapshot directly, even when the body retains an alias to the buffer.
        return function.Body.Take(function.Body.Count - 1).All(HasNoReturn)
            && function.Body.OfType<MirVariableDeclarationStatement>().Any(declaration =>
                declaration.Local.IsReadOnly && declaration.Local.Name == receiver.Name
                && declaration.Initializer is MirMutableArrayConstructionExpression);
    }

    private static bool HasNoReturn(MirStatement statement) => statement switch
    {
        MirVariableDeclarationStatement or MirExpressionStatement or MirBreakStatement or MirContinueStatement => true,
        MirIfStatement conditional => conditional.ThenStatements.All(HasNoReturn)
            && (conditional.ElseStatements?.All(HasNoReturn) ?? true),
        MirWhileStatement loop => loop.BodyStatements.All(HasNoReturn),
        MirForStatement loop => (loop.Initializer is null || HasNoReturn(loop.Initializer)) && loop.BodyStatements.All(HasNoReturn),
        MirForOfStatement loop => loop.BodyStatements.All(HasNoReturn),
        _ => false,
    };

    private bool SupportsValue(MirType type, HashSet<MirType> activeTypes)
    {
        if (type is MirArrayType array)
        {
            return SupportsValue(array.ElementType, activeTypes);
        }
        if (type is MirRecordType recordType)
        {
            if (!records.TryGetValue(recordType.RecordTypeId, out MirRecordDefinition? record)
                || record.IsClass || !activeTypes.Add(type))
            {
                return false;
            }
            bool supported = record.Fields.All(field => field.IsPublic && SupportsValue(field.Type, activeTypes));
            activeTypes.Remove(type);
            return supported;
        }
        if (type is MirResultType || type is MirNamedType named && enums.ContainsKey(named.Identifier))
        {
            if (!activeTypes.Add(type))
            {
                return false;
            }
            bool supported = Variants(type).All(variant =>
                variant.Fields.All(field => SupportsValue(field.Type, activeTypes)));
            activeTypes.Remove(type);
            return supported;
        }
        return IsScalar(type);
    }

    private void EmitWrapper(List<string> lines, string wrapper, MirFunction function, string? snapshot)
    {
        string[] parameters = function.Parameters.Select((_, index) => $"{prefix}arg_{index}").ToArray();
        var arguments = function.Parameters.Select((parameter, index) => parameter.Type is MirCallableType callback
            ? WrapCallback(callback, parameters[index])
            : Project(parameter.Type, parameters[index], ingress: true)).ToList();
        if (snapshot is not null)
        {
            arguments.Add(snapshot);
        }
        string invocation = $"{JavaScriptIdentifierEncoder.Encode(function.Name)}({string.Join(", ", arguments)})";
        bool freshLiteral = function.ReturnType is MirArrayType array && IsScalar(array.ElementType)
            && function.Body is [MirReturnStatement { Expression: MirArrayExpression { Counts: null } }];
        lines.Add($"function {wrapper}({string.Join(", ", parameters)}) {{");
        if (IsVoid(function.ReturnType))
        {
            lines.Add($"    {invocation};");
        }
        else
        {
            string result = snapshot is not null || freshLiteral ? invocation : Project(function.ReturnType, invocation, ingress: false);
            lines.Add($"    return {result};");
        }
        lines.Add("}");
    }

    private string WrapCallback(MirCallableType callback, string value)
    {
        string signature = Quote(JavaScriptBackend.CallableTypeIdentity(callback));
        string[] parameters = callback.Parameters.Select((_, index) => $"input{index}").ToArray();
        string[] arguments = callback.Parameters.Select((parameter, index) => Project(parameter.Type, parameters[index], ingress: false)).ToArray();
        string call = $"{value}({string.Join(", ", arguments)})";
        string result = IsVoid(callback.ReturnType) ? $"{{ {call}; }}" : Project(callback.ReturnType, call, ingress: true);
        return $"__cope_callable_ref({signature}, ({check}({value}, \"function\"), ({string.Join(", ", parameters)}) => {result}))";
    }

    private string Project(MirType type, string value, bool ingress)
    {
        if (type is MirArrayType array)
        {
            if (IsScalar(array.ElementType))
            {
                string scalarCheck = ingress ? ", " + Quote(array.ElementType.Identifier) : string.Empty;
                return $"{arrayProjection}({value}{scalarCheck})";
            }
            return $"{arrayProjection}({value}, item => {Project(array.ElementType, "item", ingress)})";
        }
        if (type is MirRecordType or MirResultType || type is MirNamedType named && enums.ContainsKey(named.Identifier))
        {
            return $"{prefix}value_{(ingress ? "in" : "out")}_{ProjectionIndex(type)}({value})";
        }
        return ingress ? $"{check}({value}, {Quote(type.Identifier)})" : value;
    }

    private int ProjectionIndex(MirType type)
    {
        if (!projectionIndices.TryGetValue(type, out int index))
        {
            index = projections.Count;
            projectionIndices.Add(type, index);
            projections.Add(type);
        }
        return index;
    }

    private string TypeParameters(IEnumerable<MirType> types)
        => string.Join(", ", types.Select((type, index) => $"arg{index}: {TypeScript(type)}"));

    private string TypeScript(MirType type) => type switch
    {
        MirCallableType callable => $"({TypeParameters(callable.Parameters.Select(parameter => parameter.Type))}) => {TypeScript(callable.ReturnType)}",
        MirArrayType array => $"Array<{TypeScript(array.ElementType)}>",
        MirRecordType or MirResultType => $"{prefix}Value{ProjectionIndex(type)}",
        MirNamedType named when enums.ContainsKey(named.Identifier) => $"{prefix}Value{ProjectionIndex(type)}",
        MirNamedType { Identifier: "long" } => "bigint",
        MirNamedType { Identifier: "Document" } => $"ReadonlyArray<Readonly<{{ tag: \"Open\"; payload: Readonly<{{ node: {prefix}DocumentNode; role: string }}> }}> | Readonly<{{ tag: \"Close\"; payload: Readonly<{{ node: {prefix}DocumentNode }}> }}> | Readonly<{{ tag: \"Run\"; payload: Readonly<{{ text: string }}> }}>>",
        MirNamedType { Identifier: "int" or "float" } => "number",
        _ => type.Identifier,
    };

    private IReadOnlyList<Variant> Variants(MirType type)
    {
        if (type is MirResultType result)
        {
            return [
                new Variant("ok", IsVoid(result.SuccessType) ? [] : [new Field("value", result.SuccessType)], false),
                new Variant("err", IsVoid(result.ErrorType) ? [] : [new Field("error", result.ErrorType)], false),
            ];
        }
        MirEnum definition = enums[type.Identifier];
        return definition.Cases.Select(item => new Variant(item.Name,
            item.PayloadFields.Select(field => new Field(field.Name, field.Type)).ToArray(),
            !definition.IsOption && item.PayloadFields.Count > 0)).ToArray();
    }

    public static string ProjectionRuntime(string check, string arrayProjection)
        => """
            function CHECK_VALUE(value, type) {
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
            function ARRAY_PROJECTION(value, convert) {
                if (!Array.isArray(value)) throw new TypeError("Copeland Node interop expected an array");
                const length = value.length;
                const items = new Array(length);
                const scalarType = typeof convert === "string" ? convert : undefined;
                for (let index = 0; index < length; index++) {
                    if (!Object.hasOwn(value, index)) throw new TypeError("Copeland Node interop expected a dense array");
                    const item = value[index];
                    if (scalarType !== undefined) items[index] = CHECK_VALUE(item, scalarType);
                    else if (convert === undefined) items[index] = item;
                    else items[index] = convert(item);
                }
                return items;
            }
            """.Replace("CHECK_VALUE", check, StringComparison.Ordinal)
                .Replace("ARRAY_PROJECTION", arrayProjection, StringComparison.Ordinal);

    private void EmitProjections(List<string> lines, List<string> declarations)
    {
        // Projecting a field can append another type for a later iteration.
        for (int index = 0; index < projections.Count; index++)
        {
            MirType type = projections[index];
            if (type is MirRecordType recordType)
            {
                MirRecordDefinition record = records[recordType.RecordTypeId];
                EmitRecord(lines, record, index);
                declarations.Add($"type {prefix}Value{index} = {TypeScriptFields(record.Fields.Select(field => new Field(field.Name, field.Type)))};");
            }
            else
            {
                IReadOnlyList<Variant> variants = Variants(type);
                EmitVariants(lines, type, variants, index);
                string alternatives = variants.Count == 0 ? "never" : string.Join(" | ", variants.Select(TypeScriptVariant));
                declarations.Add($"type {prefix}Value{index} = {alternatives};");
            }
        }
    }

    private string TypeScriptFields(IEnumerable<Field> fields)
        => "{ " + string.Join("; ", fields.Select(field => $"{Quote(field.Name)}: {TypeScript(field.Type)}")) + " }";

    private string TypeScriptVariant(Variant variant)
    {
        var properties = new List<string> { $"tag: {Quote(variant.Tag)}" };
        if (variant.NestedPayload)
        {
            properties.Add($"payload: {TypeScriptFields(variant.Fields)}");
        }
        else
        {
            properties.AddRange(variant.Fields.Select(field => $"{Quote(field.Name)}: {TypeScript(field.Type)}"));
        }
        return "{ " + string.Join("; ", properties) + " }";
    }

    private void EmitVariants(List<string> lines, MirType type, IReadOnlyList<Variant> variants, int index)
    {
        lines.Add($"function {prefix}value_in_{index}(value) {{");
        lines.Add("    if (value === null || typeof value !== \"object\" || Array.isArray(value) || !Object.hasOwn(value, \"tag\")) {");
        lines.Add("        throw new TypeError(\"Copeland Node interop expected a tagged object\");");
        lines.Add("    }");
        lines.Add("    switch (value.tag) {");
        foreach (Variant variant in variants)
        {
            lines.Add($"        case {Quote(variant.Tag)}: {{");
            string container = "value";
            if (variant.NestedPayload)
            {
                lines.Add("            const payload = Object.hasOwn(value, \"payload\") ? value.payload : undefined;");
                lines.Add("            if (payload === null || typeof payload !== \"object\" || Array.isArray(payload)) {");
                lines.Add("                throw new TypeError(\"Copeland Node interop expected a payload object\");");
                lines.Add("            }");
                container = "payload";
            }
            if (variant.Fields.Count > 0)
            {
                string keys = string.Join(", ", variant.Fields.Select(field => Quote(field.Name)));
                lines.Add($"            if ([{keys}].some(key => !Object.hasOwn({container}, key))) {{");
                lines.Add("                throw new TypeError(\"Copeland Node interop expected all payload fields\");");
                lines.Add("            }");
            }

            string values = string.Join(", ", variant.Fields.Select(field => Project(field.Type, $"{container}[{Quote(field.Name)}]", ingress: true)));
            string construction = type is MirResultType result
                ? $"{JavaScriptBackend.GetNodeResultFactoryName(result)}({Quote(variant.Tag)}, {(variant.Fields.Count == 0 ? "null" : values)})"
                : $"{JavaScriptBackend.GetEnumFactoryName(type.Identifier, variant.Tag)}({values})";
            lines.Add($"            return {construction};");
            lines.Add("        }");
        }
        lines.Add("        default: throw new TypeError(\"Copeland Node interop received an unknown tag\");");
        lines.Add("    }");
        lines.Add("}");

        lines.Add($"function {prefix}value_out_{index}(value) {{");
        lines.Add("    switch (value.$tag) {");
        foreach (Variant variant in variants)
        {
            var properties = new List<string> { $"tag: {Quote(variant.Tag)}" };
            string[] payload = variant.Fields.Select((field, position) =>
            {
                string member = type is MirResultType ? "value.$payload[0]" : $"value.{JavaScriptBackend.ProductionEnumPayloadName(position)}";
                return $"[{Quote(field.Name)}]: {Project(field.Type, member, ingress: false)}";
            }).ToArray();
            if (variant.NestedPayload)
            {
                properties.Add("payload: { " + string.Join(", ", payload) + " }");
            }
            else
            {
                properties.AddRange(payload);
            }
            lines.Add($"        case {Quote(variant.Tag)}: return {{ {string.Join(", ", properties)} }};");
        }
        lines.Add("        default: throw new TypeError(\"Copeland Node interop received an unknown carrier tag\");");
        lines.Add("    }");
        lines.Add("}");
    }

    private void EmitRecord(List<string> lines, MirRecordDefinition record, int index)
    {
        string keys = string.Join(", ", record.Fields.Select(field => Quote(field.Name)));
        lines.Add($"function {prefix}value_in_{index}(value) {{");
        lines.Add($"    if (value === null || typeof value !== \"object\" || Array.isArray(value) || [{keys}].some(key => !Object.hasOwn(value, key))) {{");
        lines.Add($"        throw new TypeError({Quote($"Copeland Node interop expected fields for {record.Name}")});");
        lines.Add("    }");
        string inputs = string.Join(", ", record.Fields.Select(field => Project(field.Type, $"value[{Quote(field.Name)}]", ingress: true)));
        lines.Add($"    return {JavaScriptBackend.GetRecordFactoryName(record.Id)}({inputs});");
        lines.Add("}");

        lines.Add($"function {prefix}value_out_{index}(value) {{");
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
