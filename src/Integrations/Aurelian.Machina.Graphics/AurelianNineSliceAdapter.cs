using Aurelian.Graphics.Vulkan.Native2D;
using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Presentation;

namespace Aurelian.Machina.Graphics;

/// <summary>Maps renderer-neutral Machina nine-slice quads to the native ordered-quad contract.</summary>
public static class AurelianNineSliceAdapter
{
    public static IReadOnlyList<NativeQuadSubmission> Lower(
        MachinaNineSlicePrimitive primitive,
        Native2DTextureHandle texture,
        int atlasWidth,
        int atlasHeight,
        MachinaViewportTransform viewport)
    {
        ArgumentNullException.ThrowIfNull(primitive);
        if (atlasWidth <= 0 || atlasHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(atlasWidth), "Atlas dimensions must be positive.");
        }

        var result = new List<NativeQuadSubmission>();
        foreach (MachinaNineSliceQuad quad in MachinaNineSliceLowerer.Lower(primitive))
        {
            Rect physical = viewport.ToPhysical(quad.DestinationRect);
            result.Add(new NativeQuadSubmission(
                new Native2DRect(
                    (float)physical.X,
                    (float)physical.Y,
                    (float)physical.Width,
                    (float)physical.Height),
                ToInsetUv(quad.SourceRect, atlasWidth, atlasHeight),
                texture,
                ToTint(primitive.Tint)));
        }

        return result;
    }

    /// <summary>
    /// Samples from boundary texel centers. Repeated atlas subrects therefore use Clamp and
    /// repeated quads instead of hardware Repeat, avoiding neighboring-atlas bleed.
    /// </summary>
    public static Native2DUvRect ToInsetUv(Rect source, int atlasWidth, int atlasHeight)
    {
        if (source.Width <= 0 || source.Height <= 0
            || source.X < 0 || source.Y < 0
            || source.X + source.Width > atlasWidth
            || source.Y + source.Height > atlasHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(source), "Source rectangle must be positive and inside the atlas.");
        }

        double halfTexelX = Math.Min(0.5, source.Width / 2);
        double halfTexelY = Math.Min(0.5, source.Height / 2);
        return new Native2DUvRect(
            (float)((source.X + halfTexelX) / atlasWidth),
            (float)((source.Y + halfTexelY) / atlasHeight),
            (float)((source.X + source.Width - halfTexelX) / atlasWidth),
            (float)((source.Y + source.Height - halfTexelY) / atlasHeight));
    }

    private static Native2DTint ToTint(ColorToken color)
    {
        const float denominator = 255f;
        return new Native2DTint(
            (byte)(color.Rgba >> 24) / denominator,
            (byte)(color.Rgba >> 16) / denominator,
            (byte)(color.Rgba >> 8) / denominator,
            (byte)color.Rgba / denominator);
    }
}
