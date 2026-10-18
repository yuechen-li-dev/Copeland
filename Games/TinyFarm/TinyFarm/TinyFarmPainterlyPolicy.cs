using Aurelian.GameWorld2D;

namespace TinyFarm.Native;

/// <summary>Art realization only. These values never define trunks, walls, navigation, or interaction.</summary>
internal static class TinyFarmPainterlyPolicy
{
    public const double TreeVisualScale = 1.1;
    public const double FarmhouseVisualScale = 1.05;
    public const double TreeHeightAt48PixelsPerMetre = 178;
    public const double FarmhouseHeightAt48PixelsPerMetre = 210;
    public const SpriteSampling MeadowSampling = SpriteSampling.Linear;
    public const SpriteSampling TreeSampling = SpriteSampling.Linear;
    public const SpriteSampling FarmhouseSampling = SpriteSampling.Linear;
}
