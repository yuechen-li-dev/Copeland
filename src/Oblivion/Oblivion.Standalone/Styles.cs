using Machina.Core.Styling;

namespace Oblivion.Standalone;

public sealed record OblivionStandaloneStyle(
    int DevelopmentWidth,
    int DevelopmentHeight,
    double OuterHorizontalMargin,
    double OuterVerticalMargin,
    double StackGap,
    double CollapsedCardHeight,
    double ExpandedCardHeight,
    double MaximumReadableWidth,
    string CardSubtitle,
    ColorToken PageBackground,
    ColorToken CardBackground,
    ColorToken CardBorder,
    ColorToken SelectedCardBorder);

public static class OblivionStandaloneStyles
{
    public static OblivionStandaloneStyle M19h { get; } = new(
        DevelopmentWidth: 2560,
        DevelopmentHeight: 1440,
        OuterHorizontalMargin: 88,
        OuterVerticalMargin: 72,
        StackGap: 24,
        CollapsedCardHeight: 174,
        ExpandedCardHeight: 760,
        MaximumReadableWidth: 1040,
        CardSubtitle: "A standalone technical reading surface",
        PageBackground: ColorToken.Hex(0x050914FF),
        CardBackground: ColorToken.Hex(0x0B1220FF),
        CardBorder: ColorToken.Hex(0x334155FF),
        SelectedCardBorder: ColorToken.Hex(0x2563EBFF));
}
