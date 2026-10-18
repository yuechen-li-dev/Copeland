using System.Numerics;
using Aurelian.Rendering.Contracts.Models;
using System.Runtime.InteropServices;
using Aurelian.GameMenus;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.Presentation;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Machina;
using Aurelian.NativeComposition;
using Silk.NET.Core.Native;
using Silk.NET.Windowing;
using SkiaSharp;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Runtime.Lighting;
using System.Security.Cryptography;

namespace Aurelian.Games;

/// <summary>Shared fixed-surface Vulkan graphics lifetime, used by games and the starter host.</summary>
public sealed class NativeGameGraphics : IDisposable
{
    public EnvironmentLighting? Environment
    {
        get => Renderer.Environment;
        set => Renderer.Environment = value;
    }

    public IReadOnlyList<LocalLight3D> LocalLights
    {
        get => Renderer.LocalLights;
        set => Renderer.LocalLights = value;
    }

    public ReflectionProbe3D? ReflectionProbe
    {
        get => Renderer.ReflectionProbe;
        set => Renderer.ReflectionProbe = value;
    }

    /// <summary>Agent-owned visual snapshots. Allocate the default GPU pool on first use.</summary>
    public IReadOnlyList<ParticleEmitter3D> ParticleEmitters
    {
        get => Renderer.ParticleEmitters;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Count > 0 && Renderer.ParticleSystem is null)
            {
                Renderer.ParticleSystem = Own(new VulkanParticles3D(Plant, assets.ComputeShader("Particles3D.v.ts")));
            }
            Renderer.ParticleEmitters = value;
        }
    }

    public ParticleBudget3D? ParticleBudget => Renderer.ParticleSystem?.Inspection;

    /// <summary>
    /// Game-owned native 2D layers (sprites, tiles, game UI), drawn after the optional 3D view and before the shell's HUD
    /// and menus. Add them in <see cref="IGame.AttachGraphics"/> and remove them in <see cref="IGame.DetachGraphics"/>.
    /// They are retargeted automatically when the surface is rebuilt. A purely 2D game returns no 3D view; the first
    /// layer pass then clears to <see cref="Clear"/>.
    /// </summary>
    public NativeLayerStack Layers { get; } = new();
    private readonly Stack<IDisposable> owned = new();
    private readonly Stack<IDisposable> surfaceOwned = new();
    private readonly IWindow window;
    private readonly string title;
    private readonly bool visible;
    private readonly List<StaticModel> preparedModels = [];
    private readonly GameAssets assets;
    private readonly LightingCompilationController lighting;
    private readonly List<LightingBakeCompletion> lightingCompletions = [];
    private StaticDiffuseLighting? pendingLighting;
    private readonly string lightingShaderKey;
    private readonly string lightingCompletionDomain = "compiled-diffuse-upload-" + Guid.NewGuid().ToString("N");
    private bool hasLightingTarget;
    private bool lightingPublished;
    public string? LightingDiagnostic { get; private set; } = "NoLightingAsset";
    public LightingCompilationStatus? LightingStatus => hasLightingTarget ? lighting.Observe("scene.diffuse") : null;
    public Aurelian.Runtime.Dominatus.Inspection.DominatusInspector LightingInspector => lighting.Inspector;

    public NativeGameGraphics(IWindow window, string title, bool visible, GameAssets? assets = null, bool enableRayQueries = false,
        bool vSync = true)
    {
        assets ??= new();
        this.assets = assets;
        this.window = window ?? throw new ArgumentNullException(nameof(window));
        this.title = title;
        this.visible = visible;
        VSync = vSync;
        lightingShaderKey = LightingProgramIdentity.Compute(assets.Shader("SurfaceSolid3D.v.ts"));
        lighting = new LightingCompilationController(request =>
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            ulong fence = Renderer!.UploadStaticDiffuseLighting(pendingLighting
                ?? throw new InvalidDataException("NoValidatedLightingAsset"));
            var ticket = new LightingBakeTicket(request.PlantId, fence, request.Compilation.ContentKey,
                request.Generation, lightingCompletionDomain);
            lightingCompletions.Add(new(ticket, true, true, watch.Elapsed.TotalMilliseconds));
            return ticket;
        });
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
            Font = AurelianNativeUiFont.Create(assets.FontDirectory());
            CreateSurfaceResources(ReadFramebufferExtent());
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public AurelianVulkanPlant Plant { get; }

    // Surface-lifetime resources. Resize and Reconfigure replace these instances; callers must not retain them across either call.
    public AurelianVulkanSwapchain Swapchain { get; private set; } = null!;
    public VulkanNativeFrameTarget Target { get; private set; } = null!;
    public VulkanSolid3DRenderer Renderer { get; private set; } = null!;
    public VulkanNativeSwapchainPresenter Presenter { get; private set; } = null!;
    public GameMenuNativePresenter Menus { get; private set; } = null!;
    public AurelianNativeUiFont Font { get; }
    public bool VSync { get; private set; }
    public int Width => (int)Target.Width;
    public int Height => (int)Target.Height;

    /// <summary>Number of times surface-lifetime resources were rebuilt after construction.</summary>
    public int SurfaceRebuilds { get; private set; }

    /// <summary>
    /// Rebuild the swapchain, frame target, renderer and overlay presenter for a new framebuffer extent or present mode.
    /// The Vulkan plant, compiled programs, font atlas, particle pool and published lighting are retained.
    /// Returns false when nothing changed or the extent is empty (for example, a minimized window).
    /// </summary>
    public bool Reconfigure(int width, int height, bool vSync)
    {
        if (width <= 0 || height <= 0)
        {
            return false;
        }
        if (width == Width && height == Height && vSync == VSync && !SurfaceOutOfDate)
        {
            return false;
        }
        SurfaceOutOfDate = false;
        VSync = vSync;
        RenderState state = CaptureRenderState();
        DisposeSurfaceResources();
        CreateSurfaceResources((width, height));
        RestoreRenderState(state);
        SurfaceRebuilds++;
        return true;
    }

    public bool Resize(int width, int height) => Reconfigure(width, height, VSync);

    /// <summary>True after a present found the swapchain out of date; the next <see cref="Reconfigure"/> rebuilds even at the same size.</summary>
    public bool SurfaceOutOfDate { get; private set; }

    /// <summary>Present the frame target. Returns false (and marks the surface for rebuild) if the swapchain went out of date.</summary>
    public bool Present(ulong frameId)
    {
        if (Presenter.TryPresent(frameId))
        {
            return true;
        }
        SurfaceOutOfDate = true;
        return false;
    }

    /// <summary>Follow the window's current framebuffer extent.</summary>
    public bool ResizeToWindow()
    {
        (int width, int height) = ReadFramebufferExtent();
        return Resize(width, height);
    }

    /// <summary>Prepare a model on the current renderer and on any renderer created by a later resize.</summary>
    public void Prepare(StaticModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Renderer.Prepare(model);
        if (!preparedModels.Contains(model))
        {
            preparedModels.Add(model);
        }
    }

    public Aurelian.Rendering.Contracts.Models.Graphics3DSettings Settings
    {
        get => Renderer.Settings;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            value.Validate();
            Renderer.Settings = value;
            if (pendingLighting is not null && Vector3.Distance(Vector3.Normalize(value.SunDirection), pendingLighting.SunDirection) >= .00001f)
            {
                InvalidateLighting("StaticSunDirectionChanged");
            }
        }
    }
    public NativeFrameClearColor Clear { get; set; } = new(0.055f, 0.095f, 0.15f, 1);

    public bool LoadLighting(string path, string expectedSceneKey)
    {
        Renderer.PublishStaticDiffuseLighting(false);
        lightingPublished = false;
        try
        {
            pendingLighting = assets.Lighting(path, expectedSceneKey);
            string dataKey = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(pendingLighting.TextureData().AsSpan())));
            var compilation = new LightingCompilation("scene.diffuse", LightingArtifactKind.DiffuseTransfer,
                new(expectedSceneKey, dataKey, expectedSceneKey, pendingLighting.CompilerKey, lightingShaderKey),
                1, 1, 1, 1, linearLightResponse: true);
            lighting.SetTarget(compilation);
            hasLightingTarget = true;
            for (int tick = 0; tick < 8; tick++)
            {
                LightingBakeCompletion[] observed = lightingCompletions.ToArray();
                lightingCompletions.Clear();
                lighting.Tick([new(0, true, new HashSet<LightingArtifactKind> { LightingArtifactKind.DiffuseTransfer }, 0)],
                    observed, new(100, 1000));
                var status = lighting.Observe("scene.diffuse");
                if (status.Published is not null)
                {
                    Renderer.PublishStaticDiffuseLighting(true);
                    lightingPublished = true;
                    LightingDiagnostic = null;
                    return true;
                }
                if (status.Phase == "Failed") throw new InvalidDataException(status.Reason);
            }
            throw new InvalidOperationException("Lighting publication did not converge within bounded upload ticks.");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException
            or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            LightingDiagnostic = exception.Message;
            pendingLighting = null;
            if (hasLightingTarget) lighting.Invalidate("scene.diffuse", "LightingAssetRejected: " + exception.Message);
            return false;
        }
    }

    public void InvalidateLighting(string reason)
    {
        Renderer.PublishStaticDiffuseLighting(false);
        lightingPublished = false;
        pendingLighting = null;
        LightingDiagnostic = reason;
        if (hasLightingTarget) lighting.Invalidate("scene.diffuse", reason);
    }

    public Native3DFrameResult Render(Native3DVertex[] vertices, Matrix4x4 camera, bool capture = false) =>
        Renderer.Render(vertices, camera, Clear, capture);

    public Native3DFrameResult Render(Native3DScene scene, Matrix4x4 camera, Vector3 eye, bool capture = false,
        NativeGpuGeometry3D? gpuGeometry = null) =>
        Renderer.Render(scene, camera, eye, Clear, capture, gpuGeometry);

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
        // Layers hold GPU resources created against the plant; release them while the device is still alive.
        _ = Plant?.Vk.DeviceWaitIdle(Plant.Device);
        Layers.Dispose();
        DisposeSurfaceResources();
        while (owned.TryPop(out IDisposable? item))
        {
            item.Dispose();
        }
    }

    private sealed record RenderState(
        Aurelian.Rendering.Contracts.Models.Graphics3DSettings Settings,
        EnvironmentLighting? Environment,
        ReflectionProbe3D? ReflectionProbe,
        IReadOnlyList<LocalLight3D> LocalLights,
        VulkanParticles3D? ParticleSystem,
        IReadOnlyList<ParticleEmitter3D> ParticleEmitters,
        float StaticEmissionIntensity);

    private RenderState CaptureRenderState()
    {
        return new RenderState(Renderer.Settings, Renderer.Environment, Renderer.ReflectionProbe, Renderer.LocalLights,
            Renderer.ParticleSystem, Renderer.ParticleEmitters, Renderer.StaticEmissionIntensity);
    }

    private void RestoreRenderState(RenderState state)
    {
        Renderer.Settings = state.Settings;
        Renderer.Environment = state.Environment;
        Renderer.ReflectionProbe = state.ReflectionProbe;
        Renderer.LocalLights = state.LocalLights;
        Renderer.ParticleSystem = state.ParticleSystem;
        Renderer.ParticleEmitters = state.ParticleEmitters;
        Renderer.StaticEmissionIntensity = state.StaticEmissionIntensity;
        foreach (StaticModel model in preparedModels)
        {
            Renderer.Prepare(model);
        }
        if (pendingLighting is not null)
        {
            // The lighting asset was already validated and published; only its GPU copy belonged to the old renderer.
            Renderer.UploadStaticDiffuseLighting(pendingLighting);
            Renderer.PublishStaticDiffuseLighting(lightingPublished);
        }
    }

    private void CreateSurfaceResources((int Width, int Height) extent)
    {
        try
        {
            var created = VulkanSwapchainFactory.Create(Plant, window,
                new VulkanSwapchainCreateOptions((uint)extent.Width, (uint)extent.Height, VSync, title, Visible: visible));
            if (!created.Success)
            {
                created.Surface?.Dispose();
                throw new InvalidOperationException(string.Join("; ", created.Diagnostics.Select(item => item.Message)));
            }
            OwnSurface(created.Surface!);
            Swapchain = OwnSurface(created.Swapchain!);
            VulkanTextureFormat format = Swapchain.Facts.SelectedFormat switch
            {
                "R8G8B8A8Unorm" => VulkanTextureFormat.Rgba8Unorm,
                "B8G8R8A8Unorm" => VulkanTextureFormat.Bgra8Unorm,
                "R8G8B8A8Srgb" => VulkanTextureFormat.Rgba8Srgb,
                "B8G8R8A8Srgb" => VulkanTextureFormat.Bgra8Srgb,
                _ => throw new NotSupportedException("Unsupported swapchain format: " + Swapchain.Facts.SelectedFormat),
            };
            Target = OwnSurface(new VulkanNativeFrameTarget(Plant, Swapchain.Facts.Width, Swapchain.Facts.Height, format));
            Renderer = OwnSurface(new VulkanSolid3DRenderer(Plant, assets.Shader("SurfaceSolid3D.v.ts"), Target,
                modelProgram: assets.Shader("SurfaceModel3D.v.ts"), shadowProgram: assets.Shader("Shadow3D.v.ts"),
                outputProgram: assets.Shader("ToneMap3D.v.ts"), temporalProgram: assets.Shader("TemporalResolve3D.v.ts"),
                bloomProgram: assets.Shader("Bloom3D.v.ts"),
                surfacePrograms: new(assets.Shader("SurfaceResolve3D.v.ts"), assets.Shader("AmbientOcclusion3D.v.ts"),
                    assets.Shader("AmbientDenoise3D.v.ts"), assets.Shader("LightTiles3D.v.ts"))
                {
                    HeightFog = assets.Shader("HeightFog3D.v.ts"),
                    VolumeInject = assets.Shader("VolumeInject3D.v.ts"),
                    VolumeIntegrate = assets.Shader("VolumeIntegrate3D.v.ts"),
                    VolumeResolve = assets.Shader("VolumeResolve3D.v.ts"),
                    RefractiveModel = assets.Shader("RefractiveModel3D.v.ts"),
                    RefractionResolve = assets.Shader("RefractionResolve3D.v.ts"),
                    WaterTemporal = assets.Shader("TemporalResolve3D.v.ts"),
                    SubsurfaceDiffuse = assets.Shader("SubsurfaceDiffuse3D.v.ts"),
                    SubsurfaceMerge = assets.Shader("SubsurfaceMerge3D.v.ts"),
                    TransparentModel = assets.Shader("TransparentModel3D.v.ts"),
                    TransparencyResolve = assets.Shader("TransparencyResolve3D.v.ts"),
                    ParticleBillboards = assets.Shader("ParticleBillboard3D.v.ts"),
                }));
            Presenter = OwnSurface(new VulkanNativeSwapchainPresenter(Plant, Target, Swapchain));
            Menus = OwnSurface(new GameMenuNativePresenter(Plant, Target,
                assets.Shader("AnalyticShape2D.v.ts"), assets.Shader("MsdfText.v.ts"), Font));
            Layers.Retarget(Target);
        }
        catch
        {
            DisposeSurfaceResources();
            throw;
        }
    }

    private void DisposeSurfaceResources()
    {
        if (surfaceOwned.Count == 0)
        {
            return;
        }
        // Presentation may still reference swapchain images; drain the device before releasing them.
        _ = Plant.Vk.DeviceWaitIdle(Plant.Device);
        while (surfaceOwned.TryPop(out IDisposable? item))
        {
            item.Dispose();
        }
    }

    private T OwnSurface<T>(T resource) where T : IDisposable
    {
        surfaceOwned.Push(resource);
        return resource;
    }

    private (int Width, int Height) ReadFramebufferExtent()
    {
        var size = window.FramebufferSize;
        if (size.X <= 0 || size.Y <= 0)
        {
            size = window.Size;
        }
        return (size.X, size.Y);
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
