using Aurelian.Core.Presentation.Screens;
using Xunit;

namespace Aurelian.Core.Tests;

public sealed class PresenterScreenStackM14cTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    public void ScreenLayerKey_RejectsNullOrWhitespace(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => _ = new ScreenLayerKey(value!));
    }

    [Fact]
    public void ScreenLayerKey_NormalizesCaseAndWhitespace()
    {
        ScreenLayerKey key = new("  HUD ");

        Assert.Equal("hud", key.Value);
        Assert.Equal(new ScreenLayerKey("hud"), key);
    }

    [Fact]
    public void ScreenLayers_ExposeExpectedStandardNamesAndOrders()
    {
        ScreenLayerSlot[] layers =
        [
            ScreenLayers.Background,
            ScreenLayers.World,
            ScreenLayers.Hud,
            ScreenLayers.Overlay,
            ScreenLayers.Modal,
            ScreenLayers.Debug,
            ScreenLayers.Cursor,
        ];

        Assert.Equal(
            ["background", "world", "hud", "overlay", "modal", "debug", "cursor"],
            layers.Select(static layer => layer.Key.Value).ToArray());

        Assert.Equal([0, 100, 200, 300, 400, 900, 1000], layers.Select(static layer => layer.Order).ToArray());
        Assert.True(ScreenLayers.Background.Order < ScreenLayers.World.Order);
        Assert.True(ScreenLayers.World.Order < ScreenLayers.Hud.Order);
        Assert.True(ScreenLayers.Hud.Order < ScreenLayers.Overlay.Order);
        Assert.True(ScreenLayers.Overlay.Order < ScreenLayers.Modal.Order);
        Assert.True(ScreenLayers.Modal.Order < ScreenLayers.Debug.Order);
        Assert.True(ScreenLayers.Debug.Order < ScreenLayers.Cursor.Order);
    }

    [Fact]
    public void ScreenLayerOrder_SupportsDirectCollectionExpressionDeclaration()
    {
        ScreenLayerOrder order =
        [
            ScreenLayers.Background,
            ScreenLayers.World,
            ScreenLayers.Hud,
        ];

        Assert.Equal(3, order.Count);
        Assert.Equal(
            ["background", "world", "hud"],
            order.DeclaredSlots.Select(static layer => layer.Key.Value).ToArray());
    }

    [Fact]
    public void ScreenLayerOrder_SupportsCustomLayersInCollectionExpression()
    {
        ScreenLayerOrder order =
        [
            ScreenLayers.World,
            Layer.At("damage-vignette", 250),
            ScreenLayers.Hud,
        ];

        Assert.Equal(3, order.Count);
        Assert.True(order.ContainsLayer(new ScreenLayerKey("damage-vignette")));
        Assert.Equal(250, order.GetSlot(new ScreenLayerKey("damage-vignette")).Order);
        Assert.Equal(
            ["world", "hud", "damage-vignette"],
            order.CompositionSlots.Select(static layer => layer.Key.Value).ToArray());
    }

    [Fact]
    public void ScreenLayerOrder_RejectsDuplicateLayerKeys()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
        {
            ScreenLayerOrder _ =
            [
                ScreenLayers.World,
                Layer.At("world", 250),
            ];
        });

        Assert.Contains("duplicate layer key 'world'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ScreenLayerOrder_AllowsDuplicateNumericOrdersWithDeterministicTieBreak()
    {
        ScreenLayerOrder order =
        [
            Layer.At("hits", 250),
            Layer.At("damage-vignette", 250),
            ScreenLayers.Hud,
        ];

        Assert.Equal(
            ["hud", "damage-vignette", "hits"],
            order.CompositionSlots.Select(static layer => layer.Key.Value).ToArray());
        Assert.True(order.Compare(new ScreenLayerKey("damage-vignette"), new ScreenLayerKey("hits")) < 0);
    }

    [Fact]
    public void PresenterScreenStack_RejectsUnknownScreenLayer()
    {
        ScreenLayerOrder order =
        [
            ScreenLayers.World,
            ScreenLayers.Hud,
        ];

        var stack = new PresenterScreenStack(order);
        var screen = new FakeScreen("overlay");

        ArgumentException exception = Assert.Throws<ArgumentException>(() => stack.Add(screen));

        Assert.Contains("overlay", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PresenterScreenStack_SkipsHiddenScreens()
    {
        ScreenLayerOrder order =
        [
            ScreenLayers.World,
            ScreenLayers.Hud,
        ];

        var stack = new PresenterScreenStack(order);
        FakeScreen world = new("world");
        FakeScreen hud = new("hud", isVisible: false);

        stack.Add(world);
        stack.Add(hud);

        IReadOnlyList<IPresenterScreen> visible = stack.VisibleScreensInCompositionOrder();

        Assert.Equal([world], visible);
    }

    [Fact]
    public void PresenterScreenStack_ReturnsVisibleScreensInLayerOrder()
    {
        ScreenLayerOrder order =
        [
            ScreenLayers.Background,
            ScreenLayers.World,
            ScreenLayers.Hud,
            ScreenLayers.Debug,
        ];

        var stack = new PresenterScreenStack(order);
        FakeScreen debug = new("debug");
        FakeScreen background = new("background");
        FakeScreen hud = new("hud");
        FakeScreen world = new("world");

        stack.Add(debug);
        stack.Add(background);
        stack.Add(hud);
        stack.Add(world);

        IReadOnlyList<IPresenterScreen> visible = stack.VisibleScreensInCompositionOrder();

        Assert.Equal([background, world, hud, debug], visible);
    }

    [Fact]
    public void PresenterScreenStack_PreservesInsertionOrderWithinSameLayer()
    {
        ScreenLayerOrder order =
        [
            ScreenLayers.World,
            ScreenLayers.Hud,
        ];

        var stack = new PresenterScreenStack(order);
        FakeScreen firstWorld = new("world");
        FakeScreen secondWorld = new("world");
        FakeScreen hud = new("hud");

        stack.Add(secondWorld);
        stack.Add(hud);
        stack.Add(firstWorld);

        IReadOnlyList<IPresenterScreen> visible = stack.VisibleScreensInCompositionOrder();

        Assert.Equal([secondWorld, firstWorld, hud], visible);
    }

    private sealed class FakeScreen : IPresenterScreen
    {
        public FakeScreen(string layer, bool isVisible = true)
        {
            Layer = new ScreenLayerKey(layer);
            IsVisible = isVisible;
        }

        public ScreenLayerKey Layer { get; }

        public bool IsVisible { get; }
    }
}
