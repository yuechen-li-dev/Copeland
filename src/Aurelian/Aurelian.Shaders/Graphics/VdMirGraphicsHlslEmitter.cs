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
            throw new InvalidOperationException("Only a successfully linked graphics VD-MIR module can be emitted.");
        }

        var builder = new StringBuilder();
        builder.AppendLine($"// Generated from canonical VD-MIR {module.FeatureLevel}. Do not edit.");
        builder.AppendLine();
        foreach (VdMirSemanticSpace space in module.SemanticSpaces)
        {
            builder.AppendLine($"// semantic space {space.Name} physically lowers to {space.PhysicalType}");
        }
        if (module.SemanticSpaces.Count > 0)
        {
            builder.AppendLine();
        }
        foreach (VdMirMaterial material in module.Materials)
        {
            EmitMaterial(builder, material, module);
            builder.AppendLine();
        }
        foreach (VdMirStream stream in module.Streams.Where(StreamIsEmitted))
        {
            EmitStream(builder, stream, module);
            builder.AppendLine();
        }

        foreach (VdMirGraphicsResource resource in module.GraphicsProgram.Resources)
        {
            EmitResource(builder, resource, module);
        }
        if (module.GraphicsProgram.Resources.Count > 0)
        {
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
        => stream.Role is VdMirStreamRole.StageValue or VdMirStreamRole.Builtin;

    private static void EmitStream(StringBuilder builder, VdMirStream stream, VdMirGraphicsModule module)
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
                "vertex_id" => "SV_VertexID",
                "instance_id" => "SV_InstanceID",
                "front_face" => "SV_IsFrontFace",
                not null => throw new InvalidOperationException($"Unsupported graphics builtin '{member.Builtin}'."),
                null when member.Target is not null => $"SV_Target{member.Target.Value}",
                null when member.Location is not null => $"TEXCOORD{member.Location.Value}",
                _ => throw new InvalidOperationException($"Stream member '{stream.Name}.{member.Name}' has no backend interface identity."),
            };
            builder.AppendLine($"    {modifier}{MapType(member.Type, module)} {member.Name} : {semantic};");
        }
        builder.AppendLine("};");
    }

    private static void EmitFunction(StringBuilder builder, VdMirFunction function, VdMirGraphicsModule module)
    {
        string parameters = string.Join(", ", function.Parameters
            .Where(parameter => module.Streams.All(stream => stream.Name != parameter.Type || stream.Role != VdMirStreamRole.Resource))
            .Select(parameter => $"{MapType(parameter.Type, module)} {parameter.Name}"));
        builder.AppendLine($"{MapType(function.ReturnType, module)} {function.Name}({parameters})");
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
                builder.AppendLine($"{prefix}{MapType(statement.Type!, module)} {statement.Name} = {EmitExpression(statement.Expression!, module)};");
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
            case "if":
                builder.AppendLine($"{prefix}if ({EmitExpression(statement.Expression!, module)})");
                builder.AppendLine($"{prefix}{{");
                foreach (VdMirStatement child in statement.Body ?? [])
                {
                    EmitStatement(builder, child, module, indentation + 1);
                }
                builder.AppendLine($"{prefix}}}");
                if (statement.ElseBody?.Count > 0)
                {
                    builder.AppendLine($"{prefix}else");
                    builder.AppendLine($"{prefix}{{");
                    foreach (VdMirStatement child in statement.ElseBody)
                    {
                        EmitStatement(builder, child, module, indentation + 1);
                    }
                    builder.AppendLine($"{prefix}}}");
                }
                break;
            default:
                throw new InvalidOperationException($"Unsupported graphics statement '{statement.Kind}'.");
        }
    }

    private static void EmitObjectReturn(StringBuilder builder, VdMirExpression expression, VdMirGraphicsModule module, int indentation)
    {
        string prefix = new(' ', indentation * 4);
        builder.AppendLine($"{prefix}{MapType(expression.Type, module)} result;");
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
            "field" when IsResourceRoot(expression.Operands![0], module) => expression.Value!,
            "field" => $"{EmitExpression(expression.Operands![0], module)}.{expression.Value}",
            "call" => $"{expression.Value}({string.Join(", ", expression.Operands!.Select(operand => EmitExpression(operand, module)))})",
            "binary" => $"({EmitExpression(expression.Operands![0], module)} {expression.Value} {EmitExpression(expression.Operands[1], module)})",
            "intrinsic" when expression.Value == "Sample2D" => $"{EmitExpression(expression.Operands![0], module)}.Sample({EmitExpression(expression.Operands[1], module)}, {EmitExpression(expression.Operands[2], module)})",
            "intrinsic" when expression.Value == "ConvertU32ToF32" => $"float({EmitExpression(expression.Operands![0], module)})",
            "object" => throw new InvalidOperationException("Object values must be lowered into generated stream assignments."),
            _ => throw new InvalidOperationException($"Unsupported graphics expression '{expression.Kind}'."),
        };
    }

    private static bool IsResourceRoot(VdMirExpression expression, VdMirGraphicsModule module)
        => expression.Kind == "name"
            && module.Streams.Any(stream => stream.Name == expression.Type && stream.Role == VdMirStreamRole.Resource);

    private static string MapType(string type, VdMirGraphicsModule module)
    {
        VdMirSemanticSpace? space = module.SemanticSpaces.FirstOrDefault(candidate => module.Streams
            .SelectMany(stream => stream.Members)
            .Any(member => member.Type == type && member.SemanticSpace == candidate.Name));
        if (space is not null)
        {
            return MapType(space.PhysicalType, module);
        }
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

    private static void EmitMaterial(StringBuilder builder, VdMirMaterial material, VdMirGraphicsModule module)
    {
        builder.AppendLine($"struct {material.Name}");
        builder.AppendLine("{");
        foreach (VdMirMaterialField field in material.Fields.OrderBy(field => field.Order))
        {
            builder.AppendLine($"    {MapType(field.Type, module)} {field.Name}; // offset {field.Offset}, size {field.Size}, align {field.Alignment}");
        }
        builder.AppendLine("};");
    }

    private static void EmitResource(StringBuilder builder, VdMirGraphicsResource resource, VdMirGraphicsModule module)
    {
        string declaration = resource.Kind switch
        {
            VdMirGraphicsResourceKind.Texture2D => $"Texture2D<{MapType(resource.ElementType!, module)}>",
            VdMirGraphicsResourceKind.Sampler => "SamplerState",
            VdMirGraphicsResourceKind.Material => $"ConstantBuffer<{resource.Type}>",
            _ => throw new InvalidOperationException($"Unsupported graphics resource '{resource.Kind}'."),
        };
        builder.AppendLine($"[[vk::binding({resource.Binding}, {resource.Set})]] {declaration} {resource.Name};");
    }
}
