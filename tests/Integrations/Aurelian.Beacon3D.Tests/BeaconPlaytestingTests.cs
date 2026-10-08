using System.Text.Json;
using Aurelian.GameMenus;
using Aurelian.Machina;
using Aurelian.Playtesting;
using InputMan.Core;
using Xunit;

namespace Aurelian.Beacon3D.Tests;

public sealed class BeaconPlaytestingTests
{
    private sealed class Fixture : IDisposable
    {
        public string Output { get; } = Path.Combine(AppContext.BaseDirectory, "playtest-tests", Guid.NewGuid().ToString("N"));
        public BeaconPlaytestTarget Target { get; }
        public PlaytestRunner<BeaconPlaytestObservation> Runner { get; }

        public Fixture()
        {
            var font = AurelianNativeUiFont.Create(Path.Combine(AppContext.BaseDirectory, "Assets"));
            Target = new BeaconPlaytestTarget(new GameMenuView(font));
            Runner = new PlaytestRunner<BeaconPlaytestObservation>(Target, Output,
                BeaconPlaytestJsonContext.Default.BeaconPlaytestObservation);
        }

        public void Dispose()
        {
            Runner.Dispose();
            Target.Dispose();
        }
    }

    [Fact]
    public void SharedRunnerDrivesRealMouseBindingsAndNativeUtilityReports()
    {
        using var fixture = new Fixture();
        fixture.Runner.ExecuteLine("click-action new-game");
        fixture.Runner.ExecuteLine("mouse-delta 100 -20");
        fixture.Runner.ExecuteLine("tap space");
        BeaconPlaytestObservation first = fixture.Runner.ExecuteLine("wait-seconds 0.1");
        Assert.Equal(0.25f, first.Yaw, 5);
        Assert.Equal(-0.03f, first.Pitch, 5);
        Assert.True(first.Height > 0);
        fixture.Runner.ExecuteLine("mouse-hold primary 0.2");
        BeaconPlaytestObservation observation = fixture.Runner.ExecuteLine("wait-seconds 2");
        Assert.True(observation.Shots > 0);
        Assert.Equal(2, observation.Brains.Agents.Length);
        var decision = Assert.Single(observation.Brains.Trace.Where(entry => entry.Decision is not null).Take(1)).Decision!;
        Assert.Equal("Chase", decision.BestId);
        Assert.Contains(decision.Scores, score => score.Id == "Chase" && score.Score == 0.5f);
        Assert.Contains(decision.Scores, score => score.Id == "Attack" && score.Score == 0);
        Assert.Contains(observation.Brains.Trace, entry => entry.Kind == "return");
        Assert.Contains(observation.Brains.Changes, entry => entry.Key == "Beacon.Decision.Intent");
        Assert.Equal(observation.PolicyHash, fixture.Target.Observe().PolicyHash);
    }

    [Fact]
    public void RewindAndReplayReproduceWorldAndKernelHistoryIncludingHeldInput()
    {
        using var fixture = new Fixture();
        fixture.Runner.ExecuteLine("click-action new-game");
        fixture.Runner.ExecuteLine("wait-seconds 2");
        fixture.Runner.ExecuteLine("press w");
        fixture.Runner.ExecuteLine("mark origin");
        BeaconPlaytestObservation origin = fixture.Runner.ExecuteLine("checkpoint origin");
        BeaconPlaytestObservation future = Branch(fixture.Runner);
        Assert.NotEqual(origin.SemanticHash, future.SemanticHash);
        BeaconPlaytestObservation rewound = fixture.Runner.ExecuteLine("rewind origin");
        Assert.Equal(origin.SemanticHash, rewound.SemanticHash);
        Assert.Equal(origin.PolicyHash, rewound.PolicyHash);
        fixture.Runner.ExecuteLine("assert unchanged origin");
        BeaconPlaytestObservation repeated = Branch(fixture.Runner);
        Assert.Equal(future.SemanticHash, repeated.SemanticHash);
        Assert.Equal(future.PolicyHash, repeated.PolicyHash);
        Assert.True(repeated.Z < origin.Z);
        using JsonDocument input = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture.Output, "inputs.json")));
        Assert.DoesNotContain(input.RootElement.GetProperty("steps").EnumerateArray(), step => step.GetProperty("kind").GetString() == "rewind");
    }

    private static BeaconPlaytestObservation Branch(PlaytestRunner<BeaconPlaytestObservation> runner)
    {
        runner.ExecuteLine("mouse-delta 80 0");
        runner.ExecuteLine("mouse-hold primary 0.3");
        return runner.ExecuteLine("wait-seconds 0.2");
    }

    [Fact]
    public void FailureReleasesHeldInputAndFocusLossCancelsPendingActions()
    {
        using var fixture = new Fixture();
        fixture.Runner.ExecuteLine("click-action new-game");
        fixture.Runner.ExecuteLine("press w");
        fixture.Runner.ExecuteLine("mouse-press primary");
        Assert.Throws<InvalidOperationException>(() => fixture.Runner.ExecuteLine("assert screen Title"));
        BeaconPlaytestObservation before = fixture.Target.Observe();
        BeaconPlaytestObservation after = fixture.Runner.ExecuteLine("wait-seconds 0.1");
        Assert.Equal(before.X, after.X);
        Assert.Equal(before.Z, after.Z);
        Assert.Equal(before.Shots, after.Shots);
        fixture.Runner.ExecuteLine("mouse-delta 400 0");
        fixture.Runner.ExecuteLine("focus off");
        fixture.Runner.ExecuteLine("focus on");
        fixture.Runner.ExecuteLine("click-action resume");
        Assert.Equal(after.Yaw, fixture.Runner.ExecuteLine("wait-seconds 0.1").Yaw);
        using var trace = new FileStream(Path.Combine(fixture.Output, "trace.jsonl"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(trace);
        Assert.Contains("failed assertion", reader.ReadToEnd());
    }

    [Fact]
    public void InvalidScriptIsRejectedBeforeStartingGame()
    {
        using var fixture = new Fixture();
        Assert.Throws<FormatException>(() => fixture.Runner.Run(new PlaytestScript(1,
            [new("click-action", Text: "new-game"), new("mouse-delta", X: double.NaN)])));
        Assert.Equal("Title", fixture.Target.Observe().Screen);
        Assert.Throws<InvalidOperationException>(() => fixture.Runner.ExecuteLine("rewind missing"));
    }
}
