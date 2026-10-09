using System.Text;
using Copeland.TS.Gpu.VdMir;

namespace Aurelian.Shaders.Language.VdMir.Emission.Hlsl;

internal static class VdMirValueHlslEmitter
{
    public static void Emit(StringBuilder builder, IReadOnlyList<VdMirValueType>? types)
    {
        foreach (VdMirValueType type in types ?? [])
        {
            builder.AppendLine($"struct {type.Name}");
            builder.AppendLine("{");
            int offset = 0;
            int padding = 0;
            foreach (VdMirValueField field in type.Fields)
            {
                Pad(builder, ref offset, field.Offset, ref padding);
                string storage = field.PhysicalType ?? field.Type;
                string physical = storage switch
                {
                    "f32" => "float",
                    "u32" => "uint",
                    _ => storage,
                };
                builder.AppendLine($"    {physical} {field.Name};");
                offset += field.Size;
            }
            Pad(builder, ref offset, type.Size, ref padding);
            builder.AppendLine("};");
            builder.AppendLine();
        }
    }

    internal static void Pad(StringBuilder builder, ref int offset, int target, ref int padding)
    {
        while (offset < target)
        {
            builder.AppendLine($"    uint VtsPadding{padding++};");
            offset += 4;
        }
    }
}
