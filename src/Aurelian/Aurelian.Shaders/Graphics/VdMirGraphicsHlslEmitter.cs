using System.Text;
using Copeland.TS.Gpu.VdMir;

namespace Aurelian.Shaders.Graphics;

public static class VdMirGraphicsHlslEmitter
{
    public static string Emit(VdMirGraphicsModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (!module.Success || module.GraphicsProgram is null)
        {
            throw new InvalidOperationException("Only a successfully linked graphics.m2 VD-MIR module can be emitted.");
        }

        var builder = new StringBuilder();
        builder.AppendLine("// Generated from canonical VD-MIR graphics.m2. Do not edit.");
        builder.AppendLine();
        foreach (VdMirStream stream in module.Streams.Where(StreamIsEmitted))
        {
            EmitStream(builder, stream);
            builder.AppendLine();
        }

        HashSet<string> entryNames = module.EntryPoints.Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);
        foreach (VdMirFunction function in module.Functions.Where(function => !entryNames.Contains(function.Name)))
        {
            EmitFunction(builder, function, module);
            builder.AppendLine();
        }
        foreach (VdMirGraphicsEntryPoint entry in module.EntryPoints.OrderBy(entry => entry.Stage))
        {
            VdMirFunction function = module.Functions.Single(candidate => candidate.Name == entry.Name);
            EmitFunction(builder, function, module);
            if (entry != module.EntryPoints.OrderBy(candidate => candidate.Stage).Last())
            {
                builder.AppendLine();
            }
        }
        return builder.ToString();
    }

    private static bool StreamIsEmitted(VdMirStream stream)
        => stream.Role == VdMirStreamRole.StageValue;

    private static void EmitStream(StringBuilder builder, VdMirStream stream)
    {
        builder.AppendLine($"struct {stream.Name}");
        builder.AppendLine("{");
        foreach (VdMirStreamMember member in stream.Members)
        {
            string modifier = member.Interpolation switch
            {
                "flat" => "nointerpolation ",
                "noperspective" => "noperspective ",
                _ => string.Empty,
            };
            string semantic = member.Builtin switch
            {
                "position" => "SV_Position",
                not null => throw new InvalidOperationException($"Unsupported graphics builtin '{member.Builtin}'."),
                null when member.Target is not null => $"SV_Target{member.Target.Value}",
                null when member.Location is not null => $"TEXCOORD{member.Location.Value}",
                _ => throw new InvalidOperationException($"Stream member '{stream.Name}.{member.Name}' has no backend interface identity."),
            };
            builder.AppendLine($"    {modifier}{MapType(member.Type)} {member.Name} : {semantic};");
        }
        builder.AppendLine("};");
    }

    private static void EmitFunction(StringBuilder builder, VdMirFunction function, VdMirGraphicsModule module)
    {
        string parameters = string.Join(", ", function.Parameters.Select(parameter => $"{MapType(parameter.Type)} {parameter.Name}"));
        builder.AppendLine($"{MapType(function.ReturnType)} {function.Name}({parameters})");
        builder.AppendLine("{");
        foreach (VdMirStatement statement in function.Statements)
        {
            EmitStatement(builder, statement, module, 1);
        }
        builder.AppendLine("}");
    }

    private static void EmitStatement(StringBuilder builder, VdMirStatement statement, VdMirGraphicsModule module, int indentation)
    {
        string prefix = new(' ', indentation * 4);
        switch (statement.Kind)
        {
            case "local":
                builder.AppendLine($"{prefix}{MapType(statement.Type!)} {statement.Name} = {EmitExpression(statement.Expression!, module)};");
                break;
            case "return":
                if (statement.Expression!.Kind == "object")
                {
                    EmitObjectReturn(builder, statement.Expression, module, indentation);
                }
                else
                {
                    builder.AppendLine($"{prefix}return {EmitExpression(statement.Expression, module)};");
                }
                break;
            default:
                throw new InvalidOperationException($"Unsupported graphics statement '{statement.Kind}'.");
        }
    }

    private static void EmitObjectReturn(StringBuilder builder, VdMirExpression expression, VdMirGraphicsModule module, int indentation)
    {
        string prefix = new(' ', indentation * 4);
        builder.AppendLine($"{prefix}{MapType(expression.Type)} result;");
        for (int index = 0; index < expression.Operands!.Count; index++)
        {
            builder.AppendLine($"{prefix}result.{expression.MemberNames![index]} = {EmitExpression(expression.Operands[index], module)};");
        }
        builder.AppendLine($"{prefix}return result;");
    }

    private static string EmitExpression(VdMirExpression expression, VdMirGraphicsModule module)
    {
        return expression.Kind switch
        {
            "name" or "literal" => expression.Value!,
            "field" => $"{EmitExpression(expression.Operands![0], module)}.{expression.Value}",
            "call" => $"{expression.Value}({string.Join(", ", expression.Operands!.Select(operand => EmitExpression(operand, module)))})",
            "object" => throw new InvalidOperationException("Object values must be lowered into generated stream assignments."),
            _ => throw new InvalidOperationException($"Unsupported graphics expression '{expression.Kind}'."),
        };
    }

    private static string MapType(string type)
    {
        return type switch
        {
            "f32" => "float",
            "u32" => "uint",
            "bool" => "bool",
            "float2" => "float2",
            "float3" => "float3",
            "float4" => "float4",
            _ => type,
        };
    }
}
