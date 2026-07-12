using Machina.Fonts.ReferenceRendering;

namespace Machina.Fonts.Tooling;

public static class LayerCompositor
{
    public static RgbaImage Compose(DiagnosticLayerComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);
        IReadOnlyList<DiagnosticLayer> orderedLayers = composition.GetOrderedLayers();
        RgbaImage output = DiagnosticDrawing.CreateFilledImage(composition.Width, composition.Height, composition.Background);

        foreach (DiagnosticLayer layer in orderedLayers)
        {
            if (!layer.Visible)
            {
                continue;
            }

            switch (layer)
            {
                case DiagnosticImageLayer imageLayer:
                    ComposeImageLayer(output, imageLayer);
                    break;

                case DiagnosticMaskLayer maskLayer:
                    ComposeMaskLayer(output, maskLayer);
                    break;

                case DiagnosticBoundsLayer boundsLayer:
                    ComposeBoundsLayer(output, boundsLayer);
                    break;

                case DiagnosticGridLayer gridLayer:
                    ComposeGridLayer(output, gridLayer);
                    break;

                case DiagnosticAxisLayer axisLayer:
                    ComposeAxisLayer(output, axisLayer);
                    break;

                case DiagnosticBaselineLayer baselineLayer:
                    ComposeBaselineLayer(output, baselineLayer);
                    break;

                case DiagnosticTextLabelLayer textLabelLayer:
                    ComposeTextLabelLayer(output, textLabelLayer);
                    break;

                case DiagnosticDifferenceLayer differenceLayer:
                    ComposeDifferenceLayer(output, differenceLayer);
                    break;

                case DiagnosticGlyphWireframeLayer glyphWireframeLayer:
                    ComposeGlyphWireframeLayer(output, glyphWireframeLayer);
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported diagnostic layer type '{layer.GetType().Name}'.");
            }
        }

        return output;
    }

    private static void ComposeImageLayer(RgbaImage output, DiagnosticImageLayer layer)
    {
        if (layer.Image is null)
        {
            return;
        }

        ValidateSameSize(output, layer.Image);
        for (int index = 0; index < output.Pixels.Length; index++)
        {
            Rgba32 sourcePixel = layer.Image.Pixels[index];
            if (layer.TintColor is Rgba32 tint)
            {
                sourcePixel = DiagnosticDrawing.ApplyTint(sourcePixel, tint, layer.Opacity);
                output.Pixels[index] = sourcePixel;
                continue;
            }

            output.Pixels[index] = DiagnosticDrawing.Blend(output.Pixels[index], sourcePixel, layer.Opacity);
        }
    }

    private static void ComposeMaskLayer(RgbaImage output, DiagnosticMaskLayer layer)
    {
        if (layer.Mask is null)
        {
            return;
        }

        if (output.Width != layer.Mask.Width || output.Height != layer.Mask.Height)
        {
            throw new InvalidOperationException("Mask layer dimensions must match the composition.");
        }

        for (int y = 0; y < output.Height; y++)
        {
            for (int x = 0; x < output.Width; x++)
            {
                float coverage = layer.Mask.GetCoverage(x, y);
                if (coverage <= 0f)
                {
                    continue;
                }

                DiagnosticDrawing.BlendPixel(output, x, y, layer.Color, layer.Opacity * coverage);
            }
        }
    }

    private static void ComposeBoundsLayer(RgbaImage output, DiagnosticBoundsLayer layer)
    {
        foreach (DiagnosticBoundsItem item in layer.Items)
        {
            DiagnosticDrawing.DrawRectangle(output, item.Bounds, item.StrokeColor, layer.Opacity);
            if (item.ShowLabel && item.Bounds is not null)
            {
                DiagnosticDrawing.DrawLabel(output, item.Bounds.Left + 1, Math.Max(0, item.Bounds.Top - 6), item.Label, item.StrokeColor, layer.Opacity);
            }
        }
    }

    private static void ComposeGridLayer(RgbaImage output, DiagnosticGridLayer layer)
    {
        for (int x = 0; x < output.Width; x += layer.GridStep)
        {
            Rgba32 color = x % layer.MajorStep == 0
                ? layer.MajorGridColor
                : layer.GridColor;
            DiagnosticDrawing.DrawVerticalSegment(output, x, 0, output.Height - 1, color, layer.Opacity);
        }

        for (int y = 0; y < output.Height; y += layer.GridStep)
        {
            Rgba32 color = y % layer.MajorStep == 0
                ? layer.MajorGridColor
                : layer.GridColor;
            DiagnosticDrawing.DrawHorizontalSegment(output, 0, output.Width - 1, y, color, layer.Opacity);
        }

        if (layer.ShowUnitLabels)
        {
            for (int x = 0; x < output.Width; x += layer.MajorStep)
            {
                DiagnosticDrawing.DrawLabel(output, x + 1, 1, x.ToString(), layer.LabelColor, layer.Opacity);
            }

            for (int y = 0; y < output.Height; y += layer.MajorStep)
            {
                DiagnosticDrawing.DrawLabel(output, 1, y + 1, y.ToString(), layer.LabelColor, layer.Opacity);
            }
        }
    }

    private static void ComposeAxisLayer(RgbaImage output, DiagnosticAxisLayer layer)
    {
        if (layer.ShowXAxis)
        {
            DiagnosticDrawing.DrawHorizontalSegment(output, 0, output.Width - 1, 0, layer.AxisColor, layer.Opacity);
        }

        if (layer.ShowYAxis)
        {
            DiagnosticDrawing.DrawVerticalSegment(output, 0, 0, output.Height - 1, layer.AxisColor, layer.Opacity);
        }

        if (layer.ShowXAxis)
        {
            for (int x = 0; x < output.Width; x += layer.TickStep)
            {
                DiagnosticDrawing.DrawVerticalSegment(output, x, 0, Math.Min(4, output.Height - 1), layer.AxisColor, layer.Opacity);
            }
        }

        if (layer.ShowYAxis)
        {
            for (int y = 0; y < output.Height; y += layer.TickStep)
            {
                DiagnosticDrawing.DrawHorizontalSegment(output, 0, Math.Min(4, output.Width - 1), y, layer.AxisColor, layer.Opacity);
            }
        }

        if (layer.ShowOriginMarker)
        {
            DiagnosticDrawing.DrawHorizontalSegment(output, 0, Math.Min(4, output.Width - 1), 0, layer.AxisColor, layer.Opacity);
            DiagnosticDrawing.DrawVerticalSegment(output, 0, 0, Math.Min(4, output.Height - 1), layer.AxisColor, layer.Opacity);
        }
    }

    private static void ComposeBaselineLayer(RgbaImage output, DiagnosticBaselineLayer layer)
    {
        DiagnosticDrawing.DrawHorizontalSegment(output, 0, output.Width - 1, layer.BaselineY, layer.BaselineColor, layer.Opacity);
    }

    private static void ComposeTextLabelLayer(RgbaImage output, DiagnosticTextLabelLayer layer)
    {
        foreach (DiagnosticTextLabel label in layer.Labels)
        {
            DiagnosticDrawing.DrawLabel(output, label.X, label.Y, label.Text, label.Color, layer.Opacity);
        }
    }

    private static void ComposeDifferenceLayer(RgbaImage output, DiagnosticDifferenceLayer layer)
    {
        switch (layer.Mode)
        {
            case DiagnosticDifferenceMode.PairwiseMaskOverlay:
                ComposePairwiseDifference(output, layer);
                break;

            case DiagnosticDifferenceMode.ThreeWayMaskOverlay:
                ComposeThreeWayDifference(output, layer);
                break;

            default:
                throw new InvalidOperationException($"Unsupported difference mode '{layer.Mode}'.");
        }
    }

    private static void ComposePairwiseDifference(RgbaImage output, DiagnosticDifferenceLayer layer)
    {
        if (layer.LeftMask is null || layer.RightMask is null)
        {
            return;
        }

        ValidateSameSize(output, layer.LeftMask.Width, layer.LeftMask.Height);
        ValidateSameSize(output, layer.RightMask.Width, layer.RightMask.Height);

        for (int y = 0; y < output.Height; y++)
        {
            for (int x = 0; x < output.Width; x++)
            {
                bool leftInk = layer.LeftMask.IsInk(x, y);
                bool rightInk = layer.RightMask.IsInk(x, y);

                if (!leftInk && !rightInk)
                {
                    continue;
                }

                Rgba32 color = leftInk && rightInk
                    ? layer.OverlapColor
                    : leftInk
                        ? layer.LeftColor
                        : layer.RightColor;
                DiagnosticDrawing.BlendPixel(output, x, y, color, layer.Opacity);
            }
        }

        if (layer.BaselineColor is Rgba32 baselineColor)
        {
            DiagnosticDrawing.DrawHorizontalSegment(output, 0, output.Width - 1, (int)Math.Round(layer.BaselineY, MidpointRounding.AwayFromZero), baselineColor, layer.Opacity);
        }
    }

    private static void ComposeThreeWayDifference(RgbaImage output, DiagnosticDifferenceLayer layer)
    {
        if (layer.LeftMask is null || layer.RightMask is null || layer.ThirdMask is null || layer.ThirdColor is null)
        {
            return;
        }

        ValidateSameSize(output, layer.LeftMask.Width, layer.LeftMask.Height);
        ValidateSameSize(output, layer.RightMask.Width, layer.RightMask.Height);
        ValidateSameSize(output, layer.ThirdMask.Width, layer.ThirdMask.Height);

        for (int y = 0; y < output.Height; y++)
        {
            for (int x = 0; x < output.Width; x++)
            {
                bool leftInk = layer.LeftMask.IsInk(x, y);
                bool rightInk = layer.RightMask.IsInk(x, y);
                bool thirdInk = layer.ThirdMask.IsInk(x, y);
                int count = (leftInk ? 1 : 0) + (rightInk ? 1 : 0) + (thirdInk ? 1 : 0);

                if (count == 0)
                {
                    continue;
                }

                Rgba32 color = count > 1
                    ? layer.OverlapColor
                    : leftInk
                        ? layer.LeftColor
                        : rightInk
                            ? layer.RightColor
                            : layer.ThirdColor.Value;

                DiagnosticDrawing.BlendPixel(output, x, y, color, layer.Opacity);
            }
        }

        if (layer.BaselineColor is Rgba32 baselineColor)
        {
            DiagnosticDrawing.DrawHorizontalSegment(output, 0, output.Width - 1, (int)Math.Round(layer.BaselineY, MidpointRounding.AwayFromZero), baselineColor, layer.Opacity);
        }
    }

    private static void ComposeGlyphWireframeLayer(RgbaImage output, DiagnosticGlyphWireframeLayer layer)
    {
        foreach (FontDiagnosticBounds bounds in layer.Bounds)
        {
            DiagnosticDrawing.DrawRectangle(output, bounds, layer.StrokeColor, layer.Opacity);
        }
    }

    private static void ValidateSameSize(RgbaImage output, RgbaImage layerImage)
    {
        ValidateSameSize(output, layerImage.Width, layerImage.Height);
    }

    private static void ValidateSameSize(RgbaImage output, int width, int height)
    {
        if (output.Width != width || output.Height != height)
        {
            throw new InvalidOperationException("Diagnostic layer dimensions must match the composition.");
        }
    }
}
