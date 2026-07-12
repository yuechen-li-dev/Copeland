using System.Runtime.CompilerServices;
using System.Reflection;
using Aurelian.Core.Presentation.Screens;
using Xunit;

namespace Aurelian.VisibleTriangle.Tests;

public sealed class VisibleTrianglePresenterScreenStackM14dTests
{
    [Fact]
    public void VisibleTriangleWorldScreen_ImplementsPresenterScreenOnWorldLayer()
    {
        var screen = Assert.IsAssignableFrom<IPresenterScreen>(CreateWorldScreen(isVisible: true));

        Assert.Equal(ScreenLayers.World.Key, screen.Layer);
        Assert.True(screen.IsVisible);
    }

    [Fact]
    public void VisibleTrianglePresenterScreenStack_UsesCollectionExpressionLayerOrder()
    {
        ScreenLayerOrder order = VisibleTrianglePresenterScreenStack.CreateLayerOrder();

        Assert.Equal(
            ["background", "world", "hud", "debug", "cursor"],
            order.DeclaredSlots.Select(static layer => layer.Key.Value).ToArray());
    }

    [Fact]
    public void VisibleTrianglePresenterScreenStack_PlacesWorldBetweenBackgroundAndHud()
    {
        ScreenLayerOrder order = VisibleTrianglePresenterScreenStack.CreateLayerOrder();
        var stack = new PresenterScreenStack(order);
        var background = new FakeScreen(ScreenLayers.Background.Key);
        var world = new FakeScreen(ScreenLayers.World.Key);
        var hud = new FakeScreen(ScreenLayers.Hud.Key);

        stack.Add(hud);
        stack.Add(world);
        stack.Add(background);

        IReadOnlyList<IPresenterScreen> visible = stack.VisibleScreensInCompositionOrder();

        Assert.Equal([background, world, hud], visible);
    }

    [Fact]
    public void VisibleTrianglePresenterScreenStack_CreateStack_RegistersVisibleWorldScreen()
    {
        var worldScreen = Assert.IsType<VisibleTriangleWorldScreen>(CreateWorldScreen(isVisible: true));

        PresenterScreenStack stack = VisibleTrianglePresenterScreenStack.CreateStack(worldScreen);
        IReadOnlyList<IPresenterScreen> visible = stack.VisibleScreensInCompositionOrder();

        Assert.Equal([worldScreen], visible);
    }

    [Fact]
    public void VisibleTrianglePresenterScreenStack_PreservesDeterministicOrderWithinWorldLayer()
    {
        ScreenLayerOrder order = VisibleTrianglePresenterScreenStack.CreateLayerOrder();
        var stack = new PresenterScreenStack(order);
        var firstWorld = new FakeScreen(ScreenLayers.World.Key);
        var secondWorld = new FakeScreen(ScreenLayers.World.Key);

        stack.Add(firstWorld);
        stack.Add(secondWorld);

        IReadOnlyList<IPresenterScreen> visible = stack.VisibleScreensInCompositionOrder();

        Assert.Equal([firstWorld, secondWorld], visible);
    }

    private static object CreateWorldScreen(bool isVisible)
    {
        object screen = RuntimeHelpers.GetUninitializedObject(typeof(VisibleTriangleWorldScreen));
        FieldInfo? field = typeof(VisibleTriangleWorldScreen).GetField(
            "<IsVisible>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(screen, isVisible);
        return screen;
    }

    private sealed class FakeScreen : IPresenterScreen
    {
        public FakeScreen(ScreenLayerKey layer, bool isVisible = true)
        {
            Layer = layer;
            IsVisible = isVisible;
        }

        public ScreenLayerKey Layer { get; }

        public bool IsVisible { get; }
    }
}
