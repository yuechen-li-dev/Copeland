using System.Text;
using Copeland.TS.Gpu.VdMir;

namespace Aurelian.Shaders.Language.VdMir.Emission.Hlsl;

internal static class VdMirEnumHlslEmitter
{
    public static void Emit(StringBuilder builder, IReadOnlyList<VdMirEnum>? enums)
    {
        foreach (VdMirEnum enumeration in enums ?? [])
        {
            foreach (VdMirEnumCase variant in enumeration.Cases.Where(item => item.Payload.Count > 0))
            {
                if (variant.PayloadLayout is { } payload)
                {
                    VdMirValueHlslEmitter.Emit(builder, [payload]);
                }
                else Structure(builder, variant.PayloadType, variant.Payload);
            }
            if (enumeration.CarrierLayout is { } carrier)
            {
                VdMirValueHlslEmitter.Emit(builder, [carrier]);
            }
            else Structure(builder, enumeration.Name, enumeration.CarrierFields);
        }
    }

    private static void Structure(StringBuilder builder, string name, IReadOnlyList<VdMirEnumField> fields)
    {
        builder.AppendLine($"struct {name}");
        builder.AppendLine("{");
        foreach (VdMirEnumField field in fields)
        {
            string type = field.Type switch
            {
                "f32" => "float",
                "u32" => "uint",
                _ => field.Type,
            };
            builder.AppendLine($"    {type} {field.Name};");
        }
        builder.AppendLine("};");
        builder.AppendLine();
    }
}
