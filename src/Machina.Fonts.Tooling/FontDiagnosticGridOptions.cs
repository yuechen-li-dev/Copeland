using Machina.Fonts.ReferenceRendering;

namespace Machina.Fonts.Tooling;

public sealed record FontDiagnosticGridOptions
{
    public bool ShowGrid { get; init; } = true;

    public int GridStep { get; init; } = 8;

    public bool ShowUnitLabels { get; init; } = true;

    public bool ShowAxes { get; init; } = true;

    public int AxisStep { get; init; } = 32;

    public bool ShowBaseline { get; init; } = true;

    public int BaselineY { get; init; }

    public bool ShowOriginMarker { get; init; } = true;

    public Rgba32 GridColor { get; init; } = new(54, 60, 72, 255);

    public Rgba32 MajorGridColor { get; init; } = new(82, 88, 100, 255);

    public Rgba32 AxisColor { get; init; } = new(156, 162, 176, 255);

    public Rgba32 BaselineColor { get; init; } = new(255, 64, 64, 255);

    public Rgba32 LabelColor { get; init; } = new(210, 216, 228, 255);

    public FontDiagnosticGridOptions Validate()
    {
        if (GridStep <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(GridStep));
        }

        if (AxisStep <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(AxisStep));
        }

        return this;
    }
}
