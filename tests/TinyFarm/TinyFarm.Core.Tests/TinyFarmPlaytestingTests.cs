using Aurelian.Playtesting;
using System.Text.Json;
using Deliverance.Core.Storage;
using InputMan.Aurelian;
using InputMan.Core;
using TinyFarm.InputMan;
using TinyFarm.Playtesting;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmPlaytestingTests
{
    private sealed class Fixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "playtest-tests", Guid.NewGuid().ToString("N"));
        public PlaytestTarget Target { get; }
        public PlaytestRunner Runner { get; }

        public Fixture(bool slice = true)
        {
            var game = new TinyFarmGame(new FileSaveStore(Path.Combine(Directory, "saves")), slice: slice, crafting: slice, shipping: slice);
            Target = new PlaytestTarget(game, new AurelianInputAdapter(new InputManEngine(GameControls.CreateProfile(true))));
            Runner = new PlaytestRunner(Target, Directory);
        }

        public void Dispose()
        {
            Runner.Dispose();
            Target.Dispose();
        }
    }

    [Fact]
    public void NormalizedMouseBeginsGameThroughMachinaHitTesting()
    {
        using var fixture = new Fixture();
        PlaytestObservation before = fixture.Target.Observe();
        PlaytestMenuTarget button = Assert.Single(before.Targets, target => target.Action == "new-game");
        PlaytestObservation after = fixture.Runner.Execute(new("click", X: button.X, Y: button.Y));
        Assert.Equal("Playing", after.Screen);
        Assert.Equal(before.SemanticHash, after.SemanticHash);
    }

    [Fact]
    public void SharedInspectorReadsExistingScheduleBrainsWithoutChangingGameState()
    {
        using var fixture = new Fixture(slice: false);
        fixture.Runner.ExecuteLine("new game");
        fixture.Runner.ExecuteLine("wait 2");
        PlaytestObservation observation = fixture.Runner.ExecuteLine("inspect brains");
        Assert.Contains(observation.Brains.SelectMany(inspection => inspection.Agents), agent => agent.ActivePath.Length > 0);
        Assert.Contains(observation.Brains.SelectMany(inspection => inspection.Trace), entry => entry.Decision is not null);
        Assert.Contains(observation.Brains.SelectMany(inspection => inspection.Agents).SelectMany(agent => agent.Blackboard), value => !value.CheckpointSupported);
        Assert.Equal(observation.SemanticHash, fixture.Target.Observe().SemanticHash);
    }

    [Fact]
    public void ChordCancelsMovementWhileClockAdvancesExactlyThreeSeconds()
    {
        using var fixture = new Fixture();
        PlaytestObservation before = fixture.Runner.ExecuteLine("new game");
        PlaytestObservation after = fixture.Runner.ExecuteLine("hold w+s 3");
        Assert.Equal(before.X, after.X);
        Assert.Equal(before.Y, after.Y);
        Assert.Equal(before.Minute + 3, after.Minute);
        PlaytestObservation moved = fixture.Runner.ExecuteLine("hold w 0.3");
        Assert.True(moved.Y < after.Y);
    }

    [Fact]
    public void TextEntryAndHeldMovementCannotLeakThroughActionMap()
    {
        using var fixture = new Fixture();
        fixture.Runner.ExecuteLine("new game");
        fixture.Runner.ExecuteLine("open inventory");
        PlaytestObservation before = fixture.Runner.ExecuteLine("click-action search");
        fixture.Runner.ExecuteLine("text w");
        PlaytestObservation after = fixture.Runner.ExecuteLine("hold w 3");
        Assert.Equal("w", after.Search);
        Assert.True(after.SearchFocused);
        Assert.Contains("TextEntry", after.ActionMaps);
        Assert.DoesNotContain("Gameplay", after.ActionMaps);
        Assert.Equal(before.SemanticHash, after.SemanticHash);
        Assert.Equal(before.FieldHash, after.FieldHash);
    }

    [Fact]
    public void NamedDeliveranceSlotRestoresWorldAndField()
    {
        using var fixture = new Fixture();
        fixture.Runner.ExecuteLine("new game");
        PlaytestObservation saved = fixture.Runner.ExecuteLine("save slot2.sav");
        fixture.Runner.ExecuteLine("hold a 0.3");
        PlaytestObservation loaded = fixture.Runner.ExecuteLine("load slot2.sav");
        Assert.Equal(saved.SemanticHash, loaded.SemanticHash);
        Assert.Equal(saved.FieldHash, loaded.FieldHash);
        Assert.Equal("Playing", loaded.Screen);
    }

    [Fact]
    public void HeldKeyIsClearedByFocusLossAndDoesNotResumeOnRegain()
    {
        using var fixture = new Fixture();
        fixture.Runner.ExecuteLine("new game");
        fixture.Runner.ExecuteLine("press w");
        fixture.Runner.ExecuteLine("focus off");
        PlaytestObservation before = fixture.Target.Observe();
        PlaytestObservation hidden = fixture.Runner.ExecuteLine("wait-seconds 2");
        Assert.Equal(before.SemanticHash, hidden.SemanticHash);
        fixture.Runner.ExecuteLine("focus on");
        PlaytestObservation after = fixture.Runner.ExecuteLine("wait-seconds 0.1");
        Assert.Equal(before.X, after.X);
        Assert.Equal(before.Y, after.Y);
    }

    [Fact]
    public void FailedAssertionReleasesKeysAndLeavesUsefulTrace()
    {
        using var fixture = new Fixture();
        fixture.Runner.ExecuteLine("new game");
        fixture.Runner.ExecuteLine("press w");
        Assert.Throws<InvalidOperationException>(() => fixture.Runner.ExecuteLine("assert screen Inventory"));
        PlaytestObservation before = fixture.Target.Observe();
        PlaytestObservation after = fixture.Runner.ExecuteLine("wait-seconds 0.1");
        Assert.Equal(before.Y, after.Y);
        using var traceStream = new FileStream(Path.Combine(fixture.Directory, "trace.jsonl"),
            FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(traceStream);
        Assert.Contains("Assertion failed", reader.ReadToEnd());
    }

    [Fact]
    public void SeededFuzzReplaysIdenticalWorldAndField()
    {
        PlaytestScript script = TinyFarmPlaytestProfiles.Fuzz(25, 100);
        using var first = new Fixture();
        using var second = new Fixture();
        first.Runner.Run(script);
        second.Runner.Run(TinyFarmPlaytestProfiles.Fuzz(25, 100));
        PlaytestObservation a = first.Target.Observe();
        PlaytestObservation b = second.Target.Observe();
        Assert.Equal(a.SemanticHash, b.SemanticHash);
        Assert.Equal(a.FieldHash, b.FieldHash);
        Assert.Equal(a.Screen, b.Screen);
        Assert.Equal(a.AcceptedActions, b.AcceptedActions);
        Assert.Equal(a.RejectedActions, b.RejectedActions);
        string json = File.ReadAllText(Path.Combine(first.Directory, "script.json"));
        Assert.NotNull(JsonSerializer.Deserialize(json, PlaytestJsonContext.Default.PlaytestScript));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("50000")]
    [InlineData("Unknown")]
    public void InvalidPhysicalKeysAreRejectedBeforeGameMutation(string key)
    {
        using var fixture = new Fixture();
        string before = fixture.Target.Observe().SemanticHash;
        Assert.Throws<FormatException>(() => fixture.Runner.Run(new(1, [new("command", Command: "new game"), new("hold", Keys: [key])])));
        Assert.Equal("Title", fixture.Target.Observe().Screen);
        Assert.Equal(before, fixture.Target.Observe().SemanticHash);
    }

    [Fact]
    public void ResizeAndHudChangesArePresentationOnly()
    {
        using var fixture = new Fixture();
        fixture.Runner.ExecuteLine("new game");
        fixture.Runner.ExecuteLine("mark initial");
        fixture.Runner.ExecuteLine("resize 2560 1440");
        fixture.Runner.ExecuteLine("tap f9");
        PlaytestObservation after = fixture.Runner.ExecuteLine("assert unchanged initial");
        Assert.False(after.HudVisible);
        Assert.Equal(2560, after.Width);
        Assert.Equal(1440, after.Height);
    }

    [Fact]
    public void CaptureAndSavePathsCannotEscapeTheirOwnedDirectory()
    {
        using var fixture = new Fixture();
        Assert.Throws<InvalidOperationException>(() => fixture.Runner.ExecuteLine("capture ../outside.png"));
        Assert.Throws<InvalidOperationException>(() => fixture.Runner.ExecuteLine("save ../outside"));
    }

    [Fact]
    public void UnknownUiActionsAndImpossibleSemanticActionsFailExplicitly()
    {
        using var fixture = new Fixture();
        Assert.Throws<InvalidOperationException>(() => fixture.Runner.ExecuteLine("ui missing"));
        fixture.Runner.ExecuteLine("new game");
        Assert.Throws<InvalidOperationException>(() => fixture.Runner.ExecuteLine("open container shipping-chest"));
        Assert.True(fixture.Target.Observe().RejectedActions > 0);
    }
}
