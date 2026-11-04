using System.Numerics;
using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.Graphics.Vulkan.Native3D;

internal static class LocalLightPacking3D
{
    internal static void Write(LocalLight3D light, Span<float> words)
    {
        Vector3 direction = Vector3.Normalize(light.Direction);
        words[0] = light.Position.X;
        words[1] = light.Position.Y;
        words[2] = light.Position.Z;
        words[3] = light.Range;
        words[4] = light.Color.X;
        words[5] = light.Color.Y;
        words[6] = light.Color.Z;
        words[7] = light.Intensity;
        words[8] = direction.X;
        words[9] = direction.Y;
        words[10] = direction.Z;
        words[11] = MathF.Cos(light.OuterAngle);
        words[12] = MathF.Cos(light.InnerAngle);
        words[13] = light.Kind == LocalLightKind.Spot ? 1 : 0;
        words[14] = light.CastShadows ? 1 : 0;
        words[15] = 0;
    }
}
