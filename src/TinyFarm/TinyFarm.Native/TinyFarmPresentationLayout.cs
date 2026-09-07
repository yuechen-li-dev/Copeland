using Aurelian.GameWorld2D;
using Aurelian.Graphics.Vulkan.Native2D;

namespace TinyFarm.Native;

/// <summary>One scalable UI canvas over a world-owned fixed projection.</summary>
internal sealed record TinyFarmPresentationLayout(int Width, int Height, bool Legacy = false)
{
    public float UiScale => Math.Min(Width / 1280f, Height / 720f);
    public float UiLeft => (Width - 1280 * UiScale) / 2;
    public float UiTop => (Height - 720 * UiScale) / 2;
    // Fit 16 x 10 metres plus three metres of canopy headroom. The slab extends into aspect space visually.
    public float WorldScale => Legacy ? 48 : Math.Min(Width / 16f, Height / 13f);
    public PixelRect WorldViewport => Legacy
        ? new PixelRect(22, 24, 904, 648)
        : new PixelRect(Math.Max(0, (Width - 16 * WorldScale) / 2), Math.Max(0, (Height - 13 * WorldScale) / 2),
            16 * WorldScale, 13 * WorldScale);

    public Native2DRect UiRect(Native2DRect rect)
    {
        return new Native2DRect(UiLeft + rect.X * UiScale, UiTop + rect.Y * UiScale,
            rect.Width * UiScale, rect.Height * UiScale);
    }
}
