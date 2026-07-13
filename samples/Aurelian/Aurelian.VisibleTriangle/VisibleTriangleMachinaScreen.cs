using Machina.Presentation.Screens;

namespace Aurelian.VisibleTriangle;

/// <summary>
/// Integration-owned adapter that exposes the visible-triangle world sample to Machina screen composition.
/// </summary>
internal sealed class VisibleTriangleMachinaScreen : IPresenterScreen
{
    public VisibleTriangleMachinaScreen(
        VisibleTriangleWorldScreen worldScreen,
        bool isVisible = true)
    {
        ArgumentNullException.ThrowIfNull(worldScreen);

        WorldScreen = worldScreen;
        IsVisible = isVisible;
    }

    public PresenterScreenId Id => new("visible-triangle-world");

    public ScreenLayerKey Layer => ScreenLayers.World.Key;

    public bool IsVisible { get; }

    public VisibleTriangleWorldScreen WorldScreen { get; }
}
