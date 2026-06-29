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
            GalleryScreen.Build(offState, theme: StandardTheme.Default),
            GalleryScreen.Width,
            GalleryScreen.Height);

        var onFrame = GeometryHarness.ResolveDocument(
            GalleryScreen.Build(onState, theme: StandardTheme.Default),
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
            GalleryScreen.Build(GalleryState.Default, theme: StandardTheme.Default),
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

    [Fact]
    public void Gallery_BadgeRow_ResolvesWithoutOverflowOrOverlap()
    {
        var frame = GeometryHarness.ResolveDocument(
            GalleryScreen.Build(GalleryState.Default, theme: StandardTheme.Default),
            GalleryScreen.Width,
            GalleryScreen.Height);

        var row = frame.RectOf("badges-section/badges-row");
        var stableShell = frame.RectOf("badges-section/badge-stable");
        var alertShell = frame.RectOf("badges-section/badge-alert");
        var stableLabelRegion = frame.RectOf("badges-section/badge-stable.label-region");
        var alertLabelRegion = frame.RectOf("badges-section/badge-alert.label-region");

        Assert.True(stableShell.Width > 0 && stableShell.Height > 0);
        Assert.True(alertShell.Width > 0 && alertShell.Height > 0);
        Assert.True(stableShell.X >= row.X);
        Assert.True(alertShell.X + alertShell.Width <= row.X + row.Width);
        Assert.True(stableShell.X + stableShell.Width <= alertShell.X);
        AssertRectInside(stableLabelRegion, stableShell, "badges-section/badge-stable.label-region");
        AssertRectInside(alertLabelRegion, alertShell, "badges-section/badge-alert.label-region");
    }

    [Fact]
    public void Gallery_MsdfProofSlot_ExistsOnlyWhenEnabled()
    {
        var defaultFrame = GeometryHarness.ResolveDocument(
            GalleryScreen.Build(GalleryState.Default, theme: StandardTheme.Default),
            GalleryScreen.Width,
            GalleryScreen.Height);
        var proofFrame = GeometryHarness.ResolveDocument(
            GalleryScreen.Build(GalleryState.Default, includeMsdfFontProof: true, theme: StandardTheme.Default),
            GalleryScreen.Width,
            GalleryScreen.Height);

        Assert.False(GalleryMsdfFontProofLayout.TryGetImageSlotRect(defaultFrame.Resolved, out _));
        Assert.True(GalleryMsdfFontProofLayout.TryGetImageSlotRect(proofFrame.Resolved, out var rect));
        Assert.True(rect.Width > 0);
        Assert.True(rect.Height > 0);
    }

    [Fact]
    public void ComponentGallery_CanBuildDirectOutlineTextProof()
    {
        var options = new GalleryProofOptions(IncludeDirectOutlineTextProof: true);
        var frame = GeometryHarness.ResolveDocument(
            GalleryScreen.Build(GalleryState.Default, options, StandardTheme.Default),
            GalleryScreen.Width,
            GalleryScreen.GetHeight(options));

        Assert.True(GalleryDirectOutlineTextProofLayout.TryGetProofImageSlotRect(frame.Resolved, out var proofRect));
        Assert.True(GalleryDirectOutlineTextProofLayout.TryGetComparisonDirectSlotRect(frame.Resolved, out var comparisonRect));
        Assert.True(proofRect.Width > 0);
        Assert.True(proofRect.Height > 0);
        Assert.True(comparisonRect.Width > 0);
        Assert.True(comparisonRect.Height > 0);
    }

    [Fact]
    public void ComponentGallery_DirectOutlineTextLayoutProof_IsOptIn()
    {
        var defaultFrame = GeometryHarness.ResolveDocument(
            GalleryScreen.Build(GalleryState.Default, theme: StandardTheme.Default),
            GalleryScreen.Width,
            GalleryScreen.Height);
        var proofOptions = new GalleryProofOptions(IncludeDirectOutlineTextLayoutProof: true);
        var proofFrame = GeometryHarness.ResolveDocument(
            GalleryScreen.Build(GalleryState.Default, proofOptions, StandardTheme.Default),
            GalleryScreen.Width,
            GalleryScreen.GetHeight(proofOptions));

        Assert.False(GalleryDirectOutlineTextLayoutProofLayout.TryGetProofImageSlotRect(defaultFrame.Resolved, out _));
        Assert.True(GalleryDirectOutlineTextLayoutProofLayout.TryGetProofImageSlotRect(proofFrame.Resolved, out var proofRect));
        Assert.True(GalleryDirectOutlineTextLayoutProofLayout.TryGetAlignmentGridImageSlotRect(proofFrame.Resolved, out var gridRect));
        Assert.True(proofRect.Width > 0);
        Assert.True(gridRect.Height > 0);
    }

    [Fact]
    public void ComponentGallery_RenderBridgeProof_IsOptIn()
    {
        var defaultFrame = GeometryHarness.ResolveDocument(
            GalleryScreen.Build(GalleryState.Default, theme: StandardTheme.Default),
            GalleryScreen.Width,
            GalleryScreen.Height);
        var proofOptions = new GalleryProofOptions(IncludeDirectOutlineRenderBridgeProof: true);
        var proofFrame = GeometryHarness.ResolveDocument(
            GalleryScreen.Build(GalleryState.Default, proofOptions, StandardTheme.Default),
            GalleryScreen.Width,
            GalleryScreen.GetHeight(proofOptions));

        Assert.False(GalleryDirectOutlineRenderBridgeProofLayout.TryGetProofImageSlotRect(defaultFrame.Resolved, out _));
        Assert.True(GalleryDirectOutlineRenderBridgeProofLayout.TryGetProofImageSlotRect(proofFrame.Resolved, out var proofRect));
        Assert.True(GalleryDirectOutlineRenderBridgeProofLayout.TryGetAlignmentGridImageSlotRect(proofFrame.Resolved, out var gridRect));
        Assert.True(proofRect.Width > 0);
        Assert.True(gridRect.Height > 0);
    }

    private static void AssertRectInside(Machina.Layout.Geometry.Rect inner, Machina.Layout.Geometry.Rect outer, string id)
    {
        Assert.True(inner.X >= outer.X, $"{id} left outside root.");
        Assert.True(inner.Y >= outer.Y, $"{id} top outside root.");
        Assert.True(inner.X + inner.Width <= outer.X + outer.Width, $"{id} right outside root.");
        Assert.True(inner.Y + inner.Height <= outer.Y + outer.Height, $"{id} bottom outside root.");
    }
}
