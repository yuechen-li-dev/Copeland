using Machina.Runtime.Input;
using Xunit;

namespace Aurelian.GameMenus.Tests;

public sealed class GameMenuTests
{
    private static GameMenuPage Page() => new("title", "GAME", "Choose an action.",
        [new("start", "Start"), new("locked", "Continue", true), new("quit", "Quit")]);

    [Fact]
    public void NavigationSkipsDisabledRowsAndStopsAtEnds()
    {
        var navigation = new GameMenuNavigation();
        var entries = Page().Entries;
        Assert.Null(navigation.Update(entries, new(false, true, false, false)));
        Assert.Equal(2, navigation.SelectedIndex);
        Assert.Equal("quit", navigation.Update(entries, new(false, true, true, false)));
        Assert.Equal("start", navigation.Update(entries, new(true, false, true, false)));
        Assert.Equal("start", navigation.Update(entries, new(true, false, true, false)));
    }

    [Fact]
    public void EmptyAndDisabledMenusCannotActivate()
    {
        var navigation = new GameMenuNavigation();
        Assert.Null(navigation.Update([], new(false, true, true, false)));
        Assert.Null(navigation.Update([new("locked", "Locked", true)], new(false, true, true, false)));
        Assert.Throws<ArgumentException>(() => navigation.Update(
            [new("same", "One"), new("same", "Two")], default));
    }

    [Fact]
    public void PointerUsesResolvedButtonsAndRequiresMatchingRelease()
    {
        var view = new GameMenuView();
        var page = Page();
        var start = view.ActionCenter(page, 0, "start");
        var quit = view.ActionCenter(page, 0, "quit");
        Assert.Null(view.Pointer(page, 0, start, true));
        Assert.Null(view.Pointer(page, 0, quit, false));
        view.Pointer(page, 0, start, true);
        Assert.Equal("start", view.Pointer(page, 0, start, false));
        var enabledPage = page with
        {
            Entries = page.Entries.Select(entry => entry with { Disabled = false }).ToArray(),
        };
        var disabled = view.ActionCenter(enabledPage, 0, "locked");
        view.Pointer(page, 0, disabled, true);
        Assert.Null(view.Pointer(page, 0, disabled, false));
        view.Pointer(page, 0, start, true);
        Assert.Null(view.Pointer(page, 0, new PointerPoint(0, 0), false));
    }

    [Fact]
    public void PageChangesAndFocusCancellationRejectStalePresses()
    {
        var view = new GameMenuView();
        var page = Page();
        var start = view.ActionCenter(page, 0, "start");
        view.Pointer(page, 0, start, true);
        Assert.Null(view.Pointer(page with { Id = "pause" }, 0, start, false));
        view.Pointer(page, 0, start, true);
        view.CancelPointer();
        Assert.Null(view.Pointer(page, 0, start, false));
    }

    [Fact]
    public void RetainedLayoutDetectsChangedEntriesInSameSourceList()
    {
        var entries = new List<GameMenuEntry> { new("start", "Start") };
        var page = new GameMenuPage("title", "GAME", "", entries);
        var view = new GameMenuView();
        var original = view.Prepare(page, 0);
        Assert.Same(original, view.Prepare(page with { }, 0));
        var point = view.ActionCenter(page, 0, "start");
        view.Pointer(page, 0, point, true);
        entries[0] = new("quit", "Quit");
        Assert.NotSame(original, view.Prepare(page, 0));
        Assert.Null(view.Pointer(page, 0, point, false));
        view.Pointer(page, 0, point, true);
        Assert.Equal("quit", view.Pointer(page, 0, point, false));
    }
}
