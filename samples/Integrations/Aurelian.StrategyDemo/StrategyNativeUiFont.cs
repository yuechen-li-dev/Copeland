using Aurelian.Machina;
using Machina.Core.Measurement;
using Machina.Core.Styling;
using Machina.Presentation;

namespace Aurelian.StrategyDemo;

/// <summary>Compatibility facade over the engine-owned native UI font realization.</summary>
public sealed class StrategyNativeUiFont : ITextMeasurer
{
    public const int MinimumFieldDimension = AurelianNativeUiFont.MinimumFieldDimension;
    public const double FieldPixelRange = AurelianNativeUiFont.FieldPixelRange;
    private readonly AurelianNativeUiFont font;

    private StrategyNativeUiFont(AurelianNativeUiFont font)
    {
        this.font = font;
    }

    public IReadOnlyCollection<AurelianMsdfAtlasResource> Resources => font.Resources;

    public static StrategyNativeUiFont Create(string assetDirectory)
    {
        return new StrategyNativeUiFont(AurelianNativeUiFont.Create(assetDirectory));
    }

    public IntrinsicSize MeasureText(string value, TextStyle style) => font.MeasureText(value, style);

    public PositionedTextOperation Qualify(PositionedTextOperation operation) => font.Qualify(operation);

    public AurelianMsdfAtlasResource ResourceFor(PositionedTextOperation operation) => font.ResourceFor(operation);
}
