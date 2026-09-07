using Deliverance.Core.Storage;
using InputMan.Core;
using TinyFarm.Core;
using TinyFarm.InputMan;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmPresentationM25Tests
{
    [Fact]
    public void IdenticalMovementWithDifferentPresentationProducesIdenticalSaveAndReplayState()
    {
        var visible = new TinyFarmGame(new FileSaveStore(Path.Combine(Path.GetTempPath(), "tinyfarm-m25-visible")));
        var hidden = new TinyFarmGame(new FileSaveStore(Path.Combine(Path.GetTempPath(), "tinyfarm-m25-hidden")));
        visible.Start();
        hidden.Start();
        hidden.Presentation.HudVisible = false;
        hidden.Presentation.InspectorVisible = true;
        var engine = new InputManEngine(GameControls.CreateProfile());
        engine.SetMaps(visible.Contexts);
        ScenePosition before = visible.State.ActorScene(TinyFarmIds.Player).WorldPosition;
        for (int step = 0; step < 90; step++)
        {
            var snapshot = new InputSnapshot(new Dictionary<ControlKey, bool> { [Controls.Key(KeyboardKey.D)] = true });
            engine.Tick(snapshot, 1f / 60, step / 60f);
            visible.Handle(engine.CurrentFrame);
            hidden.Handle(engine.CurrentFrame);
            visible.Advance(TimeSpan.FromSeconds(1.0 / 60), engine.CurrentFrame, true);
            hidden.Advance(TimeSpan.FromSeconds(1.0 / 60), engine.CurrentFrame, true);
        }
        Assert.NotEqual(before, visible.State.ActorScene(TinyFarmIds.Player).WorldPosition);
        Assert.Equal(TinyFarmSemanticHash.Compute(visible.State), TinyFarmSemanticHash.Compute(hidden.State));
        Assert.Equal(visible.Host.Session.Field.SemanticHash, hidden.Host.Session.Field.SemanticHash);
        Assert.Equal(visible.Host.Session.CaptureWeekSave(), hidden.Host.Session.CaptureWeekSave());
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(1600, 1000)]
    public void ProjectionRetainsUniformScaleAndCanopyHeadroom(int width, int height)
    {
        var layout = new TinyFarm.Native.TinyFarmPresentationLayout(width, height);
        Assert.Equal(16 * layout.WorldScale, layout.WorldViewport.Width, 3);
        Assert.Equal(13 * layout.WorldScale, layout.WorldViewport.Height, 3);
        Assert.True(layout.WorldViewport.X >= 0);
        Assert.True(layout.WorldViewport.Y >= 0);
        var square = layout.UiRect(new Aurelian.Graphics.Vulkan.Native2D.Native2DRect(20, 20, 100, 100));
        Assert.Equal(square.Width, square.Height);
    }

    [Fact]
    public void BankPaddingPreservesInteriorAndMakesOnlyTheBorderDry()
    {
        byte[] source = [127, 30, 255, 20, 128, 40, 0, 25, 129, 50, 255, 30, 130, 60, 255, 35];
        byte[] original = source.ToArray();
        byte[] padded = TinyFarm.Native.TinyFarmBankMask.Pad(source, 2, 2);
        Assert.Equal(original, source);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                int offset = (y * 4 + x) * 4;
                if (x == 0 || y == 0 || x == 3 || y == 3)
                {
                    Assert.Equal(0, padded[offset + 2]);
                }
                else
                {
                    Assert.Equal(source.Skip(((y - 1) * 2 + x - 1) * 4).Take(4), padded.Skip(offset).Take(4));
                }
            }
        }
    }

    [Fact]
    public void FunctionKeysTogglePresentationWithoutChangingGameplayOrInputContexts()
    {
        var game = new TinyFarmGame(new FileSaveStore(Path.Combine(Path.GetTempPath(), "tinyfarm-m25", Guid.NewGuid().ToString("N"))));
        game.Start();
        var engine = new InputManEngine(GameControls.CreateProfile());
        engine.SetMaps(game.Contexts);
        string hash = TinyFarmSemanticHash.Compute(game.State);
        string fieldHash = game.Host.Session.Field.SemanticHash;
        ActionMapId[] contexts = game.Contexts;

        Press(KeyboardKey.F9);
        Assert.False(game.Presentation.HudVisible);
        // A held key is not another toggle.
        Press(KeyboardKey.F9);
        Assert.False(game.Presentation.HudVisible);
        Release();
        Press(KeyboardKey.F10);
        Assert.True(game.Presentation.InspectorVisible);
        Assert.False(game.Presentation.HudVisible);
        Release();
        Press(KeyboardKey.F11);
        Assert.True(game.Presentation.CleanCaptureRequested);
        Assert.False(game.Presentation.HudVisible);
        Assert.Equal(hash, TinyFarmSemanticHash.Compute(game.State));
        Assert.Equal(fieldHash, game.Host.Session.Field.SemanticHash);
        Assert.Equal(contexts, game.Contexts);
        Assert.False(game.CapturesGameplay);

        void Press(KeyboardKey key)
        {
            engine.Tick(new InputSnapshot(new Dictionary<ControlKey, bool> { [Controls.Key(key)] = true }), .016f, 0);
            game.Handle(engine.CurrentFrame);
        }

        void Release()
        {
            engine.Tick(InputSnapshot.Empty, .016f, 0);
            game.Handle(engine.CurrentFrame);
        }
    }
}
