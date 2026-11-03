using System.Text;
using Copeland.TS.Gpu.VdMir;

namespace Aurelian.Shaders.Graphics;

public static partial class VdMirGraphicsHlslEmitter
{
    private static void EmitGraphicsStorageLoads(StringBuilder builder, VdMirGraphicsModule module)
    {
        foreach (VdMirStorageLayout layout in module.StorageLayouts ?? [])
        {
            builder.AppendLine($"{MapType(layout.Type, module)} {GraphicsStorageLoadName(layout.Type, module)}(StructuredBuffer<uint> data, uint index)");
            builder.AppendLine("{");
            builder.AppendLine($"    {MapType(layout.Type, module)} value = ({MapType(layout.Type, module)})0;");
            builder.AppendLine("    uint count, stride;");
            builder.AppendLine("    data.GetDimensions(count, stride);");
            builder.AppendLine($"    if (index >= count / {layout.Stride / 4}u) return value;");
            foreach (VdMirStorageField field in layout.Fields)
            {
                string destination = field.Path.Length == 0 ? "value" : "value." + field.Path;
                string source = $"data[index * {layout.Stride / 4}u + {field.Offset / 4}u]";
                if (field.Type == "f32") source = "asfloat(" + source + ")";
                builder.AppendLine($"    {destination} = {source};");
            }
            builder.AppendLine("    return value;");
            builder.AppendLine("}");
        }
    }

    private static string GraphicsStorageLoadName(string type, VdMirGraphicsModule module)
        => GeneratedName("VtsGraphicsLoad_" + type, module);
}
