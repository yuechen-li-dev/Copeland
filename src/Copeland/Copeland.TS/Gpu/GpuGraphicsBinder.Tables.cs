using System.Globalization;
using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Syntax;

namespace Copeland.TS.Gpu;

public static partial class GpuGraphicsBinder
{
    private sealed partial class Binder
    {
        private readonly Dictionary<string, VdMirStorageLayout> graphicsStorageLayouts = new(StringComparer.Ordinal);

        private string BindTableType(string path, GenericTypeSyntax syntax)
        {
            if (syntax.Identifier.Text == "DescriptorArray")
            {
                if (syntax.TypeArguments.Count != 2 || syntax.TypeArguments[1] is not LiteralTypeSyntax { LiteralToken.Value: int count }
                    || count is < 1 or > 4096)
                {
                    Add("COPE-GPU-TABLE-0001", "SDSL-V4600", "descriptor-array", "DescriptorArray needs a texture/sampler element and literal capacity 1..4096.", Span(path, syntax));
                    return "error";
                }
                string element = BindType(path, syntax.TypeArguments[0]);
                if (element is not ("Texture2D<float4>" or "Sampler"))
                {
                    Add("COPE-GPU-TABLE-0001", "SDSL-V4600", "descriptor-array", "Only sampled float4 textures and samplers are descriptor-array elements.", Span(path, syntax));
                    return "error";
                }
                return $"descriptor-array<{element},{count}>";
            }
            if (syntax.TypeArguments.Count != 1)
            {
                Add("COPE-GPU-TABLE-0002", "SDSL-V4601", "storage-buffer", "StorageBuffer needs one transport value type.", Span(path, syntax));
                return "error";
            }
            string type = BindType(path, syntax.TypeArguments[0]);
            if (!graphicsStorageLayouts.ContainsKey(type))
            {
                var layout = GpuStorageTransport.Describe(type, _values);
                if (layout is null)
                {
                    Add("COPE-GPU-TABLE-0002", "SDSL-V4601", "storage-buffer", "Graphics storage requires nonempty f32/u32/vector/record transport; bools and resources are not transport values.", Span(path, syntax));
                    return "error";
                }
                graphicsStorageLayouts.Add(type, layout);
            }
            return "storage-buffer<" + type + ">";
        }

        private VdMirExpression BindResourceIndex(string path, IndexExpressionSyntax syntax, Dictionary<string, string> scope)
        {
            var target = BindExpression(path, syntax.Target, scope);
            var index = BindExpression(path, syntax.Index, scope);
            if (target.Type.StartsWith("descriptor-array<", StringComparison.Ordinal))
            {
                if (index.Type != "u32" || index.Kind != "literal" && !(index.Kind == "intrinsic" && index.Value == "NonUniformIndex"))
                {
                    Add("COPE-GPU-TABLE-0003", "SDSL-V4602", "descriptor-index", "Descriptor indexing needs a literal u32 or an explicit NonUniform(u32) index.", Span(path, syntax));
                    return Error(path, syntax);
                }
                if (index.Kind == "literal" && uint.TryParse(index.Value, out uint value) && value >= DescriptorCount(target.Type))
                {
                    Add("COPE-GPU-TABLE-0004", "SDSL-V4603", "descriptor-index", "Descriptor index is outside the declared capacity.", Span(path, syntax));
                    return Error(path, syntax);
                }
                return new("descriptor-index", ArrayElement(target.Type), Span(path, syntax), Operands: [target, index]);
            }
            if (target.Type.StartsWith("storage-buffer<", StringComparison.Ordinal))
            {
                if (index.Type != "u32")
                {
                    Add("COPE-GPU-TABLE-0002", "SDSL-V4601", "storage-buffer", "Storage index must be u32.", Span(path, syntax));
                    return Error(path, syntax);
                }
                return new("graphics-storage-load", target.Type[15..^1], Span(path, syntax), Operands: [target, index]);
            }
            return _values.Read(target, [index], AddEnumFunction, Span(path, syntax)) ?? ValueOperationError(path, syntax);
        }

        private VdMirExpression BindNonUniform(string path, CallExpressionSyntax syntax, Dictionary<string, string> scope)
        {
            var operands = syntax.Arguments.Select(argument => BindExpression(path, argument, scope)).ToArray();
            if (operands.Length != 1 || operands[0].Type != "u32")
            {
                Add("COPE-GPU-TABLE-0003", "SDSL-V4602", "descriptor-index", "NonUniform requires one u32 index.", Span(path, syntax));
                return Error(path, syntax);
            }
            return new("intrinsic", "u32", Span(path, syntax), "NonUniformIndex", operands);
        }

        private static string ArrayElement(string type) => type[17..type.LastIndexOf(',')];
        private static int? DescriptorCount(string type) => type.StartsWith("descriptor-array<", StringComparison.Ordinal)
            ? int.Parse(type[(type.LastIndexOf(',') + 1)..^1], CultureInfo.InvariantCulture) : null;
        private static string? ResourceElement(string type, VdMirGraphicsResourceKind kind)
        {
            if (kind == VdMirGraphicsResourceKind.StorageBuffer) return type[15..^1];
            if (kind != VdMirGraphicsResourceKind.Texture2D) return null;
            return type.StartsWith("descriptor-array<", StringComparison.Ordinal) ? "float4" : type[10..^1];
        }
    }
}
