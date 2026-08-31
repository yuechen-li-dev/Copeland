using Machina.Core.Styling;
using Oblivion.App;

namespace Oblivion.Standalone;

public sealed record OblivionStandaloneStyle(
    OblivionResolvedAppearance Appearance,
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
    ColorToken SelectedCardBorder,
    ColorToken PrimaryText,
    ColorToken SecondaryText,
    ColorToken BadgeSurface,
    ColorToken BadgeText,
    ColorToken BadgeBorder,
    ColorToken AffordanceSurface,
    ColorToken AffordanceAccent,
    ColorToken DocumentSurface,
    ColorToken DocumentText,
    ColorToken DocumentHeading,
    ColorToken DocumentMutedText,
    ColorToken DocumentCodeSurface,
    ColorToken DocumentBorder,
    ColorToken DocumentQuoteBorder,
    ColorToken DocumentLinkText,
    ColorToken DocumentDiagnosticText);

public static class OblivionStandaloneStyles
{
    public static OblivionStandaloneStyle Dark { get; } = new(
        Appearance: OblivionResolvedAppearance.Dark,
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
        SelectedCardBorder: ColorToken.Hex(0x2563EBFF),
        PrimaryText: ColorToken.Hex(0xF8FAFCFF),
        SecondaryText: ColorToken.Hex(0xA8B8CCFF),
        BadgeSurface: ColorToken.Hex(0x1E293BFF),
        BadgeText: ColorToken.Hex(0xE2E8F0FF),
        BadgeBorder: ColorToken.Hex(0x334155FF),
        AffordanceSurface: ColorToken.Hex(0x111827FF),
        AffordanceAccent: ColorToken.Hex(0x38BDF8FF),
        DocumentSurface: ColorToken.Hex(0x0F172AFF),
        DocumentText: ColorToken.Hex(0xE2E8F0FF),
        DocumentHeading: ColorToken.Hex(0xFFFFFFFF),
        DocumentMutedText: ColorToken.Hex(0xA8B8CCFF),
        DocumentCodeSurface: ColorToken.Hex(0x111827FF),
        DocumentBorder: ColorToken.Hex(0x475569FF),
        DocumentQuoteBorder: ColorToken.Hex(0x64748BFF),
        DocumentLinkText: ColorToken.Hex(0x93C5FDFF),
        DocumentDiagnosticText: ColorToken.Hex(0xFBBF24FF));

    public static OblivionStandaloneStyle Light { get; } = Dark with
    {
        Appearance = OblivionResolvedAppearance.Light,
        PageBackground = ColorToken.Hex(0xEDEFF0FF),
        CardBackground = ColorToken.Hex(0xF8FAFCFF),
        CardBorder = ColorToken.Hex(0xD4D4D8FF),
        SelectedCardBorder = ColorToken.Hex(0x2563EBFF),
        PrimaryText = ColorToken.Hex(0x18181BFF),
        SecondaryText = ColorToken.Hex(0x52525BFF),
        BadgeSurface = ColorToken.Hex(0xF4F4F5FF),
        BadgeText = ColorToken.Hex(0x27272AFF),
        BadgeBorder = ColorToken.Hex(0xD4D4D8FF),
        AffordanceSurface = ColorToken.Hex(0xFFFFFFFF),
        AffordanceAccent = ColorToken.Hex(0x2563EBFF),
        DocumentSurface = ColorToken.Hex(0xFFFFFFFF),
        DocumentText = ColorToken.Hex(0x27272AFF),
        DocumentHeading = ColorToken.Hex(0x09090BFF),
        DocumentMutedText = ColorToken.Hex(0x52525BFF),
        DocumentCodeSurface = ColorToken.Hex(0xF4F4F5FF),
        DocumentBorder = ColorToken.Hex(0xD4D4D8FF),
        DocumentQuoteBorder = ColorToken.Hex(0xA1A1AAFF),
        DocumentLinkText = ColorToken.Hex(0x1D4ED8FF),
        DocumentDiagnosticText = ColorToken.Hex(0x92400EFF),
    };

    public static OblivionStandaloneStyle M19h => Dark;

    public static OblivionStandaloneStyle For(OblivionResolvedAppearance appearance)
    {
        return appearance == OblivionResolvedAppearance.Light ? Light : Dark;
    }
}

public static class OblivionStandaloneAppearanceResolver
{
    public static OblivionResolvedAppearance Resolve(
        OblivionAppearance configuredAppearance,
        OblivionResolvedAppearance platformAppearance)
    {
        return configuredAppearance switch
        {
            OblivionAppearance.Light => OblivionResolvedAppearance.Light,
            OblivionAppearance.Dark => OblivionResolvedAppearance.Dark,
            OblivionAppearance.System => platformAppearance,
            _ => throw new ArgumentOutOfRangeException(nameof(configuredAppearance)),
        };
    }
}
