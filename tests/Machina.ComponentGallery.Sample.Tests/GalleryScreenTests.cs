using Machina.ComponentGallery.Sample;
using Machina.Standard.Theme;
using Machina.Testing;
using Xunit;

namespace Machina.ComponentGallery.Sample.Tests;

public sealed class GalleryScreenTests
{
    [Fact]
    public void GalleryScreen_BuildsRootAndSections()
    {
        var document = GalleryScreen.Build(GalleryState.Default, StandardTheme.Default);

        Assert.Contains(document.Rows, row => row.Id == "root");
        Assert.Contains(document.Rows, row => row.Id == "header");
        Assert.Contains(document.Rows, row => row.Id == "text-section");
        Assert.Contains(document.Rows, row => row.Id == "buttons-section");
        Assert.Contains(document.Rows, row => row.Id == "selection-section");
        Assert.Contains(document.Rows, row => row.Id == "input-section");
        Assert.Contains(document.Rows, row => row.Id == "badges-section");
        Assert.Contains(document.Rows, row => row.Id == "actions-section");
        Assert.Contains(document.Rows, row => row.Id == "cards-section");
        Assert.Contains(document.Rows, row => row.Id == "theme-section");

        Assert.All(
            document.Rows.Where(row => row.Id != "root"),
            row => Assert.Equal("root", row.Parent));
    }

    [Fact]
    public void GalleryScreen_StillContainsKnownSections()
    {
        var document = GalleryScreen.Build(GalleryState.Default, StandardTheme.Default);
        var rowIds = document.Rows.Select(row => row.Id).ToArray();

        Assert.Contains("text-section", rowIds);
        Assert.Contains("buttons-section", rowIds);
        Assert.Contains("selection-section", rowIds);
        Assert.Contains("badges-section", rowIds);
        Assert.Contains("actions-section", rowIds);
        Assert.Contains("input-section", rowIds);
        Assert.Contains("cards-section", rowIds);
        Assert.Contains("theme-section", rowIds);
    }

    [Fact]
    public void Gallery_HitTargets_UseGalleryActions()
    {
        var frame = GeometryHarness.ResolveDocument(
            GalleryScreen.Build(GalleryState.Default, StandardTheme.Default),
            GalleryScreen.Width,
            GalleryScreen.Height);

        frame.AssertHitActionInside("buttons-section/button-primary", GalleryActions.ClickPrimaryButton.Value, HitPointKind.Center);
        frame.AssertHitActionInside("buttons-section/button-outline", GalleryActions.ClickSecondaryButton.Value, HitPointKind.Center);
        frame.AssertHitActionInside("actions-section/live-checkbox.box", GalleryActions.ToggleCheckbox.Value, HitPointKind.Center);
        frame.AssertHitActionInside("actions-section/live-checkbox.label", GalleryActions.ToggleCheckbox.Value, HitPointKind.Center);
        frame.AssertHitActionInside("actions-section/live-switch.track", GalleryActions.ToggleSwitch.Value, HitPointKind.Center);
        frame.AssertHitActionInside("actions-section/live-switch.label", GalleryActions.ToggleSwitch.Value, HitPointKind.Center);
    }

    [Fact]
    public void GalleryState_Dispatch_UpdatesOnlyExpectedFields()
    {
        var state = GalleryState.Default;

        var primary = GalleryState.Dispatch(state, GalleryActions.ClickPrimaryButton);
        Assert.Equal(state.PrimaryClicks + 1, primary.PrimaryClicks);
        Assert.Equal(state.SecondaryClicks, primary.SecondaryClicks);

        var checkbox = GalleryState.Dispatch(state, GalleryActions.ToggleCheckbox);
        Assert.NotEqual(state.LiveCheckboxChecked, checkbox.LiveCheckboxChecked);
        Assert.Equal(state.LiveSwitchOn, checkbox.LiveSwitchOn);

        var unknown = GalleryState.Dispatch(state, new Machina.Core.Actions.UiActionId("gallery.unknown"));
        Assert.Same(state, unknown);
    }
}
