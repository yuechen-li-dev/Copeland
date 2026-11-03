using Copeland.TS.Gpu.VdMir;

namespace Copeland.TS.Gpu;

/// <summary>One word-addressed transport layout authority for graphics and compute storage resources.</summary>
internal static class GpuStorageTransport
{
    public static VdMirStorageLayout? Describe(string type, GpuValues values)
    {
        var fields = new List<VdMirStorageField>();
        bool valid = Flatten(type, "");
        return valid && fields.Count > 0 ? new(type, fields.Count * 4, fields) : null;

        bool Flatten(string fieldType, string path)
        {
            if (fieldType is "f32" or "u32")
            {
                fields.Add(new(path, fieldType, fields.Count * 4));
                return true;
            }
            int width = fieldType switch { "float2" => 2, "float3" => 3, "float4" => 4, _ => 0 };
            if (width > 0)
            {
                foreach (char lane in "xyzw"[..width]) Flatten("f32", Join(path, lane.ToString()));
                return true;
            }
            if (values.Find(fieldType) is { } aggregate)
            {
                return aggregate.Fields.All(field => Flatten(field.Type, Join(path, field.Name)));
            }
            return false;
        }
    }

    private static string Join(string path, string field) => path.Length == 0 ? field : path + "." + field;
}
