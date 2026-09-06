using Machina.Core.Styling;
using Machina.Layout.Geometry;

namespace Machina.Presentation;

public enum MachinaNineSliceMode
{
    Stretch,
    Tile,
}

public readonly record struct MachinaTextureAssetId
{
    public MachinaTextureAssetId(string value)
    {
        Value = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Texture asset identity must not be empty.", nameof(value))
            : value;
    }

    public string Value { get; }
}

public readonly record struct MachinaSliceMargins(double Left, double Top, double Right, double Bottom)
{
    public void Validate(Rect sourceRect)
    {
        if (!double.IsFinite(Left) || !double.IsFinite(Top)
            || !double.IsFinite(Right) || !double.IsFinite(Bottom)
            || Left < 0 || Top < 0 || Right < 0 || Bottom < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MachinaSliceMargins), "Slice margins must be finite and non-negative.");
        }

        if (Left + Right > sourceRect.Width || Top + Bottom > sourceRect.Height)
        {
            throw new ArgumentException("Slice margins must fit inside the source rectangle.");
        }
    }
}

public sealed record MachinaNineSlicePrimitive : MachinaPresentationOperation
{
    public MachinaNineSlicePrimitive(
        string sourceId,
        MachinaTextureAssetId texture,
        Rect sourceRect,
        Rect destinationRect,
        MachinaSliceMargins margins,
        MachinaNineSliceMode edgeMode,
        MachinaNineSliceMode centerMode,
        double borderScale = 1,
        ColorToken? tint = null)
    {
        SourceId = MachinaPresentationValidation.ValidateSourceId(sourceId);
        SourceRect = MachinaPresentationValidation.ValidateRect(sourceRect, nameof(sourceRect));
        DestinationRect = MachinaPresentationValidation.ValidateRect(destinationRect, nameof(destinationRect));
        if (sourceRect.Width <= 0 || sourceRect.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceRect), "Nine-slice source dimensions must be positive.");
        }

        if (destinationRect.Width <= 0 || destinationRect.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(destinationRect), "Nine-slice destination dimensions must be positive.");
        }

        margins.Validate(sourceRect);
        if (!Enum.IsDefined(edgeMode) || !Enum.IsDefined(centerMode))
        {
            throw new ArgumentOutOfRangeException(nameof(edgeMode));
        }

        if (!double.IsFinite(borderScale) || borderScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(borderScale), "Border scale must be finite and positive.");
        }

        Texture = texture;
        Margins = margins;
        EdgeMode = edgeMode;
        CenterMode = centerMode;
        BorderScale = borderScale;
        Tint = tint ?? ColorToken.White;
    }

    public string SourceId { get; }
    public MachinaTextureAssetId Texture { get; }
    public Rect SourceRect { get; }
    public Rect DestinationRect { get; }
    public MachinaSliceMargins Margins { get; }
    public MachinaNineSliceMode EdgeMode { get; }
    public MachinaNineSliceMode CenterMode { get; }
    public double BorderScale { get; }
    public ColorToken Tint { get; }
}

public readonly record struct MachinaNineSliceQuad(Rect DestinationRect, Rect SourceRect);

public static class MachinaNineSliceLowerer
{
    public static IReadOnlyList<MachinaNineSliceQuad> Lower(MachinaNineSlicePrimitive primitive)
    {
        MachinaProgrammablePanelPrimitive panel = MachinaPanelPrebuilt.NineSlice(primitive);
        return MachinaProgrammablePanelLowerer.Lower(panel).Quads
            .Select(quad => new MachinaNineSliceQuad(quad.DestinationRect, quad.SourceRect))
            .ToArray();
    }
}
