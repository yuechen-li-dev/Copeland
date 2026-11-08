using System.Diagnostics;
using Deliverance.Core.Storage;
using Machina.Presentation;
using TinyFarm.Core;
using TinyFarm.InputMan;
using TinyFarm.Native;
using TinyFarm.Oblivion;
using Xunit;
using Xunit.Abstractions;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmUiInvalidationTests(ITestOutputHelper output)
{
    [Fact]
    public void SpiritMoneyAndItemNamesRefreshWithoutExplicitInvalidation()
    {
        using var game = Game();
        game.Start();
        var ui = new TinyFarmNativeUi(game);
        TinyFarmFrame frame = TinyFarmFrameProjector.Project(game.State, game.Definitions);
        MachinaPresentationFrame first = ui.Resource(frame);
        int playerIndex = game.State.MutableActors.FindIndex(actor => actor.Id == TinyFarmIds.Player);
        ActorState player = game.State.MutableActors[playerIndex];
        game.State.MutableActors[playerIndex] = player with { Rpg = player.Rpg! with { SpiritCurrent = 5 } };
        MachinaPresentationFrame spirit = ui.Resource(frame);
        Assert.NotSame(first, spirit);
        Assert.Contains(Text(spirit), text => text.Contains("SP 5/", StringComparison.Ordinal));

        game.OpenInventory(false);
        first = ui.Resource(frame);
        player = game.State.MutableActors[playerIndex];
        game.State.MutableActors[playerIndex] = player with { Money = player.Money + 100 };
        MachinaPresentationFrame money = ui.Resource(frame);
        Assert.NotSame(first, money);
        Assert.Contains(Text(money), text => text.StartsWith($"{player.Money + 100} coins", StringComparison.Ordinal));

        int swordIndex = game.State.MutableItems.FindIndex(item => item.Id == TinyFarmIds.Sword);
        game.State.MutableItems[swordIndex] = game.State.MutableItems[swordIndex] with { Name = "Reforged sword" };
        MachinaPresentationFrame renamed = ui.Resource(frame);
        Assert.NotSame(money, renamed);
        Assert.Contains(Text(renamed), text => text.Contains("Reforged", StringComparison.Ordinal));
        Assert.Same(renamed, ui.Resource(frame));
    }

    [Fact]
    public void ClockChangesReuseTheUnchangedBaseView()
    {
        using var game = Game();
        game.Start();
        var ui = new TinyFarmNativeUi(game);
        var first = ui.Resources(TinyFarmFrameProjector.Project(game.State, game.Definitions));
        game.State.Minute++;
        var later = ui.Resources(TinyFarmFrameProjector.Project(game.State, game.Definitions));
        Assert.Same(first.Base, later.Base);
        Assert.NotSame(first.Clock, later.Clock);
        Assert.Equal(1, ui.Rebuilds);
    }

    [Fact]
    public void InspectorRefreshesFactsWithoutAViewportChange()
    {
        using var game = Game();
        var ui = new TinyFarmNativeUi(game);
        var layout = new TinyFarmPresentationLayout(1280, 720);
        game.LiveInspection.Presentation = new TinyFarmPresentationInspection(
            1280, 720, 64, true, true, 1, 1, "point sampling", "First detail", "world", "hud");
        MachinaPresentationFrame first = ui.InspectorResource(layout);
        game.LiveInspection.Presentation = game.LiveInspection.Presentation with
        {
            Sampling = "linear sampling",
            Detail = "Updated detail",
        };
        MachinaPresentationFrame changed = ui.InspectorResource(layout);
        Assert.NotSame(first, changed);
        Assert.Contains("linear sampling", Text(changed));
        Assert.Contains("Updated detail", Text(changed));
        Assert.Same(changed, ui.InspectorResource(layout));
    }

    [Fact]
    public void ReenteringTheSameScreenBetweenPressAndReleaseCannotActivateAnOldPress()
    {
        using var game = Game();
        game.Start();
        game.OpenInventory(false);
        var ui = new TinyFarmNativeUi(game);
        TinyFarmFrame frame = TinyFarmFrameProjector.Project(game.State, game.Definitions);
        var point = ui.ActionCenter(frame, TinyFarmUiActions.Equipment);
        ui.Pointer(frame, point, true);
        game.BackFromMenu();
        game.OpenInventory(false);
        ui.Pointer(frame, point, false);
        Assert.False(game.Menus.EquipmentOnly);
    }

    [Fact]
    public void SuspendingPausePreservesSelectionButCancelsItsPointerPress()
    {
        using var game = Game();
        game.Start();
        game.OpenPause();
        game.Menus.MovePause(2);
        var parent = game.UiScope;
        var ui = new TinyFarmNativeUi(game);
        TinyFarmFrame frame = TinyFarmFrameProjector.Project(game.State, game.Definitions);
        var quit = ui.ActionCenter(frame, TinyFarmUiActions.Quit);
        ui.Pointer(frame, quit, true);
        game.OpenInventory(true);
        game.BackFromMenu();
        Assert.Same(parent, game.UiScope);
        Assert.Equal(2, game.Menus.PauseSelection);
        ui.Pointer(frame, quit, false);
        Assert.Null(game.Menus.Confirmation);
    }

    [Fact]
    public void SearchFiltersAndScrollHaveScreenEntryLifetimesAndRestoreClearsTheWholeUi()
    {
        using var game = Game();
        game.Start();
        game.OpenInventory(false);
        var inventory = game.UiScope;
        game.Menus.SetSearch("sword");
        game.Menus.SearchFocused = true;
        game.Menus.SetEquipmentOnly(true);
        game.Start();
        Assert.True(inventory.IsDisposed);
        game.OpenStats(false);
        Assert.Equal("", game.Menus.Search);
        Assert.False(game.Menus.SearchFocused);
        game.Menus.SetStatsGroup("All");
        game.Menus.ScrollStats(4, 100);
        game.Start();
        game.OpenStats(false);
        Assert.Equal("Stats", game.Menus.StatsGroup);
        Assert.Equal(0, game.Menus.StatsOffset);
        game.Start();
        game.OpenInventory(false);
        Assert.Equal("", game.Menus.Search);
        Assert.False(game.Menus.EquipmentOnly);
        var active = game.UiScope;
        game.AfterRestore();
        Assert.True(active.IsDisposed);
        Assert.Equal([TinyFarmScreen.Playing], game.UiPath);
    }

    [Fact]
    public void UnchangedNativeViewsReusePreparationAndReportAuthoringCost()
    {
        using var game = Game();
        game.Start();
        var ui = new TinyFarmNativeUi(game);
        TinyFarmNativeUiFont font = TinyFarmNativeUiFont.Create(
            Path.Combine(AppContext.BaseDirectory, "Assets", "SpaceMono-Regular.ttf"));
        ui.SetTextMeasurer(font);
        TinyFarmFrame frame = TinyFarmFrameProjector.Project(game.State, game.Definitions);
        MachinaPresentationFrame expected = ui.Resource(frame);
        for (int index = 0; index < 10; index++)
        {
            Assert.Same(expected, ui.Resource(frame));
        }
        long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int index = 0; index < 100; index++)
        {
            Assert.Same(expected, ui.Resource(frame));
        }
        double microseconds = Stopwatch.GetElapsedTime(start).TotalMicroseconds / 100;
        long bytes = (GC.GetAllocatedBytesForCurrentThread() - beforeBytes) / 100;
        output.WriteLine($"Unchanged native HUD: {microseconds:F1} us, {bytes} bytes per declaration; {ui.Rebuilds} preparation(s).");
        Assert.Equal(1, ui.Rebuilds);
    }

    private static IEnumerable<string> Text(MachinaPresentationFrame frame)
    {
        return frame.Operations.OfType<PositionedTextOperation>().Select(operation => operation.Text);
    }

    private static TinyFarmGame Game()
    {
        return new TinyFarmGame(new FileSaveStore(Path.Combine(Path.GetTempPath(),
            "tinyfarm-ui-invalidation", Guid.NewGuid().ToString("N"))));
    }
}
