using System.Text;
using Copeland.TS.Gpu.VdMir;
using Aurelian.Shaders.Language.VdMir.Emission.Hlsl;

namespace Aurelian.Shaders.Compute;

public static class VdMirComputeHlslEmitter
{
    public static string Emit(VdMirComputeModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (!module.Success || module.EntryPoint is null)
        {
            throw new InvalidOperationException("Only a successfully bound compute M1 VD-MIR module can be emitted.");
        }

        var builder = new StringBuilder();
        builder.AppendLine($"// Generated from canonical VD-MIR {module.FeatureLevel}. Do not edit.");
        builder.AppendLine();
        VdMirEnumHlslEmitter.Emit(builder, module.Enums);
        VdMirValueHlslEmitter.Emit(builder, module.ValueTypes);
        foreach (VdMirResource resource in module.Resources)
        {
            string resourceType;
            if (resource.ElementType == "acceleration_structure")
            {
                resourceType = "RaytracingAccelerationStructure";
            }
            else if (resource.Access == VdMirResourceAccess.Readonly)
            {
                resourceType = "StructuredBuffer<float>";
            }
            else
            {
                resourceType = "RWStructuredBuffer<float>";
            }
            builder.AppendLine($"[[vk::binding({resource.Binding}, {resource.Set})]] {resourceType} {resource.Name};");
        }
        if (module.Resources.Count > 0)
        {
            builder.AppendLine();
        }
        if (module.FeatureLevel == "compute.rayquery.m2")
        {
            builder.AppendLine(RayQueryHlslEmitter.Helper);
        }

        foreach (VdMirFunction function in module.Functions.Where(function => function.Name != module.EntryPoint.Name))
        {
            string parameters = string.Join(", ", function.Parameters.Select(parameter =>
                MapType(parameter.Type, module) + " " + parameter.Name));
            builder.AppendLine($"{MapType(function.ReturnType, module)} {function.Name}({parameters});");
        }
        foreach (VdMirFunction function in module.Functions.Where(function => function.Name != module.EntryPoint.Name))
        {
            EmitFunction(builder, function, null, module);
            builder.AppendLine();
        }

        VdMirFunction entryFunction = module.Functions.Single(function => function.Name == module.EntryPoint.Name);
        builder.AppendLine($"[numthreads({module.EntryPoint.NumThreadsX}, {module.EntryPoint.NumThreadsY}, {module.EntryPoint.NumThreadsZ})]");
        EmitFunction(builder, entryFunction, module.EntryPoint, module);
        return builder.ToString();
    }

    private static void EmitFunction(StringBuilder builder, VdMirFunction function, VdMirComputeEntryPoint? entry, VdMirComputeModule module)
    {
        string parameters = string.Join(", ", function.Parameters.Select(parameter =>
        {
            string semantic = parameter.Builtin switch
            {
                "dispatch_thread_id" => " : SV_DispatchThreadID",
                null => string.Empty,
                _ => throw new InvalidOperationException($"Unsupported compute builtin '{parameter.Builtin}'."),
            };
            return $"{MapType(parameter.Type, module)} {parameter.Name}{semantic}";
        }));
        builder.AppendLine($"{MapType(function.ReturnType, module)} {function.Name}({parameters})");
        builder.AppendLine("{");
        foreach (VdMirStatement statement in function.Statements)
        {
            EmitStatement(builder, statement, 1, module);
        }
        builder.AppendLine("}");
    }

    private static void EmitStatement(StringBuilder builder, VdMirStatement statement, int indentation, VdMirComputeModule module)
    {
        string prefix = new(' ', indentation * 4);
        switch (statement.Kind)
        {
            case "local":
                builder.AppendLine($"{prefix}{MapType(statement.Type!, module)} {statement.Name} = {EmitExpression(statement.Expression!)};");
                break;
            case "assign":
            {
                VdMirExpression assignment = statement.Expression!;
                builder.AppendLine($"{prefix}{EmitExpression(assignment.Operands![0])} = {EmitExpression(assignment.Operands[1])};");
                break;
            }
            case "expression":
                builder.AppendLine($"{prefix}{EmitExpression(statement.Expression!)};");
                break;
            case "ray-query":
                builder.AppendLine($"{prefix}AurelianTraceClosest({string.Join(", ", statement.Expression!.Operands!.Select(EmitExpression))});");
                break;
            case "if":
                builder.AppendLine($"{prefix}if ({EmitExpression(statement.Expression!)})");
                builder.AppendLine($"{prefix}{{");
                foreach (VdMirStatement nested in statement.Body ?? [])
                {
                    EmitStatement(builder, nested, indentation + 1, module);
                }
                builder.AppendLine($"{prefix}}}");
                if (statement.ElseBody is not null)
                {
                    builder.AppendLine($"{prefix}else");
                    builder.AppendLine($"{prefix}{{");
                    foreach (VdMirStatement nested in statement.ElseBody)
                    {
                        EmitStatement(builder, nested, indentation + 1, module);
                    }
                    builder.AppendLine($"{prefix}}}");
                }
                break;
            case "return":
                if (statement.Expression is { Kind: "object" } value)
                {
                    builder.AppendLine($"{prefix}{MapType(value.Type, module)} result = ({MapType(value.Type, module)})0;");
                    for (int index = 0; index < value.Operands!.Count; index++)
                    {
                        builder.AppendLine($"{prefix}result.{value.MemberNames![index]} = {EmitExpression(value.Operands[index])};");
                    }
                    builder.AppendLine($"{prefix}return result;");
                    break;
                }
                builder.AppendLine(statement.Expression is null
                    ? $"{prefix}return;"
                    : $"{prefix}return {EmitExpression(statement.Expression)};");
                break;
            case "block":
                builder.AppendLine($"{prefix}{{");
                foreach (VdMirStatement child in statement.Body ?? [])
                {
                    EmitStatement(builder, child, indentation + 1, module);
                }
                builder.AppendLine($"{prefix}}}");
                break;
            default:
                throw new InvalidOperationException($"Unsupported compute statement '{statement.Kind}'.");
        }
    }

    private static string EmitExpression(VdMirExpression expression)
    {
        return expression.Kind switch
        {
            "name" or "literal" => expression.Value!,
            "field" => $"{EmitExpression(expression.Operands![0])}.{expression.Value}",
            "index" => $"{EmitExpression(expression.Operands![0])}[{EmitExpression(expression.Operands[1])}]",
            "binary" => $"({EmitExpression(expression.Operands![0])} {expression.Value} {EmitExpression(expression.Operands[1])})",
            "call" => $"{CallName(expression.Value!)}({string.Join(", ", expression.Operands!.Select(EmitExpression))})",
            _ => throw new InvalidOperationException($"Unsupported compute expression '{expression.Kind}'."),
        };
    }

    private static string CallName(string name) => name switch
    {
        "Sqrt" => "sqrt",
        "U32" => "uint",
        _ => name,
    };

    private static string MapType(string type, VdMirComputeModule module) => type switch
    {
        "void" => "void",
        "bool" => "bool",
        "u32" => "uint",
        "f32" => "float",
        "uint3" => "uint3",
        _ when (module.ValueTypes ?? []).Any(value => value.Name == type) => type,
        _ when (module.Enums ?? []).Any(enumeration => enumeration.Name == type || enumeration.Cases.Any(variant => variant.PayloadType == type)) => type,
        _ => throw new InvalidOperationException($"Unsupported compute type '{type}'."),
    };
}
