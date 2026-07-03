using Machina.Layout.Geometry;
using Machina.Presenter.Sample;
using Xunit;

namespace Machina.Presenter.Sample.Tests;

internal static class PresenterRegionAssert
{
    public static OblivionScrollRegionTarget SingleScrollRegion(
        PresenterNavigationShellRenderResult render,
        PresenterScrollbarTargetKind kind,
        string? cardId = null)
    {
        return Assert.Single(
            render.PageRender!.OblivionInteraction!.ScrollRegions,
            target => target.Target.Kind == kind &&
                (cardId is null || string.Equals(target.Target.CardId, cardId, StringComparison.Ordinal)));
    }

    public static void RectInside(Rect inner, Rect outer, string id)
    {
        Assert.True(inner.X >= outer.X, $"{id} left edge should be inside parent.");
        Assert.True(inner.Y >= outer.Y, $"{id} top edge should be inside parent.");
        Assert.True(inner.X + inner.Width <= outer.X + outer.Width, $"{id} right edge should be inside parent.");
        Assert.True(inner.Y + inner.Height <= outer.Y + outer.Height, $"{id} bottom edge should be inside parent.");
    }

    public static void DoesNotOverlapVertically(Rect upper, Rect lower, string id)
    {
        Assert.True(
            upper.Y + upper.Height <= lower.Y,
            $"{id} should not overlap vertically. Upper={upper}, lower={lower}");
    }
}
