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
        ArgumentNullException.ThrowIfNull(primitive);
        Rect source = primitive.SourceRect;
        Rect destination = primitive.DestinationRect;
        MachinaSliceMargins margins = primitive.Margins;

        (double destinationLeft, double destinationRight) = FitMargins(
            margins.Left * primitive.BorderScale,
            margins.Right * primitive.BorderScale,
            destination.Width);
        (double destinationTop, double destinationBottom) = FitMargins(
            margins.Top * primitive.BorderScale,
            margins.Bottom * primitive.BorderScale,
            destination.Height);

        double[] sourceX = [source.X, source.X + margins.Left, source.X + source.Width - margins.Right, source.X + source.Width];
        double[] sourceY = [source.Y, source.Y + margins.Top, source.Y + source.Height - margins.Bottom, source.Y + source.Height];
        double[] destinationX = [destination.X, destination.X + destinationLeft, destination.X + destination.Width - destinationRight, destination.X + destination.Width];
        double[] destinationY = [destination.Y, destination.Y + destinationTop, destination.Y + destination.Height - destinationBottom, destination.Y + destination.Height];

        var quads = new List<MachinaNineSliceQuad>();
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                Rect sourceRegion = FromEdges(sourceX[column], sourceY[row], sourceX[column + 1], sourceY[row + 1]);
                Rect destinationRegion = FromEdges(destinationX[column], destinationY[row], destinationX[column + 1], destinationY[row + 1]);
                if (sourceRegion.Width <= 0 || sourceRegion.Height <= 0
                    || destinationRegion.Width <= 0 || destinationRegion.Height <= 0)
                {
                    continue;
                }

                bool center = row == 1 && column == 1;
                bool horizontalEdge = row is 0 or 2 && column == 1;
                bool verticalEdge = column is 0 or 2 && row == 1;
                if (center && primitive.CenterMode == MachinaNineSliceMode.Tile)
                {
                    AddTiled(quads, destinationRegion, sourceRegion, tileX: true, tileY: true);
                }
                else if (horizontalEdge && primitive.EdgeMode == MachinaNineSliceMode.Tile)
                {
                    AddTiled(quads, destinationRegion, sourceRegion, tileX: true, tileY: false);
                }
                else if (verticalEdge && primitive.EdgeMode == MachinaNineSliceMode.Tile)
                {
                    AddTiled(quads, destinationRegion, sourceRegion, tileX: false, tileY: true);
                }
                else
                {
                    quads.Add(new MachinaNineSliceQuad(destinationRegion, sourceRegion));
                }
            }
        }

        return quads;
    }

    private static void AddTiled(
        ICollection<MachinaNineSliceQuad> quads,
        Rect destination,
        Rect source,
        bool tileX,
        bool tileY)
    {
        double tileWidth = tileX ? source.Width : destination.Width;
        double tileHeight = tileY ? source.Height : destination.Height;
        for (double y = 0; y < destination.Height - 0.000001; y += tileHeight)
        {
            double height = Math.Min(tileHeight, destination.Height - y);
            double sourceHeight = tileY ? source.Height * (height / tileHeight) : source.Height;
            for (double x = 0; x < destination.Width - 0.000001; x += tileWidth)
            {
                double width = Math.Min(tileWidth, destination.Width - x);
                double sourceWidth = tileX ? source.Width * (width / tileWidth) : source.Width;
                quads.Add(new MachinaNineSliceQuad(
                    new Rect(destination.X + x, destination.Y + y, width, height),
                    new Rect(source.X, source.Y, sourceWidth, sourceHeight)));
            }
        }
    }

    private static (double First, double Second) FitMargins(double first, double second, double available)
    {
        double total = first + second;
        if (total <= available || total == 0)
        {
            return (first, second);
        }

        double scale = available / total;
        return (first * scale, second * scale);
    }

    private static Rect FromEdges(double left, double top, double right, double bottom)
    {
        return new Rect(left, top, right - left, bottom - top);
    }
}
