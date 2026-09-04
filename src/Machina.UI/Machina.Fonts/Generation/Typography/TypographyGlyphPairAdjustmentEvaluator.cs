using Typography.OpenFont;
using Typography.OpenFont.Tables;

namespace Machina.Fonts.Generation.Typography;

internal static class TypographyGlyphPairAdjustmentEvaluator
{
    public static GlyphPairAdjustment? Evaluate(
        Typeface typeface,
        GlyphKey left,
        GlyphKey right)
    {
        ArgumentNullException.ThrowIfNull(typeface);

        if (left.Face != right.Face
            || left.EmSize != right.EmSize
            || left.Weight != right.Weight
            || left.Slant != right.Slant)
        {
            return null;
        }

        ushort leftGlyphIndex = typeface.GetGlyphIndex(left.Codepoint);
        ushort rightGlyphIndex = typeface.GetGlyphIndex(right.Codepoint);
        if (leftGlyphIndex == 0 || rightGlyphIndex == 0)
        {
            return null;
        }

        double scale = left.EmSize / typeface.UnitsPerEm;
        if (!double.IsFinite(scale) || scale <= 0d)
        {
            return null;
        }

        PairProbeGlyphPositions probe = new(leftGlyphIndex, rightGlyphIndex);

        bool appliedGposPairAdjustment = false;
        if (typeface.GPOSTable is GPOS gpos)
        {
            foreach (GPOS.LookupTable lookup in gpos.LookupList)
            {
                if (!ContainsPairPositioning(lookup))
                {
                    continue;
                }

                lookup.DoGlyphPosition(probe, 0, 1);
                appliedGposPairAdjustment = true;
            }
        }

        if (appliedGposPairAdjustment)
        {
            return CreateAdjustment(scale, probe.XAdvance0, probe.YAdvance0);
        }

        try
        {
            short distance = typeface.GetKernDistance(leftGlyphIndex, rightGlyphIndex);
            return CreateAdjustment(scale, distance, 0);
        }
        catch (NullReferenceException)
        {
            return null;
        }

    }

    private static bool ContainsPairPositioning(GPOS.LookupTable lookup)
    {
        foreach (GPOS.LookupSubTable subTable in lookup.SubTables)
        {
            string typeName = subTable.GetType().Name;
            if (typeName is "LkSubTableType2Fmt1" or "LkSubTableType2Fmt2")
            {
                return true;
            }
        }

        return false;
    }

    private static GlyphPairAdjustment? CreateAdjustment(double scale, short advanceX, short advanceY)
    {
        double scaledAdvanceX = advanceX * scale;
        double scaledAdvanceY = advanceY * scale;

        if (scaledAdvanceX == 0d && scaledAdvanceY == 0d)
        {
            return null;
        }

        return new GlyphPairAdjustment(scaledAdvanceX, scaledAdvanceY);
    }

    private sealed class PairProbeGlyphPositions : IGlyphPositions
    {
        private readonly ushort[] glyphIndices;

        public PairProbeGlyphPositions(ushort leftGlyphIndex, ushort rightGlyphIndex)
        {
            glyphIndices = [leftGlyphIndex, rightGlyphIndex];
        }

        public int Count => glyphIndices.Length;

        public short XAdvance0 { get; private set; }

        public short YAdvance0 { get; private set; }

        public void AppendGlyphAdvance(int index, short dx, short dy)
        {
            if (index == 0)
            {
                XAdvance0 += dx;
                YAdvance0 += dy;
            }
        }

        public void AppendGlyphOffset(int index, short dx, short dy)
        {
        }

        public ushort GetGlyph(int index, out short advanceWidth)
        {
            advanceWidth = index == 0 ? XAdvance0 : (short)0;
            return glyphIndices[index];
        }

        public ushort GetGlyph(
            int index,
            out ushort inputOffset,
            out short offsetX,
            out short offsetY,
            out short advanceWidth)
        {
            inputOffset = checked((ushort)index);
            offsetX = 0;
            offsetY = 0;
            advanceWidth = index == 0 ? XAdvance0 : (short)0;
            return glyphIndices[index];
        }

        public GlyphClassKind GetGlyphClassKind(int index)
        {
            return GlyphClassKind.Base;
        }

        public void GetOffset(int index, out short offsetX, out short offsetY)
        {
            offsetX = 0;
            offsetY = 0;
        }
    }
}
