using System.Globalization;
using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Syntax;

namespace Copeland.TS.Gpu;

public static partial class GpuComputeBinder
{
    private sealed partial class Binder
    {
        private static bool IsSampledResource(string type) => type is "Texture2D<float4>" or "Sampler"
            || type.StartsWith("descriptor-array<", StringComparison.Ordinal);

        private static int? SampledCount(string type) => type.StartsWith("descriptor-array<", StringComparison.Ordinal)
            ? int.Parse(type[(type.LastIndexOf(',') + 1)..^1], CultureInfo.InvariantCulture) : null;

        private static string SampledElement(string type) => SampledCount(type) is null ? type : type[17..type.LastIndexOf(',')];

        private string BindSampledType(string path, GenericTypeSyntax syntax)
        {
            if (syntax.Identifier.Text == "Texture2D" && syntax.TypeArguments.Count == 1
                && BindType(path, syntax.TypeArguments[0], syntax.Identifier) == "float4")
            {
                return "Texture2D<float4>";
            }
            if (syntax.Identifier.Text == "DescriptorArray" && syntax.TypeArguments.Count == 2
                && syntax.TypeArguments[1] is LiteralTypeSyntax { LiteralToken.Value: int count } && count is >= 1 and <= 4096)
            {
                string element = BindType(path, syntax.TypeArguments[0], syntax.Identifier);
                if (element is "Texture2D<float4>" or "Sampler") return $"descriptor-array<{element},{count}>";
            }
            AddDiagnostic("COPE-GPU-TABLE-0001", "SDSL-V4600", "descriptor-array",
                "Compute sampled resources require Texture2D<float4>, Sampler, or DescriptorArray of either with literal capacity 1..4096.", Span(path, syntax));
            return "error";
        }

        private VdMirExpression BindSampledIndex(string path, IndexExpressionSyntax syntax, VdMirExpression target, VdMirExpression index)
        {
            if (index.Type != "u32" || index.Kind != "literal" && !(index.Kind == "intrinsic" && index.Value == "NonUniformIndex"))
            {
                AddDiagnostic("COPE-GPU-TABLE-0003", "SDSL-V4602", "descriptor-index",
                    "Descriptor indexing needs a literal u32 or explicit NonUniform(u32).", Span(path, syntax));
                return ErrorExpression(path, syntax);
            }
            if (index.Kind == "literal" && uint.TryParse(index.Value, out uint value) && value >= SampledCount(target.Type))
            {
                AddDiagnostic("COPE-GPU-TABLE-0004", "SDSL-V4603", "descriptor-index", "Descriptor index exceeds its declared capacity.", Span(path, syntax));
                return ErrorExpression(path, syntax);
            }
            return new("descriptor-index", SampledElement(target.Type), Span(path, syntax), Operands: [target, index]);
        }

        private VdMirExpression? BindSampledCall(string path, CallExpressionSyntax call, Dictionary<string, ValueBinding> scope)
        {
            if (call.Target is not NameExpressionSyntax name || name.IdentifierToken.Text is not ("SampleLevel" or "NonUniform" or "Sample")) return null;
            var arguments = call.Arguments.Select(argument => BindExpression(path, argument, scope)).ToArray();
            if (name.IdentifierToken.Text == "NonUniform")
            {
                if (arguments.Length == 1 && arguments[0].Type == "u32")
                    return new("intrinsic", "u32", Span(path, call), "NonUniformIndex", arguments);
                AddDiagnostic("COPE-GPU-TABLE-0003", "SDSL-V4602", "descriptor-index", "NonUniform requires one u32 index.", Span(path, call));
            }
            else if (name.IdentifierToken.Text == "SampleLevel" && arguments.Length == 4
                && arguments[0].Type == "Texture2D<float4>" && arguments[1].Type == "Sampler"
                && arguments[2].Type == "float2" && arguments[3].Type == "f32")
            {
                return new("intrinsic", "float4", Span(path, call), "SampleLevel2D", arguments);
            }
            else
            {
                AddDiagnostic("COPE-GPU-SAMPLE-0001", "SDSL-V4118", "texture-sampling",
                    "Compute sampling requires SampleLevel(Texture2D<float4>, Sampler, float2, f32); implicit derivatives are not available.", Span(path, call));
            }
            return ErrorExpression(path, call);
        }
    }
}
