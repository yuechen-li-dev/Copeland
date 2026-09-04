using Aurelian.Graphics.Vulkan.Native2D;
using Machina.Fonts;
using Machina.Fonts.ReferenceRendering;

namespace Aurelian.Machina;

public static class AurelianGlyphRunAdapter
{
    public static IReadOnlyList<NativeMsdfQuadSubmission> Adapt(
        MachinaGlyphRun glyphRun,
        FontAtlasSnapshot atlas,
        IReadOnlyDictionary<int, Native2DTextureHandle> pageTextures,
        Native2DTint color,
        Native2DRect? clipRect = null,
        float destinationOffsetX = 0,
        float destinationOffsetY = 0)
    {
        ArgumentNullException.ThrowIfNull(glyphRun);
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(pageTextures);

        Dictionary<int, FontAtlasPage> pages = atlas.Pages.ToDictionary(page => page.Index);
        List<NativeMsdfQuadSubmission> submissions = new(glyphRun.Glyphs.Count);

        foreach (MachinaGlyphPlacement glyph in glyphRun.Glyphs)
        {
            if (glyph.IsWhitespace)
            {
                continue;
            }
            if (!atlas.Glyphs.TryGetValue(glyph.Key, out GlyphAtlasEntry? entry))
            {
                throw new InvalidOperationException($"Missing atlas entry for glyph U+{glyph.Key.Codepoint:X4}.");
            }
            if (!pages.TryGetValue(entry.PageIndex, out FontAtlasPage? page))
            {
                throw new InvalidOperationException($"Atlas entry for U+{glyph.Key.Codepoint:X4} references missing page {entry.PageIndex}.");
            }
            if (!pageTextures.TryGetValue(entry.PageIndex, out Native2DTextureHandle texture))
            {
                throw new InvalidOperationException($"No native texture exists for atlas page {entry.PageIndex}.");
            }

            ValidateEntry(entry, page);
            GlyphFieldPlacement field = entry.Placement;
            float destinationX = CheckedFloat(glyph.OriginX + field.PlaneLeft + destinationOffsetX, "destination X");
            float destinationY = CheckedFloat(glyph.BaselineY + field.PlaneTop + destinationOffsetY, "destination Y");
            float destinationWidth = CheckedPositiveFloat(field.Width, "destination width");
            float destinationHeight = CheckedPositiveFloat(field.Height, "destination height");
            float fieldScale = Math.Min(destinationWidth / entry.Width, destinationHeight / entry.Height);

            var submission = new NativeMsdfQuadSubmission(
                new Native2DRect(destinationX, destinationY, destinationWidth, destinationHeight),
                new Native2DUvRect(
                    CheckedFloat(entry.U0, "u0"),
                    CheckedFloat(entry.V0, "v0"),
                    CheckedFloat(entry.U1, "u1"),
                    CheckedFloat(entry.V1, "v1")),
                texture,
                color,
                new NativeMsdfParameters(
                    CheckedPositiveFloat(field.PixelRange, "pixel range"),
                    CheckedPositiveFloat(fieldScale, "field scale"),
                    0.5f));

            if (clipRect is Native2DRect clip && !TryClip(submission, clip, out submission))
            {
                continue;
            }

            submissions.Add(submission);
        }

        return submissions;
    }

    private static bool TryClip(
        NativeMsdfQuadSubmission source,
        Native2DRect clip,
        out NativeMsdfQuadSubmission clipped)
    {
        float left = Math.Max(source.Destination.X, clip.X);
        float top = Math.Max(source.Destination.Y, clip.Y);
        float right = Math.Min(source.Destination.X + source.Destination.Width, clip.X + clip.Width);
        float bottom = Math.Min(source.Destination.Y + source.Destination.Height, clip.Y + clip.Height);
        if (right <= left || bottom <= top)
        {
            clipped = default;
            return false;
        }

        float uScale = (source.Uv.U1 - source.Uv.U0) / source.Destination.Width;
        float vScale = (source.Uv.V1 - source.Uv.V0) / source.Destination.Height;
        clipped = source with
        {
            Destination = new Native2DRect(left, top, right - left, bottom - top),
            Uv = new Native2DUvRect(
                source.Uv.U0 + ((left - source.Destination.X) * uScale),
                source.Uv.V0 + ((top - source.Destination.Y) * vScale),
                source.Uv.U1 - (((source.Destination.X + source.Destination.Width) - right) * uScale),
                source.Uv.V1 - (((source.Destination.Y + source.Destination.Height) - bottom) * vScale)),
        };
        return true;
    }

    private static void ValidateEntry(GlyphAtlasEntry entry, FontAtlasPage page)
    {
        if (entry.X < 0
            || entry.Y < 0
            || entry.X + entry.Width > page.Width
            || entry.Y + entry.Height > page.Height)
        {
            throw new InvalidOperationException($"Atlas entry for U+{entry.Key.Codepoint:X4} lies outside page {page.Index}.");
        }
        if (entry.U0 < 0d
            || entry.V0 < 0d
            || entry.U1 > 1d
            || entry.V1 > 1d
            || entry.U0 > entry.U1
            || entry.V0 > entry.V1)
        {
            throw new InvalidOperationException($"Atlas UVs for U+{entry.Key.Codepoint:X4} are outside normalized page bounds.");
        }

        double expectedU0 = entry.X / (double)page.Width;
        double expectedV0 = entry.Y / (double)page.Height;
        double expectedU1 = (entry.X + entry.Width) / (double)page.Width;
        double expectedV1 = (entry.Y + entry.Height) / (double)page.Height;
        const double tolerance = 1e-12;
        if (Math.Abs(entry.U0 - expectedU0) > tolerance
            || Math.Abs(entry.V0 - expectedV0) > tolerance
            || Math.Abs(entry.U1 - expectedU1) > tolerance
            || Math.Abs(entry.V1 - expectedV1) > tolerance)
        {
            throw new InvalidOperationException($"Atlas UVs for U+{entry.Key.Codepoint:X4} do not map its storage rectangle.");
        }
    }

    private static float CheckedPositiveFloat(double value, string name)
    {
        float result = CheckedFloat(value, name);
        if (result <= 0f)
        {
            throw new InvalidOperationException($"Glyph {name} must be positive.");
        }
        return result;
    }

    private static float CheckedFloat(double value, string name)
    {
        float result = (float)value;
        if (!float.IsFinite(result))
        {
            throw new InvalidOperationException($"Glyph {name} must be finite and representable as f32.");
        }
        return result;
    }
}
