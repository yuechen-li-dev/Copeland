using Machina.Core.Measurement;

namespace Machina.Core.Lowering;

public sealed record UiLoweringOptions(
    ITextMeasurer? TextMeasurer = null)
{
    internal ITextMeasurer EffectiveTextMeasurer => TextMeasurer ?? DeterministicTextMeasurer.Instance;
}
