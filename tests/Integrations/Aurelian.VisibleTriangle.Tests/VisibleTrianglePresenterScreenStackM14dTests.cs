using System.Runtime.CompilerServices;
using System.Reflection;
using Machina.Presentation.Screens;
using Xunit;

namespace Aurelian.VisibleTriangle.Tests;

public sealed class VisibleTrianglePresenterScreenStackM14dTests
{
    [Fact]
    public void VisibleTriangleMachinaScreen_AdaptsWorldScreenOnWorldLayer()
    {
        var screen = Assert.IsAssignableFrom<IPresenterScreen>(CreateMachinaWorldScreen(isVisible: true));

        Assert.Equal(new PresenterScreenId("visible-triangle-world"), screen.Id);
        Assert.Equal(ScreenLayers.World.Key, screen.Layer);
        Assert.True(screen.IsVisible);
    }

    [Fact]
    public void VisibleTriangleWorldScreen_RemainsFreeOfMachinaPresenterContract()
    {
        Assert.False(typeof(IPresenterScreen).IsAssignableFrom(typeof(VisibleTriangleWorldScreen)));
    }

    [Fact]
    public void VisibleTrianglePresenterScreenStack_UsesCollectionExpressionLayerOrder()
    {
        ScreenLayerOrder order = VisibleTrianglePresenterScreenStack.CreateLayerOrder();

        Assert.Equal(
            ["background", "world", "hud", "overlay", "debug", "cursor"],
            order.DeclaredSlots.Select(static layer => layer.Key.Value).ToArray());
    }

    [Fact]
    public void VisibleTrianglePresenterScreenStack_PlacesWorldBetweenBackgroundAndHud()
    {
        ScreenLayerOrder order = VisibleTrianglePresenterScreenStack.CreateLayerOrder();
        var stack = new PresenterScreenStack(order);
        var world = new FakeScreen("world", ScreenLayers.World.Key);
        var hud = new FakeScreen("hud", ScreenLayers.Hud.Key);
        var background = new FakeScreen("background", ScreenLayers.Background.Key);

        stack.Add(hud);
        stack.Add(world);
        stack.Add(background);

        IReadOnlyList<IPresenterScreen> visible = stack.VisibleScreensInCompositionOrder();

        Assert.Equal([background, world, hud], visible);
    }

    [Fact]
    public void VisibleTrianglePresenterScreenStack_CreateStack_RegistersVisibleWorldScreen()
    {
        var worldScreen = Assert.IsType<VisibleTriangleMachinaScreen>(CreateMachinaWorldScreen(isVisible: true));

        PresenterScreenStack stack = VisibleTrianglePresenterScreenStack.CreateStack(worldScreen);
        IReadOnlyList<IPresenterScreen> visible = stack.VisibleScreensInCompositionOrder();

        Assert.Equal([worldScreen], visible);
    }

    [Fact]
    public void VisibleTrianglePresenterScreenStack_PreservesDeterministicOrderWithinWorldLayer()
    {
        ScreenLayerOrder order = VisibleTrianglePresenterScreenStack.CreateLayerOrder();
        var stack = new PresenterScreenStack(order);
        var firstWorld = new FakeScreen("first-world", ScreenLayers.World.Key);
        var secondWorld = new FakeScreen("second-world", ScreenLayers.World.Key);

        stack.Add(firstWorld);
        stack.Add(secondWorld);

        IReadOnlyList<IPresenterScreen> visible = stack.VisibleScreensInCompositionOrder();

        Assert.Equal([firstWorld, secondWorld], visible);
    }

    [Fact]
    public void MixedMachinaUiAndAurelianWorldScreens_UseConfiguredCanonicalOrder()
    {
        ScreenLayerOrder order = VisibleTrianglePresenterScreenStack.CreateLayerOrder();
        var stack = new PresenterScreenStack(order);
        var world = Assert.IsType<VisibleTriangleMachinaScreen>(CreateMachinaWorldScreen(isVisible: true));
        var hud = new FakeScreen("sample-hud", ScreenLayers.Hud.Key);
        var overlay = new FakeScreen("sample-overlay", ScreenLayers.Overlay.Key);

        stack.Add(overlay);
        stack.Add(hud);
        stack.Add(world);

        Assert.Equal([world, hud, overlay], stack.VisibleScreensInCompositionOrder());
    }

    private static object CreateMachinaWorldScreen(bool isVisible)
    {
        var worldScreen = (VisibleTriangleWorldScreen)RuntimeHelpers.GetUninitializedObject(
            typeof(VisibleTriangleWorldScreen));
        return new VisibleTriangleMachinaScreen(worldScreen, isVisible);
    }

    private sealed class FakeScreen : IPresenterScreen
    {
        public FakeScreen(string id, ScreenLayerKey layer, bool isVisible = true)
        {
            Id = new PresenterScreenId(id);
            Layer = layer;
            IsVisible = isVisible;
        }

        public PresenterScreenId Id { get; }

        public ScreenLayerKey Layer { get; }

        public bool IsVisible { get; }
    }
}
