using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using Machina.Core.Authoring;
using Machina.Core.Lowering;
using Machina.Core.Styling;
using Machina.Dominatus.Rendering.Bridge;
using Machina.Dominatus.Rendering.Commands;
using Machina.Layout.Compilation;
using Machina.Layout.Documents;
using Machina.Layout.Geometry;
using Machina.Layout.Resolving;
using Machina.Renderer.Raster.Colors;
using Machina.Renderer.Raster.Dominatus.Actuation;
using Xunit;

namespace Machina.Renderer.Raster.Dominatus.Tests;

public sealed class RasterRenderActuatorTests
{
    [Fact]
    public void RegisterRasterRenderer_AllowsDispatchingRegisteredCommands()
    {
        var recorder = new RasterRenderRecorder();
        var host = new ActuatorHost().AddRasterRenderer(recorder);
        var ctx = CreateContext(host);

        var beginResult = host.Dispatch(ctx, new BeginFrameCommand(2, 2));
        var fillResult = host.Dispatch(ctx, new FillRectCommand("id", new Rect(0, 0, 1, 1), ColorToken.White));
        var endResult = host.Dispatch(ctx, new EndFrameCommand());

        Assert.True(beginResult.Accepted && beginResult.Completed && beginResult.Ok);
        Assert.True(fillResult.Accepted && fillResult.Completed && fillResult.Ok);
        Assert.True(endResult.Accepted && endResult.Completed && endResult.Ok);
        Assert.Single(recorder.CompletedFrames);
    }

    [Fact]
    public void RenderNode_EmitsPixelsAndCompletesFrame()
    {
        var recorder = new RasterRenderRecorder();
        var host = new ActuatorHost().AddRasterRenderer(recorder);
        var world = new AiWorld(host);
        var graph = new HfsmGraph { Root = new StateId("Root") }.Add(new StateId("Root"), RenderOneFrame);
        world.Add(new AiAgent(new HfsmInstance(graph)));

        RunTicksUntil(world, () => recorder.CompletedFrames.Count == 1, 10);

        var frame = Assert.Single(recorder.CompletedFrames);
        Assert.Equal(4, frame.Width);
        Assert.Equal(4, frame.Height);
        Assert.Equal(new Rgba32(255, 0, 0, 255), frame.Surface.GetPixel(1, 1));
        Assert.Equal(new Rgba32(255, 0, 0, 255), frame.Surface.GetPixel(2, 2));
        Assert.Equal(Rgba32.Transparent, frame.Surface.GetPixel(0, 0));
    }

    [Fact]
    public void MultipleFillRects_ComposeWithAlpha()
    {
        var recorder = new RasterRenderRecorder();
        recorder.BeginFrame(2, 1);
        recorder.FillRect("bg", new Rect(0, 0, 2, 1), ColorToken.Hex(0x000000FF));
        recorder.FillRect("fg", new Rect(0, 0, 2, 1), ColorToken.Hex(0xFF000080));
        recorder.EndFrame();

        var frame = Assert.Single(recorder.CompletedFrames);
        var pixel = frame.Surface.GetPixel(0, 0);
        Assert.Equal(new Rgba32(128, 0, 0, 255), pixel);
    }

    [Fact]
    public void PpmOutput_UsesCompletedFrame()
    {
        var recorder = new RasterRenderRecorder();
        recorder.BeginFrame(2, 1);
        recorder.FillRect("left", new Rect(0, 0, 1, 1), ColorToken.Hex(0xFF0000FF));
        recorder.FillRect("right", new Rect(1, 0, 1, 1), ColorToken.Hex(0x00FF00FF));
        recorder.EndFrame();

        var ppm = recorder.LastFrame!.ToPpm();
        var header = System.Text.Encoding.ASCII.GetString(ppm[..11]);
        Assert.Equal("P6\n2 1\n255\n", header);
        Assert.Equal(new byte[] { 255, 0, 0, 0, 255, 0 }, ppm[11..]);
    }

    [Fact]
    public void Bridge_RectOnlyUi_RendersWithoutTextCommands()
    {
        var ui = UI.Rect(id: "panel", width: 4, height: 4, color: ColorToken.Hex(0xFF0000FF));
        var lowering = UiLowerer.Lower(ui);
        var resolved = ResolveLayout(lowering, 4, 4);
        var commands = MachinaRenderBridge.BuildCommands(lowering, resolved, new MachinaRenderOptions(4, 4));

        Assert.DoesNotContain(commands, c => c is DrawTextCommand);

        var recorder = new RasterRenderRecorder();
        var host = new ActuatorHost().AddRasterRenderer(recorder);
        var ctx = CreateContext(host);
        foreach (var command in commands)
        {
            host.Dispatch(ctx, command);
        }

        var frame = Assert.Single(recorder.CompletedFrames);
        Assert.Equal(new Rgba32(255, 0, 0, 255), frame.Surface.GetPixel(0, 0));
    }

    [Fact]
    public void DrawText_IsExplicitlyUnsupported()
    {
        var recorder = new RasterRenderRecorder();
        var handler = new RasterRenderActuationHandler(recorder);
        var ex = Assert.Throws<NotSupportedException>(() =>
            handler.Handle(new ActuatorHost(), CreateContext(new ActuatorHost()), default, new DrawTextCommand("id", new Rect(0, 0, 1, 1), "hi", new TextStyle())));

        Assert.Equal("DrawTextCommand is not supported by Raster M0b. Text rendering is deferred to M0c.", ex.Message);
    }

    private static IEnumerator<AiStep> RenderOneFrame(AiCtx ctx)
    {
        yield return Ai.Act(new BeginFrameCommand(4, 4));
        yield return Ai.Act(new FillRectCommand("panel", new Rect(1, 1, 2, 2), ColorToken.Hex(0xFF0000FF)));
        yield return Ai.Act(new EndFrameCommand());
        yield return Ai.Succeed();
    }

    private static ResolvedLayoutDocument ResolveLayout(UiLoweringResult lowering, int width, int height)
    {
        var document = LayoutCompiler.CompileLayoutRows(lowering.Rows);
        return LayoutDocumentResolver.ResolveLayoutDocument(document, new Rect(0, 0, width, height));
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

    private static IEnumerator<AiStep> Idle()
    {
        yield return Ai.Succeed();
    }
}
