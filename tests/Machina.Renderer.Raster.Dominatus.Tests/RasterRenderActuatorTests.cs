using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using Machina.Core.Authoring;
using Machina.Core.Actions;
using Machina.Core.Lowering;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Dominatus.Rendering.Bridge;
using Machina.Dominatus.Rendering.Commands;
using Machina.Layout.Compilation;
using Machina.Layout.Documents;
using Machina.Layout.Geometry;
using Machina.Layout.Resolving;
using Machina.Renderer.Raster.Colors;
using Machina.Renderer.Raster.Dominatus.Actuation;
using Machina.Renderer.Raster.Text;
using Machina.Standard.Authoring;
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
    public void DrawText_IsUnsupportedWithoutTextRasterizer()
    {
        var recorder = new RasterRenderRecorder();
        var handler = new RasterRenderActuationHandler(recorder, new RasterRenderOptions());

        var ex = Assert.Throws<NotSupportedException>(() =>
            handler.Handle(new ActuatorHost(), CreateContext(new ActuatorHost()), default, new DrawTextCommand("id", new Rect(0, 0, 1, 1), "hi", new TextStyle())));

        Assert.Equal("DrawTextCommand is not supported because no text rasterizer is registered.", ex.Message);
    }

    [Fact]
    public void DrawText_IsSupportedWithDebugBitmapTextRasterizer()
    {
        var recorder = new RasterRenderRecorder();
        var host = new ActuatorHost().AddRasterRenderer(recorder, new RasterRenderOptions(new DebugBitmapTextRasterizer()));
        var ctx = CreateContext(host);

        host.Dispatch(ctx, new BeginFrameCommand(40, 20));
        host.Dispatch(ctx, new DrawTextCommand("title", new Rect(0, 0, 40, 20), "Hi", new TextStyle(ColorToken.White, TextSize.Md)));
        host.Dispatch(ctx, new EndFrameCommand());

        var frame = Assert.Single(recorder.CompletedFrames);
        Assert.True(CountNonTransparent(frame.Surface) > 0);
    }

    [Fact]
    public void Bridge_TextOnlyUi_RendersTextPixels()
    {
        var ui = UI.Text("Hello", id: "hello", color: ColorToken.White);
        var commands = BuildCommands(ui, 80, 32);

        var recorder = new RasterRenderRecorder();
        var host = new ActuatorHost().AddRasterRenderer(recorder, new RasterRenderOptions(new DebugBitmapTextRasterizer()));
        var ctx = CreateContext(host);
        foreach (var command in commands)
        {
            host.Dispatch(ctx, command);
        }

        var frame = Assert.Single(recorder.CompletedFrames);
        Assert.True(CountNonTransparent(frame.Surface) > 0);
    }

    [Fact]
    public void Bridge_StandardUi_RendersRectsAndTextAndPpm()
    {
        var ui = StandardUI.Card(
            id: "card",
            child: UI.Column(
                id: "content",
                gap: 8,
                children:
                [
                    UI.Text("Profile", id: "title", size: TextSize.H1),
                    StandardUI.Button("Save", id: "save", action: UiAction.Named("save")),
                ]));

        var commands = BuildCommands(ui, 160, 80);

        var recorder = new RasterRenderRecorder();
        var host = new ActuatorHost().AddRasterRenderer(recorder, new RasterRenderOptions(new DebugBitmapTextRasterizer()));
        var ctx = CreateContext(host);

        foreach (var command in commands)
        {
            host.Dispatch(ctx, command);
        }

        var frame = Assert.Single(recorder.CompletedFrames);
        var ppm = frame.ToPpm();

        Assert.True(CountNonTransparent(frame.Surface) > 0);
        Assert.StartsWith("P6\n160 80\n255\n", System.Text.Encoding.ASCII.GetString(ppm[..15]));
        Assert.True(ppm.Length > 15);
    }

    [Fact]
    public void DrawText_BeforeBeginFrame_FailsWithInvalidSequence()
    {
        var recorder = new RasterRenderRecorder();
        var host = new ActuatorHost().AddRasterRenderer(recorder, new RasterRenderOptions(new DebugBitmapTextRasterizer()));
        var ctx = CreateContext(host);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            host.Dispatch(ctx, new DrawTextCommand("id", new Rect(0, 0, 40, 20), "Hello", new TextStyle())));

        Assert.Equal("Cannot draw text without an active frame.", ex.Message);
    }

    [Fact]
    public void PushClipAndPopClip_RemainUnsupported()
    {
        var recorder = new RasterRenderRecorder();
        var handler = new RasterRenderActuationHandler(recorder, new RasterRenderOptions(new DebugBitmapTextRasterizer()));

        var pushEx = Assert.Throws<NotSupportedException>(() =>
            handler.Handle(new ActuatorHost(), CreateContext(new ActuatorHost()), default, new PushClipCommand("clip", new Rect(0, 0, 1, 1))));
        var popEx = Assert.Throws<NotSupportedException>(() =>
            handler.Handle(new ActuatorHost(), CreateContext(new ActuatorHost()), default, new PopClipCommand()));

        Assert.Equal("PushClipCommand is not supported by Raster M0b.", pushEx.Message);
        Assert.Equal("PopClipCommand is not supported by Raster M0b.", popEx.Message);
    }

    private static IReadOnlyList<IActuationCommand> BuildCommands(UiNode ui, int width, int height)
    {
        var lowering = UiLowerer.Lower(ui);
        var resolved = ResolveLayout(lowering, width, height);
        return MachinaRenderBridge.BuildCommands(lowering, resolved, new MachinaRenderOptions(width, height));
    }

    private static ResolvedLayoutDocument ResolveLayout(UiLoweringResult lowering, int width, int height)
    {
        var document = LayoutCompiler.CompileLayoutRows(lowering.Rows);
        return LayoutDocumentResolver.ResolveLayoutDocument(document, new Rect(0, 0, width, height));
    }

    private static int CountNonTransparent(Machina.Renderer.Raster.Surface.RasterSurface surface)
    {
        var count = 0;
        for (var y = 0; y < surface.Height; y++)
        {
            for (var x = 0; x < surface.Width; x++)
            {
                if (surface.GetPixel(x, y).A > 0)
                {
                    count++;
                }
            }
        }

        return count;
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
