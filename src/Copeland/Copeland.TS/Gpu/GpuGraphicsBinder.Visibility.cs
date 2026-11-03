using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Syntax;

namespace Copeland.TS.Gpu;

public static partial class GpuGraphicsBinder
{
    private sealed partial class Binder
    {
        private bool usesVisibilityImages;
        private VdMirExpression BindVisibilityImageOperation(string path, CallExpressionSyntax syntax,
            string name, VdMirExpression[] arguments)
        {
            usesVisibilityImages = true;
            bool load = name == "Load";
            string element = arguments.Length > 0 && arguments[0].Type.StartsWith("Texture2D<", StringComparison.Ordinal)
                ? arguments[0].Type[10..^1] : "error";
            bool valid = load
                ? arguments.Length == 2 && element is "uint2" or "u32" or "float4" && arguments[1].Type == "uint2"
                : arguments.Length == 5 && element == "float4" && arguments[1].Type == "Sampler"
                    && arguments.Skip(2).All(argument => argument.Type == "float2");
            if (!valid || _currentStage != VdMirGraphicsStage.Pixel)
            {
                Add("COPE-GPU-IMAGE-0001", "SDSL-V4604", "image-operation",
                    "Pixel Load requires Texture2D<float4/uint2/u32>, uint2 texel coordinates; SampleGrad requires a float4 texture, sampler, UV and two float2 gradients.",
                    Span(path, syntax));
                return Error(path, syntax);
            }
            return new("intrinsic", element, Span(path, syntax), load ? "Load2D" : "SampleGrad2D", arguments);
        }
    }
}
