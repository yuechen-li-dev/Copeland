using Xunit;
using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using Machina.Core.Styling;
using Machina.Dominatus.Rendering.Commands;
using Machina.Dominatus.Rendering.Snapshot;
using Machina.Layout.Geometry;

namespace Machina.Dominatus.Tests;

public sealed class RenderSnapshotActuatorTests
{
    [Fact]
    public void RegisterSnapshotRenderer_AllowsDispatchingRegisteredCommands()
    {
        var recorder = new RenderSnapshotRecorder();
        var host = new ActuatorHost().AddSnapshotRenderer(recorder);
        var ctx = CreateContext(host);

        var beginResult = host.Dispatch(ctx, new BeginFrameCommand(100, 80));
        var endResult = host.Dispatch(ctx, new EndFrameCommand());

        Assert.True(beginResult.Accepted);
        Assert.True(beginResult.Completed);
        Assert.True(beginResult.Ok);
        Assert.True(endResult.Accepted);
        Assert.Single(recorder.CompletedSnapshots);
    }

    [Fact]
    public void RenderNode_EmitsDeterministicSnapshotInOrder_AndCompletes()
    {
        var recorder = new RenderSnapshotRecorder();
        var host = new ActuatorHost().AddSnapshotRenderer(recorder);
        var world = new AiWorld(host);

        var graph = new HfsmGraph { Root = new StateId("Root") }
            .Add(new StateId("Root"), RenderOneFrame);

        var agent = new AiAgent(new HfsmInstance(graph));
        world.Add(agent);

        RunTicksUntil(world, () => recorder.CompletedSnapshots.Count > 0, 10);

        Assert.Single(recorder.CompletedSnapshots);
        var snapshot = recorder.LastSnapshot;
        Assert.NotNull(snapshot);
        Assert.Equal(800, snapshot!.Width);
        Assert.Equal(600, snapshot.Height);

        var expected = new[]
        {
            "beginFrame w=800 h=600",
            "fillRect id=panel x=10 y=20 w=300 h=200 color=#101820FF",
            "drawText id=title x=20 y=30 w=100 h=20 text=\"Hello\" color=#FFFFFFFF size=H1",
            "endFrame"
        };

        Assert.Equal(expected, snapshot.Commands);
        Assert.Empty(agent.InFlightActuations);
    }


    [Fact]
    public void SnapshotRecorder_RecordsStrokeRect()
    {
        var recorder = new RenderSnapshotRecorder();

        recorder.Record(new BeginFrameCommand(10, 10));
        recorder.Record(new StrokeRectCommand("border", new Rect(1, 1, 8, 8), ColorToken.White, 2));
        recorder.Record(new EndFrameCommand());

        var snapshot = recorder.LastSnapshot;
        Assert.NotNull(snapshot);
        Assert.Contains(snapshot!.Commands, line => line.Contains("strokeRect", StringComparison.Ordinal));
        Assert.Contains(snapshot.Commands, line => line.Contains("id=border", StringComparison.Ordinal));
        Assert.Contains(snapshot.Commands, line => line.Contains("x=1 y=1 w=8 h=8", StringComparison.Ordinal));
        Assert.Contains(snapshot.Commands, line => line.Contains("color=#FFFFFFFF", StringComparison.Ordinal));
        Assert.Contains(snapshot.Commands, line => line.Contains("thickness=2", StringComparison.Ordinal));
    }

    [Fact]
    public void ClipPushPop_IsRecordedAndBalanced()
    {
        var recorder = new RenderSnapshotRecorder();
        recorder.Record(new BeginFrameCommand(20, 20));
        recorder.Record(new PushClipCommand("clip", new Rect(0, 0, 10, 10)));
        recorder.Record(new FillRectCommand("fill", new Rect(1, 1, 5, 5), ColorToken.Gray));
        recorder.Record(new PopClipCommand());
        recorder.Record(new EndFrameCommand());

        var snapshot = recorder.LastSnapshot;
        Assert.NotNull(snapshot);
        Assert.Contains("pushClip id=clip x=0 y=0 w=10 h=10", snapshot!.Commands);
        Assert.Contains("popClip", snapshot.Commands);
    }

    [Fact]
    public void PopClipWithoutPush_Throws()
    {
        var recorder = new RenderSnapshotRecorder();
        recorder.Record(new BeginFrameCommand(10, 10));

        var ex = Assert.Throws<InvalidOperationException>(() => recorder.Record(new PopClipCommand()));
        Assert.Contains("Cannot pop clip", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EndFrameWithUnbalancedClip_Throws()
    {
        var recorder = new RenderSnapshotRecorder();
        recorder.Record(new BeginFrameCommand(10, 10));
        recorder.Record(new PushClipCommand("clip", new Rect(0, 0, 1, 1)));

        var ex = Assert.Throws<InvalidOperationException>(() => recorder.Record(new EndFrameCommand()));
        Assert.Contains("unbalanced", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DrawOutsideFrame_Throws()
    {
        var recorder = new RenderSnapshotRecorder();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            recorder.Record(new FillRectCommand("panel", new Rect(0, 0, 1, 1), ColorToken.White)));

        Assert.Contains("active frame", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MultipleFrames_AreIsolated()
    {
        var recorder = new RenderSnapshotRecorder();

        recorder.Record(new BeginFrameCommand(10, 10));
        recorder.Record(new FillRectCommand("a", new Rect(0, 0, 1, 1), ColorToken.White));
        recorder.Record(new EndFrameCommand());

        recorder.Record(new BeginFrameCommand(20, 20));
        recorder.Record(new FillRectCommand("b", new Rect(0, 0, 2, 2), ColorToken.Gray));
        recorder.Record(new EndFrameCommand());

        Assert.Equal(2, recorder.CompletedSnapshots.Count);
        Assert.Contains("id=a", recorder.CompletedSnapshots[0].Commands[1]);
        Assert.Contains("id=b", recorder.CompletedSnapshots[1].Commands[1]);
        Assert.Equal(20, recorder.LastSnapshot!.Width);
    }

    private static void RunTicksUntil(AiWorld world, Func<bool> done, int maxTicks)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            world.Tick(0.016f);
            if (done())
            {
                return;
            }
        }
    }

    private static AiCtx CreateContext(ActuatorHost host)
    {
        var graph = new HfsmGraph { Root = new StateId("Root") }
            .Add(new StateId("Root"), static _ => Idle());
        var agent = new AiAgent(new HfsmInstance(graph));
        var world = new AiWorld(host);
        world.Add(agent);
        return new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, host);
    }

    private static IEnumerator<AiStep> RenderOneFrame(AiCtx ctx)
    {
        yield return Ai.Act(new BeginFrameCommand(800, 600));
        yield return Ai.Act(new FillRectCommand("panel", new Rect(10, 20, 300, 200), ColorToken.Hex(0x101820FF)));
        yield return Ai.Act(new DrawTextCommand("title", new Rect(20, 30, 100, 20), "Hello", new TextStyle(ColorToken.White, TextSize.H1)));
        yield return Ai.Act(new EndFrameCommand());
        yield return Ai.Succeed();
    }

    private static IEnumerator<AiStep> Idle()
    {
        yield return Ai.Succeed();
    }
}
