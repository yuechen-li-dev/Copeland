using Machina.Presentation.Screens;
using Xunit;

namespace Machina.Presentation.Tests;

public sealed class PresenterScreenStackTests
{
    [Fact]
    public void EmptyStack_HasNoVisibleScreens()
    {
        var stack = new PresenterScreenStack([ScreenLayers.World]);

        Assert.Equal(0, stack.Count);
        Assert.Empty(stack.VisibleScreensInCompositionOrder());
    }

    [Fact]
    public void OneVisibleScreen_IsReturned()
    {
        var stack = new PresenterScreenStack([ScreenLayers.World]);
        var world = new FakeScreen("world", "world");

        stack.Add(world);

        Assert.Equal([world], stack.VisibleScreensInCompositionOrder());
    }

    [Fact]
    public void HiddenScreen_IsExcluded()
    {
        var stack = new PresenterScreenStack([ScreenLayers.World, ScreenLayers.Hud]);
        var world = new FakeScreen("world", "world");
        var hud = new FakeScreen("hud", "hud", isVisible: false);

        stack.Add(world);
        stack.Add(hud);

        Assert.Equal([world], stack.VisibleScreensInCompositionOrder());
    }

    [Fact]
    public void MultipleDeclaredLayers_ComposeByConfiguredOrder()
    {
        ScreenLayerOrder order =
        [
            ScreenLayers.Background,
            ScreenLayers.World,
            ScreenLayers.Hud,
            ScreenLayers.Overlay,
        ];
        var stack = new PresenterScreenStack(order);
        var overlay = new FakeScreen("overlay", "overlay");
        var background = new FakeScreen("background", "background");
        var hud = new FakeScreen("hud", "hud");
        var world = new FakeScreen("world", "world");

        stack.Add(overlay);
        stack.Add(background);
        stack.Add(hud);
        stack.Add(world);

        Assert.Equal([background, world, hud, overlay], stack.VisibleScreensInCompositionOrder());
    }

    [Fact]
    public void SameLayerScreens_PreserveInsertionOrder()
    {
        var stack = new PresenterScreenStack([ScreenLayers.World, ScreenLayers.Hud]);
        var firstWorld = new FakeScreen("first-world", "world");
        var secondWorld = new FakeScreen("second-world", "world");
        var hud = new FakeScreen("hud", "hud");

        stack.Add(secondWorld);
        stack.Add(hud);
        stack.Add(firstWorld);

        Assert.Equal([secondWorld, firstWorld, hud], stack.VisibleScreensInCompositionOrder());
    }

    [Fact]
    public void DuplicateScreenIdentity_IsRejectedCaseInsensitively()
    {
        var stack = new PresenterScreenStack([ScreenLayers.World, ScreenLayers.Hud]);
        stack.Add(new FakeScreen("primary", "world"));

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => stack.Add(new FakeScreen(" PRIMARY ", "hud")));

        Assert.Contains("primary", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UndeclaredLayer_IsRejected()
    {
        var stack = new PresenterScreenStack([ScreenLayers.World]);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => stack.Add(new FakeScreen("hud", "hud")));

        Assert.Contains("hud", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LayerKeysAndScreenIdentities_NormalizeAndRejectBlankValues()
    {
        Assert.Equal(new ScreenLayerKey("hud"), new ScreenLayerKey(" HUD "));
        Assert.Equal(new PresenterScreenId("world"), new PresenterScreenId(" WORLD "));
        Assert.ThrowsAny<ArgumentException>(() => _ = new ScreenLayerKey(" "));
        Assert.ThrowsAny<ArgumentException>(() => _ = new PresenterScreenId(" "));
    }

    [Fact]
    public void EqualLayerOrders_UseDeterministicKeyOrdering()
    {
        ScreenLayerOrder order =
        [
            Layer.At("hits", 250),
            Layer.At("damage-vignette", 250),
            ScreenLayers.Hud,
        ];

        Assert.Equal(
            ["hud", "damage-vignette", "hits"],
            order.CompositionSlots.Select(static slot => slot.Key.Value).ToArray());
    }

    [Fact]
    public void RepeatedComposition_IsDeterministic()
    {
        var stack = new PresenterScreenStack([ScreenLayers.World, ScreenLayers.Hud]);
        stack.Add(new FakeScreen("world", "world"));
        stack.Add(new FakeScreen("hud", "hud"));

        IReadOnlyList<IPresenterScreen> first = stack.VisibleScreensInCompositionOrder();
        IReadOnlyList<IPresenterScreen> second = stack.VisibleScreensInCompositionOrder();

        Assert.Equal(first, second);
    }

    [Fact]
    public void PresentationAssembly_HasNoAurelianReferences()
    {
        string[] references = typeof(PresenterScreenStack).Assembly
            .GetReferencedAssemblies()
            .Select(static assembly => assembly.Name!)
            .ToArray();

        Assert.DoesNotContain(references, static reference => reference.StartsWith("Aurelian", StringComparison.Ordinal));
    }

    private sealed class FakeScreen : IPresenterScreen
    {
        public FakeScreen(string id, string layer, bool isVisible = true)
        {
            Id = new PresenterScreenId(id);
            Layer = new ScreenLayerKey(layer);
            IsVisible = isVisible;
        }

        public PresenterScreenId Id { get; }

        public ScreenLayerKey Layer { get; }

        public bool IsVisible { get; }
    }
}
