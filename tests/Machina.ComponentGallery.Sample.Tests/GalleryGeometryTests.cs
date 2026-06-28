using Machina.ComponentGallery.Sample;
using Machina.Standard.Theme;
using Machina.Testing;
using Xunit;

namespace Machina.ComponentGallery.Sample.Tests;

public sealed class GalleryGeometryTests
{
    [Fact]
    public void Gallery_LiveToggleGeometry_RemainsStableAcrossStateChanges()
    {
        var offState = GalleryState.Default with
        {
            LiveCheckboxChecked = false,
            LiveSwitchOn = false,
        };

        var onState = GalleryState.Default with
        {
            LiveCheckboxChecked = true,
            LiveSwitchOn = true,
        };

        var offFrame = GeometryHarness.ResolveDocument(
            GalleryScreen.Build(offState, StandardTheme.Default),
            GalleryScreen.Width,
            GalleryScreen.Height);

        var onFrame = GeometryHarness.ResolveDocument(
            GalleryScreen.Build(onState, StandardTheme.Default),
            GalleryScreen.Width,
            GalleryScreen.Height);

        GeometryHarness.AssertSameRowIds(offFrame, onFrame);
        GeometryHarness.AssertSameRectBetween(offFrame, onFrame, "actions-section/live-checkbox.mark");
        GeometryHarness.AssertSameRectBetween(offFrame, onFrame, "actions-section/live-checkbox.box");
        GeometryHarness.AssertSameRectBetween(offFrame, onFrame, "actions-section/live-switch.track");
        GeometryHarness.AssertOnlyXDiffers(offFrame, onFrame, "actions-section/live-switch.thumb");
    }

    [Fact]
    public void Gallery_SectionHosts_StayInsideRootBounds()
    {
        var frame = GeometryHarness.ResolveDocument(
            GalleryScreen.Build(GalleryState.Default, StandardTheme.Default),
            GalleryScreen.Width,
            GalleryScreen.Height);

        var root = frame.RectOf("root");
        AssertRectInside(frame.RectOf("text-section"), root, "text-section");
        AssertRectInside(frame.RectOf("buttons-section"), root, "buttons-section");
        AssertRectInside(frame.RectOf("selection-section"), root, "selection-section");
        AssertRectInside(frame.RectOf("input-section"), root, "input-section");
        AssertRectInside(frame.RectOf("badges-section"), root, "badges-section");
        AssertRectInside(frame.RectOf("actions-section"), root, "actions-section");
        AssertRectInside(frame.RectOf("cards-section"), root, "cards-section");
        AssertRectInside(frame.RectOf("theme-section"), root, "theme-section");
    }

    private static void AssertRectInside(Machina.Layout.Geometry.Rect inner, Machina.Layout.Geometry.Rect outer, string id)
    {
        Assert.True(inner.X >= outer.X, $"{id} left outside root.");
        Assert.True(inner.Y >= outer.Y, $"{id} top outside root.");
        Assert.True(inner.X + inner.Width <= outer.X + outer.Width, $"{id} right outside root.");
        Assert.True(inner.Y + inner.Height <= outer.Y + outer.Height, $"{id} bottom outside root.");
    }
}
