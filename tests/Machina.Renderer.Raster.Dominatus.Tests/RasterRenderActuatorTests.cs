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
        var red = ColorToken.Hex(0xFF0000FF);

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
    public void FillRect_RespectsPushedClip()
    {
        var recorder = new RasterRenderRecorder();
        var host = new ActuatorHost().AddRasterRenderer(recorder, new RasterRenderOptions(new DebugBitmapTextRasterizer()));
        var ctx = CreateContext(host);
        var red = ColorToken.Hex(0xFF0000FF);

        host.Dispatch(ctx, new BeginFrameCommand(5, 5));
        host.Dispatch(ctx, new PushClipCommand("clip", new Rect(1, 1, 2, 2)));
        host.Dispatch(ctx, new FillRectCommand("fill", new Rect(0, 0, 5, 5), red));
        host.Dispatch(ctx, new PopClipCommand());
        host.Dispatch(ctx, new EndFrameCommand());

        var frame = Assert.Single(recorder.CompletedFrames);
        AssertOnlyRegionHasColor(frame.Surface, 1, 1, 3, 3, Rgba32.FromRgba(red.Rgba));
    }


    [Fact]
    public void StrokeRectCommand_RendersPixels()
    {
        var recorder = new RasterRenderRecorder();
        var host = new ActuatorHost().AddRasterRenderer(recorder);
        var ctx = CreateContext(host);
        var red = ColorToken.Hex(0xFF0000FF);

        host.Dispatch(ctx, new BeginFrameCommand(6, 6));
        host.Dispatch(ctx, new StrokeRectCommand("border", new Rect(1, 1, 4, 4), red, 1));
        host.Dispatch(ctx, new EndFrameCommand());

        var frame = Assert.Single(recorder.CompletedFrames);
        var color = Rgba32.FromRgba(red.Rgba);
        Assert.Equal(color, frame.Surface.GetPixel(1, 1));
        Assert.Equal(color, frame.Surface.GetPixel(4, 1));
        Assert.Equal(color, frame.Surface.GetPixel(1, 4));
        Assert.Equal(Rgba32.Transparent, frame.Surface.GetPixel(3, 3));
    }

    [Fact]
    public void StrokeRectCommand_RespectsClip()
    {
        var recorder = new RasterRenderRecorder();
        var host = new ActuatorHost().AddRasterRenderer(recorder);
        var ctx = CreateContext(host);
        var red = ColorToken.Hex(0xFF0000FF);

        host.Dispatch(ctx, new BeginFrameCommand(6, 6));
        host.Dispatch(ctx, new PushClipCommand("clip", new Rect(0, 0, 3, 6)));
        host.Dispatch(ctx, new StrokeRectCommand("border", new Rect(0, 0, 6, 6), red, 1));
        host.Dispatch(ctx, new PopClipCommand());
        host.Dispatch(ctx, new EndFrameCommand());

        var frame = Assert.Single(recorder.CompletedFrames);
        for (var y = 0; y < 6; y++)
        {
            for (var x = 3; x < 6; x++)
            {
                Assert.Equal(Rgba32.Transparent, frame.Surface.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void StrokeRectCommand_BeforeBeginFrameFails()
    {
        var recorder = new RasterRenderRecorder();
        var host = new ActuatorHost().AddRasterRenderer(recorder);
        var ctx = CreateContext(host);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            host.Dispatch(ctx, new StrokeRectCommand("id", new Rect(0, 0, 2, 2), ColorToken.White, 1)));

        Assert.Equal("Cannot stroke rectangle without an active frame.", ex.Message);
    }

    [Fact]
    public void PopClipWithoutPush_FailsDeterministically()
    {
        var recorder = new RasterRenderRecorder();
        var host = new ActuatorHost().AddRasterRenderer(recorder);
        var ctx = CreateContext(host);

        host.Dispatch(ctx, new BeginFrameCommand(5, 5));
        var ex = Assert.Throws<InvalidOperationException>(() => host.Dispatch(ctx, new PopClipCommand()));
        Assert.Equal("Cannot pop clip because the clip stack is empty.", ex.Message);
    }

    [Fact]
    public void PushClipBeforeBeginFrame_FailsDeterministically()
    {
        var recorder = new RasterRenderRecorder();
        var host = new ActuatorHost().AddRasterRenderer(recorder);
        var ctx = CreateContext(host);

        var ex = Assert.Throws<InvalidOperationException>(() => host.Dispatch(ctx, new PushClipCommand("clip", new Rect(0, 0, 1, 1))));
        Assert.Equal("Cannot push clip without an active frame.", ex.Message);
    }

    [Fact]
    public void EndFrameWithUnbalancedClip_FailsDeterministically()
    {
        var recorder = new RasterRenderRecorder();
        var host = new ActuatorHost().AddRasterRenderer(recorder);
        var ctx = CreateContext(host);

        host.Dispatch(ctx, new BeginFrameCommand(5, 5));
        host.Dispatch(ctx, new PushClipCommand("clip", new Rect(0, 0, 1, 1)));

        var ex = Assert.Throws<InvalidOperationException>(() => host.Dispatch(ctx, new EndFrameCommand()));
        Assert.Equal("Cannot end frame while clip stack is not balanced.", ex.Message);
        Assert.Empty(recorder.CompletedFrames);
    }

    [Fact]
    public void PushClipWithNaN_FailsDeterministically()
    {
        var recorder = new RasterRenderRecorder();
        var host = new ActuatorHost().AddRasterRenderer(recorder);
        var ctx = CreateContext(host);

        host.Dispatch(ctx, new BeginFrameCommand(5, 5));
        var ex = Assert.Throws<ArgumentException>(() => host.Dispatch(ctx, new PushClipCommand("clip", new Rect(double.NaN, 0, 1, 1))));
        Assert.Equal("Clip rectangle must contain finite values. (Parameter 'rect')", ex.Message);
    }

    [Fact]
    public void NestedClips_IntersectDeterministically()
    {
        var recorder = new RasterRenderRecorder();
        var host = new ActuatorHost().AddRasterRenderer(recorder);
        var ctx = CreateContext(host);
        var red = ColorToken.Hex(0xFF0000FF);

        host.Dispatch(ctx, new BeginFrameCommand(6, 6));
        host.Dispatch(ctx, new PushClipCommand("c1", new Rect(1, 1, 4, 4)));
        host.Dispatch(ctx, new PushClipCommand("c2", new Rect(3, 0, 4, 4)));
        host.Dispatch(ctx, new FillRectCommand("f", new Rect(0, 0, 6, 6), red));
        host.Dispatch(ctx, new PopClipCommand());
        host.Dispatch(ctx, new PopClipCommand());
        host.Dispatch(ctx, new EndFrameCommand());

        var frame = Assert.Single(recorder.CompletedFrames);
        AssertOnlyRegionHasColor(frame.Surface, 3, 1, 5, 4, Rgba32.FromRgba(red.Rgba));
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

    private static void AssertOnlyRegionHasColor(
        Machina.Renderer.Raster.Surface.RasterSurface surface,
        int left,
        int top,
        int rightExclusive,
        int bottomExclusive,
        Rgba32 color)
    {
        for (var y = 0; y < surface.Height; y++)
        {
            for (var x = 0; x < surface.Width; x++)
            {
                var expected = x >= left && x < rightExclusive && y >= top && y < bottomExclusive
                    ? color
                    : Rgba32.Transparent;
                Assert.Equal(expected, surface.GetPixel(x, y));
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
