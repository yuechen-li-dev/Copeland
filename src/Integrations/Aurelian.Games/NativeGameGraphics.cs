using System.Numerics;
using System.Runtime.InteropServices;
using Aurelian.GameMenus;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.Presentation;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Machina;
using Silk.NET.Core.Native;
using Silk.NET.Windowing;
using SkiaSharp;

namespace Aurelian.Games;

/// <summary>Shared fixed-surface Vulkan graphics lifetime, used by games and the starter host.</summary>
public sealed class NativeGameGraphics : IDisposable
{
    private readonly Stack<IDisposable> owned = new();

    public NativeGameGraphics(IWindow window, string title, bool visible, GameAssets? assets = null, bool enableRayQueries = false)
    {
        assets ??= new();
        try
        {
            var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(
                EnableValidation: true, ApplicationName: title, EnablePresentation: true,
                RequiredPresentationInstanceExtensions: ReadRequiredExtensions(window), EnableRayQueries: enableRayQueries));
            if (!initialized.Success && enableRayQueries)
            {
                Console.Error.WriteLine("Hardware ray queries unavailable; using CPU queries: "
                    + string.Join("; ", initialized.Diagnostics.Select(item => item.Message)));
                initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(
                    EnableValidation: true, ApplicationName: title, EnablePresentation: true,
                    RequiredPresentationInstanceExtensions: ReadRequiredExtensions(window)));
            }
            if (!initialized.Success)
            {
                throw new InvalidOperationException(string.Join("; ", initialized.Diagnostics.Select(item => item.Message)));
            }
            Plant = Own(initialized.Plant!);
            var created = VulkanSwapchainFactory.Create(Plant, window,
                new VulkanSwapchainCreateOptions((uint)window.Size.X, (uint)window.Size.Y, VSync: true, title, Visible: visible));
            if (!created.Success)
            {
                created.Surface?.Dispose();
                throw new InvalidOperationException(string.Join("; ", created.Diagnostics.Select(item => item.Message)));
            }
            Own(created.Surface!);
            Swapchain = Own(created.Swapchain!);
            VulkanTextureFormat format = Swapchain.Facts.SelectedFormat switch
            {
                "R8G8B8A8Unorm" => VulkanTextureFormat.Rgba8Unorm,
                "B8G8R8A8Unorm" => VulkanTextureFormat.Bgra8Unorm,
                "R8G8B8A8Srgb" => VulkanTextureFormat.Rgba8Srgb,
                "B8G8R8A8Srgb" => VulkanTextureFormat.Bgra8Srgb,
                _ => throw new NotSupportedException("Unsupported swapchain format: " + Swapchain.Facts.SelectedFormat),
            };
            Target = Own(new VulkanNativeFrameTarget(Plant, Swapchain.Facts.Width, Swapchain.Facts.Height, format));
            Renderer = Own(new VulkanSolid3DRenderer(Plant, assets.Shader("Solid3D.v.ts"), Target,
                modelProgram: assets.Shader("StaticModel3D.v.ts")));
            Presenter = Own(new VulkanNativeSwapchainPresenter(Plant, Target, Swapchain));
            Font = AurelianNativeUiFont.Create(assets.FontDirectory());
            Menus = Own(new GameMenuNativePresenter(Plant, Target,
                assets.Shader("AnalyticShape2D.v.ts"), assets.Shader("MsdfText.v.ts"), Font));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public AurelianVulkanPlant Plant { get; }
    public AurelianVulkanSwapchain Swapchain { get; }
    public VulkanNativeFrameTarget Target { get; }
    public VulkanSolid3DRenderer Renderer { get; }
    public VulkanNativeSwapchainPresenter Presenter { get; }
    public AurelianNativeUiFont Font { get; }
    public GameMenuNativePresenter Menus { get; }
    public NativeFrameClearColor Clear { get; set; } = new(0.055f, 0.095f, 0.15f, 1);

    public Native3DFrameResult Render(Native3DVertex[] vertices, Matrix4x4 camera, bool capture = false) =>
        Renderer.Render(vertices, camera, Clear, capture);

    public Native3DFrameResult Render(Native3DScene scene, Matrix4x4 camera, Vector3 eye, bool capture = false) =>
        Renderer.Render(scene, camera, eye, Clear, capture);

    public static void WritePng(string path, int width, int height, byte[] pixels)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream file = File.Create(path);
        data.SaveTo(file);
    }

    public void Dispose()
    {
        while (owned.TryPop(out IDisposable? item))
        {
            item.Dispose();
        }
    }

    private T Own<T>(T resource) where T : IDisposable
    {
        owned.Push(resource);
        return resource;
    }

    private static unsafe IReadOnlyList<string> ReadRequiredExtensions(IWindow window)
    {
        var surface = window.VkSurface ?? throw new InvalidOperationException("Window has no Vulkan surface.");
        byte** extensions = surface.GetRequiredExtensions(out uint count);
        var names = new List<string>();
        for (int index = 0; index < count; index++)
        {
            names.Add(SilkMarshal.PtrToString((nint)extensions[index], NativeStringEncoding.UTF8)
                ?? throw new InvalidOperationException("Window requested a null Vulkan extension."));
        }
        return names;
    }
}
