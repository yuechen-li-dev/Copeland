using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Lowering;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Dominatus.Rendering.Bridge;
using Machina.Layout.Compilation;
using Machina.Layout.Geometry;
using Machina.Layout.Resolving;
using Machina.Renderer.Raster.Dominatus;
using Machina.Renderer.Raster.Dominatus.Actuation;
using Machina.Renderer.Raster.Dominatus.Models;
using Machina.Renderer.Raster.Text;
using Machina.Standard.Authoring;
using Machina.Standard.Components;

namespace Machina.Presenter.Sample;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect();
    }

    private sealed class App : Application
    {
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = BuildWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }

    private static Window BuildWindow()
    {
        const int width = 640;
        const int height = 360;

        var frame = RenderUi(BuildUi(), width, height);
        var image = new Image
        {
            Source = ToBitmap(frame),
            Stretch = Stretch.None
        };

        return new Window
        {
            Title = "Machina Presenter M0a",
            Width = width,
            Height = height,
            CanResize = false,
            Content = image
        };
    }

    private static UiNode BuildUi()
    {
        return UI.Container(
            id: "root",
            child: StandardUI.Card(
                id: "counter-card",
                width: 320,
                height: 180,
                child: UI.Column(
                    id: "content",
                    gap: 12,
                    children:
                    [
                        UI.Text("Machina UI", id: "title", color: ColorToken.White, size: TextSize.H1),
                        UI.Text("Count: 0", id: "count", color: ColorToken.Gray, size: TextSize.Md),
                        StandardUI.Button("Increment", id: "increment", action: UiAction.Named("increment"))
                    ])));
    }

    private static RasterFrame RenderUi(UiNode ui, int width, int height)
    {
        var lowering = UiLowerer.Lower(ui);
        var document = LayoutCompiler.CompileLayoutRows(lowering.Rows);
        var resolved = LayoutDocumentResolver.ResolveLayoutDocument(document, new Machina.Layout.Geometry.Rect(0, 0, width, height));
        var commands = MachinaRenderBridge.BuildCommands(lowering, resolved, new MachinaRenderOptions(width, height));

        var recorder = new RasterRenderRecorder();
        var options = new RasterRenderOptions(new DebugBitmapTextRasterizer());
        var host = new ActuatorHost().AddRasterRenderer(recorder, options);
        var context = CreateContext(host);

        foreach (var command in commands)
        {
            var dispatch = host.Dispatch(context, command);
            if (!dispatch.Accepted || !dispatch.Completed || !dispatch.Ok)
            {
                throw new InvalidOperationException($"Render command dispatch failed for {command.GetType().Name}.");
            }
        }

        return recorder.CompletedFrames.Single();
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

    private static WriteableBitmap ToBitmap(RasterFrame frame)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(frame.Width, frame.Height),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Unpremul);

        using var locked = bitmap.Lock();
        var pixelBytes = ToRgbaBytes(frame);
        System.Runtime.InteropServices.Marshal.Copy(pixelBytes, 0, locked.Address, pixelBytes.Length);

        return bitmap;
    }

    private static byte[] ToRgbaBytes(RasterFrame frame)
    {
        var width = frame.Surface.Width;
        var height = frame.Surface.Height;
        var bytes = new byte[width * height * 4];
        var index = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = frame.Surface.GetPixel(x, y);
                bytes[index++] = pixel.R;
                bytes[index++] = pixel.G;
                bytes[index++] = pixel.B;
                bytes[index++] = pixel.A;
            }
        }

        return bytes;
    }
}
