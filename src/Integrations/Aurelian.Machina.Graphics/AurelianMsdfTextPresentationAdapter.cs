using Aurelian.Graphics.Vulkan.Native2D;
using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Presentation;

namespace Aurelian.Machina;

/// <summary>
/// Realizes Machina-owned glyph placement through M2's native glyph-quad adapter.
/// It performs no font parsing, shaping, measurement, or layout.
/// </summary>
public static class AurelianMsdfTextPresentationAdapter
{
    public static IReadOnlyList<NativeMsdfQuadSubmission> Adapt(
        PositionedTextOperation operation,
        AurelianMsdfAtlasResource atlas,
        AurelianMsdfAtlasCache cache,
        Rect? clipRect = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(cache);

        MachinaTextPresentationPrimitive primitive = operation.Primitive
            ?? throw new InvalidOperationException($"Text operation '{operation.SourceId}' has no qualified glyph primitive.");
        if (primitive.RenderingMode != MachinaTextRenderingMode.Msdf)
        {
            throw new InvalidOperationException($"Text operation '{operation.SourceId}' is not in MSDF mode.");
        }
        if (primitive.AtlasIdentity != atlas.Identity)
        {
            throw new InvalidOperationException(
                $"Text operation '{operation.SourceId}' requests atlas '{primitive.AtlasIdentity}', not '{atlas.Identity}'.");
        }

        IReadOnlyDictionary<int, Native2DTextureHandle> textures = cache.Resolve(atlas);
        Native2DRect? nativeClip = clipRect is Rect clip
            ? new Native2DRect((float)clip.X, (float)clip.Y, (float)clip.Width, (float)clip.Height)
            : null;
        IReadOnlyList<NativeMsdfQuadSubmission> submissions = AurelianGlyphRunAdapter.Adapt(
            primitive.GlyphRun,
            atlas.Snapshot,
            textures,
            ToTint(operation.Color),
            nativeClip,
            (float)operation.Rect.X,
            (float)operation.Rect.Y);

        return submissions
            .Select(static submission => submission with
            {
                Uv = AurelianMsdfAtlasUpload.NormalizeUv(submission.Uv),
            })
            .ToArray();
    }

    private static Native2DTint ToTint(ColorToken color)
    {
        const float scale = 1f / 255f;
        return new Native2DTint(
            (byte)(color.Rgba >> 24) * scale,
            (byte)(color.Rgba >> 16) * scale,
            (byte)(color.Rgba >> 8) * scale,
            (byte)color.Rgba * scale);
    }
}
