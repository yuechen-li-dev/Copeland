using Deliverance.Core.Storage;
using InputMan.Core;
using Machina.Runtime.Input;
using TinyFarm.InputMan;
using TinyFarm.Native;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmTitleMenuTests
{
    private static TinyFarmGame Game() => new(new FileSaveStore(Path.Combine(Path.GetTempPath(),
        "tinyfarm-title-tests", Guid.NewGuid().ToString("N"))), slice: true, crafting: true);

    [Fact]
    public void ActualMachinaButtonsStartAndQuitWhileMissingLoadIsDisabled()
    {
        TinyFarmGame game = Game();
        var ui = new TinyFarmNativeUi(game);
        TinyFarmFrame frame = TinyFarmFrameProjector.Project(game.State, game.Definitions);
        string hash = TinyFarmSemanticHash.Compute(game.State);
        ui.Pointer(frame, new PointerPoint(200, 400), true);
        ui.Pointer(frame, new PointerPoint(200, 400), false);
        Assert.Equal(TinyFarmScreen.Title, game.Screen);
        Assert.False(game.LoadInProgress);
        ui.Pointer(frame, new PointerPoint(200, 330), true);
        ui.Pointer(frame, new PointerPoint(10, 10), false);
        Assert.Equal(TinyFarmScreen.Title, game.Screen);
        ui.Pointer(frame, new PointerPoint(200, 330), true);
        ui.Pointer(frame, new PointerPoint(200, 330), false);
        Assert.Equal(TinyFarmScreen.Playing, game.Screen);
        Assert.Equal(hash, TinyFarmSemanticHash.Compute(game.State));

        TinyFarmGame quitting = Game();
        var quitUi = new TinyFarmNativeUi(quitting);
        frame = TinyFarmFrameProjector.Project(quitting.State, quitting.Definitions);
        quitUi.Pointer(frame, new PointerPoint(200, 475), true);
        quitUi.Pointer(frame, new PointerPoint(200, 475), false);
        Assert.True(quitting.ShouldQuit);
    }

    [Fact]
    public void InputManTitleContextBlocksWorldAndEnterStartsWithoutChangingState()
    {
        TinyFarmGame game = Game();
        var engine = new InputManEngine(GameControls.CreateProfile(true));
        engine.SetMaps(game.Contexts);
        Assert.DoesNotContain(GameControls.Gameplay, game.Contexts);
        engine.Tick(new InputSnapshot(new Dictionary<ControlKey, bool>
        {
            [Controls.Key(KeyboardKey.W)] = true,
            [Controls.Key(KeyboardKey.J)] = true
        }, new Dictionary<ControlKey, float>()), .016f, 0);
        string hash = TinyFarmSemanticHash.Compute(game.State);
        game.Handle(engine.CurrentFrame);
        game.Advance(TimeSpan.FromSeconds(2), engine.CurrentFrame, true);
        Assert.Equal(hash, TinyFarmSemanticHash.Compute(game.State));
        engine.Tick(InputSnapshot.Empty, .016f, .016f);
        engine.Tick(new InputSnapshot(new Dictionary<ControlKey, bool>
        {
            [Controls.Key(KeyboardKey.Enter)] = true
        }, new Dictionary<ControlKey, float>()), .016f, .032f);
        game.Handle(engine.CurrentFrame);
        Assert.Equal(TinyFarmScreen.Playing, game.Screen);
        Assert.Equal(hash, TinyFarmSemanticHash.Compute(game.State));
    }

    [Fact]
    public async Task BootDetectsCheckpointAndTitleLoadRestoresTheExistingSave()
    {
        string path = Path.Combine(Path.GetTempPath(), "tinyfarm-title-tests", Guid.NewGuid().ToString("N"));
        var first = new TinyFarmGame(new FileSaveStore(path), slice: true, crafting: true);
        first.Start();
        first.State.Slice = first.State.Slice! with { Health = 7 };
        Assert.True(first.Save());
        var second = new TinyFarmGame(new FileSaveStore(path), slice: true, crafting: true);
        Assert.True(second.MenuSaveAvailable);
        var ui = new TinyFarmNativeUi(second);
        TinyFarmFrame frame = TinyFarmFrameProjector.Project(second.State, second.Definitions);
        ui.Pointer(frame, new PointerPoint(200, 400), true);
        ui.Pointer(frame, new PointerPoint(200, 400), false);
        var engine = new InputManEngine(GameControls.CreateProfile(true));
        engine.Tick(InputSnapshot.Empty, .016f, 0);
        for (int index = 0; index < 5000 && second.LoadInProgress; index++)
        {
            await Task.Delay(1);
            second.Advance(TimeSpan.Zero, engine.CurrentFrame, true);
        }
        Assert.False(second.LoadInProgress);
        Assert.Equal(TinyFarmScreen.Playing, second.Screen);
        Assert.Equal(7, second.State.Slice!.Health);
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(1600, 1000)]
    [InlineData(3440, 1440)]
    public void ArtCoverFillsFramebufferWithoutStretching(int width, int height)
    {
        var layout = new TinyFarmPresentationLayout(width, height);
        var uv = layout.CoverUv(1672, 941);
        Assert.InRange(uv.U0, 0, 1);
        Assert.InRange(uv.V0, 0, 1);
        Assert.InRange(uv.U1, 0, 1);
        Assert.InRange(uv.V1, 0, 1);
        double visibleRatio = (uv.U1 - uv.U0) * 1672 / ((uv.V1 - uv.V0) * 941);
        Assert.Equal(width / (double)height, visibleRatio, 5);
    }
}
