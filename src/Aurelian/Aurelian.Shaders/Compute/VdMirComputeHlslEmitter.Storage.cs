using System.Text;
using Copeland.TS.Gpu.VdMir;

namespace Aurelian.Shaders.Compute;

public static partial class VdMirComputeHlslEmitter
{
    private static string ResourceType(string element, VdMirResourceAccess access)
    {
        string scalar = element == "f32" ? "float" : "uint";
        return (access == VdMirResourceAccess.Readwrite ? "RWStructuredBuffer" : "StructuredBuffer")
            + "<" + scalar + ">";
    }

    private static string ParameterType(VdMirParameter parameter, VdMirComputeModule module)
    {
        if (parameter.Type.StartsWith("storage-buffer<", StringComparison.Ordinal))
        {
            return ResourceType(parameter.Type["storage-buffer<".Length..^1], parameter.Access ?? VdMirResourceAccess.Readonly);
        }
        return MapType(parameter.Type, module);
    }

    private static void EmitStorageLoads(StringBuilder builder, VdMirComputeModule module)
    {
        var wordLoads = new HashSet<(string Type, bool FloatBuffer)>();
        var indexedLoads = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in module.Functions)
        {
            VisitStatements(function.Statements);
        }
        foreach (var layout in module.StorageLayouts ?? [])
        {
            foreach (var access in Enum.GetValues<VdMirResourceAccess>())
            {
                foreach (bool floatBuffer in new[] { false, true })
                {
                    if (!wordLoads.Contains((layout.Type, floatBuffer)))
                    {
                        continue;
                    }
                    string bufferType = ResourceType(floatBuffer ? "f32" : "u32", access);
                    builder.AppendLine($"{MapType(layout.Type, module)} __vts_load_word_{layout.Type}({bufferType} data, uint start)");
                    builder.AppendLine("{");
                    builder.AppendLine($"    {MapType(layout.Type, module)} value = ({MapType(layout.Type, module)})0;");
                    builder.AppendLine("    uint count, stride;");
                    builder.AppendLine("    data.GetDimensions(count, stride);");
                    builder.AppendLine($"    if (count < {layout.Stride / 4}u || start > count - {layout.Stride / 4}u) return value;");
                    foreach (var field in layout.Fields)
                    {
                        string destination = field.Path.Length == 0 ? "value" : "value." + field.Path;
                        string source = $"data[start + {field.Offset / 4}u]";
                        if (field.Type == "f32" && !floatBuffer)
                        {
                            source = "asfloat(" + source + ")";
                        }
                        else if (field.Type == "u32" && floatBuffer)
                        {
                            source = "asuint(" + source + ")";
                        }
                        builder.AppendLine($"    {destination} = {source};");
                    }
                    builder.AppendLine("    return value;");
                    builder.AppendLine("}");
                }
                if (indexedLoads.Contains(layout.Type))
                {
                    builder.AppendLine($"{MapType(layout.Type, module)} __vts_load_{layout.Type}({ResourceType(layout.Type, access)} data, uint index)");
                    builder.AppendLine("{");
                    builder.AppendLine("    uint count, stride;");
                    builder.AppendLine("    data.GetDimensions(count, stride);");
                    builder.AppendLine($"    if (index >= count / {layout.Stride / 4}u) return ({MapType(layout.Type, module)})0;");
                    builder.AppendLine($"    return __vts_load_word_{layout.Type}(data, index * {layout.Stride / 4}u);");
                    builder.AppendLine("}");
                }
            }
        }

        void VisitStatements(IEnumerable<VdMirStatement> statements)
        {
            foreach (var statement in statements)
            {
                if (statement.Expression is not null)
                {
                    VisitExpression(statement.Expression);
                }
                VisitStatements(statement.Body ?? []);
                VisitStatements(statement.ElseBody ?? []);
            }
        }

        void VisitExpression(VdMirExpression expression)
        {
            if (expression.Kind == "storage-load")
            {
                wordLoads.Add((expression.Type, expression.Operands![0].Type == "storage-buffer<f32>"));
                if (expression.Value != "word")
                {
                    indexedLoads.Add(expression.Type);
                }
            }
            foreach (var operand in expression.Operands ?? [])
            {
                VisitExpression(operand);
            }
        }
    }

    private static void EmitStorageStore(StringBuilder builder, VdMirStatement statement, int indentation, VdMirComputeModule module)
    {
        var assignment = statement.Expression!;
        var layout = module.StorageLayouts!.Single(item => item.Type == assignment.Value);
        string resource = EmitExpression(assignment.Operands![0]);
        string fieldPath = statement.Name!;
        bool wordOffset = assignment.Kind == "storage-assignment-word";
        bool floatBuffer = assignment.Operands![0].Type == "storage-buffer<f32>";
        string prefix = new(' ', indentation * 4);
        builder.AppendLine(prefix + "{");
        builder.AppendLine($"{prefix}    uint __vts_index = {EmitExpression(assignment.Operands[1])};");
        builder.AppendLine($"{prefix}    {MapType(assignment.Type, module)} __vts_value = {EmitExpression(assignment.Operands[2])};");
        builder.AppendLine($"{prefix}    uint __vts_count, __vts_stride;");
        builder.AppendLine($"{prefix}    {resource}.GetDimensions(__vts_count, __vts_stride);");
        string condition = wordOffset
            ? $"__vts_count >= {layout.Stride / 4}u && __vts_index <= __vts_count - {layout.Stride / 4}u"
            : $"__vts_index < __vts_count / {layout.Stride / 4}u";
        builder.AppendLine($"{prefix}    if ({condition})");
        builder.AppendLine(prefix + "    {");
        foreach (var field in layout.Fields.Where(field => fieldPath.Length == 0
            || field.Path == fieldPath || field.Path.StartsWith(fieldPath + ".", StringComparison.Ordinal)))
        {
            string suffix = fieldPath.Length == 0 ? field.Path : field.Path.Length == fieldPath.Length ? "" : field.Path[(fieldPath.Length + 1)..];
            string value = "__vts_value" + (suffix.Length == 0 ? "" : "." + suffix);
            if (field.Type == "f32" && !floatBuffer)
            {
                value = "asuint(" + value + ")";
            }
            else if (field.Type == "u32" && floatBuffer)
            {
                value = "asfloat(" + value + ")";
            }
            string start = wordOffset ? "__vts_index" : $"__vts_index * {layout.Stride / 4}u";
            builder.AppendLine($"{prefix}        {resource}[{start} + {field.Offset / 4}u] = {value};");
        }
        builder.AppendLine(prefix + "    }");
        builder.AppendLine(prefix + "}");
    }
}
