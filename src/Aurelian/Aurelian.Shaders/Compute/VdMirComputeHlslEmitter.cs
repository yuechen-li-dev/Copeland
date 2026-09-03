using System.Text;
using Copeland.TS.Gpu.VdMir;

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
        builder.AppendLine("// Generated from canonical VD-MIR compute.m1. Do not edit.");
        builder.AppendLine();
        foreach (VdMirResource resource in module.Resources)
        {
            string resourceType = resource.Access == VdMirResourceAccess.Readonly
                ? "StructuredBuffer<float>"
                : "RWStructuredBuffer<float>";
            builder.AppendLine($"[[vk::binding({resource.Binding}, {resource.Set})]] {resourceType} {resource.Name};");
        }
        if (module.Resources.Count > 0) builder.AppendLine();

        foreach (VdMirFunction function in module.Functions.Where(function => function.Name != module.EntryPoint.Name))
        {
            EmitFunction(builder, function, null);
            builder.AppendLine();
        }

        VdMirFunction entryFunction = module.Functions.Single(function => function.Name == module.EntryPoint.Name);
        builder.AppendLine($"[numthreads({module.EntryPoint.NumThreadsX}, {module.EntryPoint.NumThreadsY}, {module.EntryPoint.NumThreadsZ})]");
        EmitFunction(builder, entryFunction, module.EntryPoint);
        return builder.ToString();
    }

    private static void EmitFunction(StringBuilder builder, VdMirFunction function, VdMirComputeEntryPoint? entry)
    {
        string parameters = string.Join(", ", function.Parameters.Select(parameter =>
        {
            string semantic = parameter.Builtin switch
            {
                "dispatch_thread_id" => " : SV_DispatchThreadID",
                null => string.Empty,
                _ => throw new InvalidOperationException($"Unsupported compute builtin '{parameter.Builtin}'."),
            };
            return $"{MapType(parameter.Type)} {parameter.Name}{semantic}";
        }));
        builder.AppendLine($"{MapType(function.ReturnType)} {function.Name}({parameters})");
        builder.AppendLine("{");
        foreach (VdMirStatement statement in function.Statements) EmitStatement(builder, statement, 1);
        builder.AppendLine("}");
    }

    private static void EmitStatement(StringBuilder builder, VdMirStatement statement, int indentation)
    {
        string prefix = new(' ', indentation * 4);
        switch (statement.Kind)
        {
            case "local":
                builder.AppendLine($"{prefix}{MapType(statement.Type!)} {statement.Name} = {EmitExpression(statement.Expression!)};");
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
            case "if":
                builder.AppendLine($"{prefix}if ({EmitExpression(statement.Expression!)})");
                builder.AppendLine($"{prefix}{{");
                foreach (VdMirStatement nested in statement.Body ?? []) EmitStatement(builder, nested, indentation + 1);
                builder.AppendLine($"{prefix}}}");
                if (statement.ElseBody is not null)
                {
                    builder.AppendLine($"{prefix}else");
                    builder.AppendLine($"{prefix}{{");
                    foreach (VdMirStatement nested in statement.ElseBody) EmitStatement(builder, nested, indentation + 1);
                    builder.AppendLine($"{prefix}}}");
                }
                break;
            case "return":
                builder.AppendLine(statement.Expression is null
                    ? $"{prefix}return;"
                    : $"{prefix}return {EmitExpression(statement.Expression)};");
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
            "call" => $"{expression.Value}({string.Join(", ", expression.Operands!.Select(EmitExpression))})",
            _ => throw new InvalidOperationException($"Unsupported compute expression '{expression.Kind}'."),
        };
    }

    private static string MapType(string type) => type switch
    {
        "void" => "void",
        "bool" => "bool",
        "u32" => "uint",
        "f32" => "float",
        "uint3" => "uint3",
        _ => throw new InvalidOperationException($"Unsupported compute type '{type}'."),
    };
}
