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
using Aurelian.Runtime.Rendering;
using Aurelian.Graphics.Vulkan.RayQueries;
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
    private bool enableAdaptiveVisibility = true;
    private VulkanSolid3DRenderer? viewmodelRenderer;

    public bool EnableAdaptiveVisibility
    {
        get => enableAdaptiveVisibility;
        set
        {
            if (enableAdaptiveVisibility == value)
            {
                return;
            }
            enableAdaptiveVisibility = value;
            VisibilityPolicy.Controller.InvalidateMeasurements();
            if (!value)
            {
                Renderer.EnableHierarchicalZ = false;
                Renderer.LodPixelError = 0;
            }
        }
    }
    public NativeDiffuseRegionalPolicy3D? DiffusePolicy { get; set; }
    private CompiledDiffuseProbeField3D? diffuseField;

    public void SetCompiledDiffuseProbes(CompiledDiffuseProbeField3D field)
    {
        field.Validate();
        diffuseField = field with { Data = (float[])field.Data.Clone() };
        Renderer.SetCompiledDiffuseProbes(diffuseField);
        ActiveWorld.CompiledProbes = diffuseField;
    }

    public NativeVisibilityPolicy3D VisibilityPolicy { get; private set; } = null!;
    private long visibilityTick = System.Diagnostics.Stopwatch.GetTimestamp();

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
    private readonly bool enableFramesInFlight;
    private RenderScale3D renderScale;
    private readonly Dictionary<RenderScale3D, WorldRenderer> worldRenderers = [];
    private WorldRenderer ActiveWorld => worldRenderers[renderScale];
    private long? firstResolutionSubmission;
    private long lastResolutionSample;
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
        bool vSync = true, bool enableFramesInFlight = true, RenderScale3D renderScale = RenderScale3D.Native,
        double? dynamicGpuBudgetMilliseconds = null)
    {
        assets ??= new();
        this.assets = assets;
        this.window = window ?? throw new ArgumentNullException(nameof(window));
        this.title = title;
        this.visible = visible;
        this.enableFramesInFlight = enableFramesInFlight;
        this.renderScale = renderScale;
        if (dynamicGpuBudgetMilliseconds is { } budget)
        {
            if (renderScale != RenderScale3D.Native)
            {
                throw new ArgumentException("Dynamic resolution starts at native scale.", nameof(renderScale));
            }
            ResolutionPolicy = new(budget);
        }
        VSync = vSync;
        lightingShaderKey = LightingProgramIdentity.Compute(assets.Shader("SurfaceSolid3D.v.ts"));
        lighting = new LightingCompilationController(request =>
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            ulong fence = Renderer!.UploadStaticDiffuseLighting(pendingLighting
                ?? throw new InvalidDataException("NoValidatedLightingAsset"));
            ActiveWorld.StaticLighting = pendingLighting;
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
            VisibilityPolicy = new(Plant.Facts.HierarchicalZEnabled);
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

    // Resize replaces surface resources. Dynamic resolution can also replace the active world renderer.
    public AurelianVulkanSwapchain Swapchain { get; private set; } = null!;
    public VulkanNativeFrameTarget Target { get; private set; } = null!;
    public VulkanSolid3DRenderer Renderer { get; private set; } = null!;
    public VulkanNativeSwapchainPresenter Presenter { get; private set; } = null!;
    public GameMenuNativePresenter Menus { get; private set; } = null!;
    public AurelianNativeUiFont Font { get; }
    public bool VSync { get; private set; }
    public int Width => (int)Target.Width;
    public int Height => (int)Target.Height;
    public RenderScale3D ActiveRenderScale => renderScale;
    public ResolutionPolicyController? ResolutionPolicy { get; }
    public int ResolutionScaleChanges { get; private set; }
    public int ResidentRenderScales => worldRenderers.Count;

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
        ResolutionPolicy?.InvalidateMeasurements();
        firstResolutionSubmission = null;
        if (!EnableAdaptiveVisibility)
        {
            Renderer.EnableHierarchicalZ = false;
            Renderer.LodPixelError = 0;
        }
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
            ActiveWorld.PreparedModels = preparedModels.Count;
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

    public Native3DFrameResult Render(Native3DVertex[] vertices, Matrix4x4 camera, bool capture = false)
    {
        if (ResolutionPolicy is null)
        {
            return Renderer.Render(vertices, camera, Clear, capture);
        }
        return Render(new Native3DScene(vertices, []), camera, Vector3.Zero, capture);
    }

    public Native3DFrameResult Render(Native3DScene scene, Matrix4x4 camera, Vector3 eye, bool capture = false,
        NativeGpuGeometry3D? gpuGeometry = null)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        TimeSpan elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(visibilityTick, now);
        elapsed = elapsed > TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : elapsed;
        visibilityTick = now;
        if (ResolutionPolicy is { } resolution)
        {
            ApplyRenderScale(resolution.Tick(elapsed));
        }
        if (EnableAdaptiveVisibility)
            VisibilityPolicy.BeforeFrame(Renderer, scene, camera, elapsed);
        DiffusePolicy?.BeforeFrame(Renderer, scene, elapsed);
        var result = Renderer.Render(scene, camera, eye, Clear, capture, gpuGeometry);
        ObserveResolutionTiming(result);
        DiffusePolicy?.AfterFrame(result);
        if (EnableAdaptiveVisibility) VisibilityPolicy.AfterFrame(result);
        return result;
    }

    /// <summary>Load world color and draw a depth-tested foreground layer before HUD/menus.</summary>
    public Native3DFrameResult RenderViewmodel(NativeViewmodel3D viewmodel, bool capture = false)
    {
        ArgumentNullException.ThrowIfNull(viewmodel);
        viewmodelRenderer ??= OwnSurface(new VulkanSolid3DRenderer(Plant, assets.Shader("Solid3D.v.ts"), Target,
            preserveColor: true)
        {
            FrameMode = enableFramesInFlight ? Native3DFrameMode.Interactive : Native3DFrameMode.Capture,
        });
        viewmodelRenderer.Settings = Graphics3DSettings.Basic with { SunDirection = Settings.SunDirection };
        return viewmodelRenderer.Render(viewmodel.Geometry, viewmodel.Camera, Clear, capture, eye: viewmodel.Eye);
    }

    public string DescribeRendering()
    {
        string world = Renderer.CurrentFramePlan?.Describe() ?? "No 3D frame has rendered.";
        string foreground = viewmodelRenderer?.CurrentFramePlan?.Describe() ?? "No foreground layer.";
        string costs = System.Text.Json.JsonSerializer.Serialize(VisibilityPolicy.Controller.Costs);
        string resolution = ResolutionPolicy is { } policy
            ? FormattableString.Invariant($" BudgetMs={policy.BudgetMilliseconds:F2} GpuMs={policy.SmoothedGpuMilliseconds:F2} Switches={ResolutionScaleChanges} Samples={policy.AcceptedSamples}")
            : "";
        return $"World:\n{world}\nViewmodel:\n{foreground}\nRenderScale={renderScale} Raster={Renderer.RenderWidth}x{Renderer.RenderHeight}{resolution}\nAdaptiveQuality={EnableAdaptiveVisibility} "
            + $"Hzb={Renderer.EnableHierarchicalZ} LodPixelError={Renderer.LodPixelError}\nVisibilityCosts={costs}";
    }

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
        EnvironmentRadiance3D? EnvironmentSource,
        DynamicDiffuseLighting3D DynamicDiffuse,
        bool VisibilityBuffer,
        bool RayShadows,
        bool BindlessMaterials,
        bool HierarchicalZ,
        float LodPixelError,
        ClusterVisibilityMode VisibilityMode,
        LocalLightSampling3D LocalLightSampling,
        SceneAccelerationUpdateMode3D RaySceneUpdateMode,
        NativeRasterAdmissionMode3D RasterAdmissionMode,
        ReflectionProbe3D? ReflectionProbe,
        IReadOnlyList<LocalLight3D> LocalLights,
        VulkanParticles3D? ParticleSystem,
        IReadOnlyList<ParticleEmitter3D> ParticleEmitters,
        float StaticEmissionIntensity);

    private sealed class WorldRenderer(VulkanSolid3DRenderer renderer)
    {
        public VulkanSolid3DRenderer Renderer { get; } = renderer;
        public CompiledDiffuseProbeField3D? CompiledProbes { get; set; }
        public StaticDiffuseLighting? StaticLighting { get; set; }
        public int PreparedModels { get; set; }
    }

    private RenderState CaptureRenderState()
    {
        return new RenderState(Renderer.Settings, Renderer.Environment, Renderer.EnvironmentSource, Renderer.DynamicDiffuse,
            Renderer.EnableVisibilityBuffer, Renderer.EnableRayShadows,
            Renderer.EnableBindlessMaterials, Renderer.EnableHierarchicalZ, Renderer.LodPixelError, Renderer.VisibilityMode,
            Renderer.LocalLightSampling, Renderer.RaySceneUpdateMode, Renderer.RasterAdmissionMode,
            Renderer.ReflectionProbe, Renderer.LocalLights,
            Renderer.ParticleSystem, Renderer.ParticleEmitters, Renderer.StaticEmissionIntensity);
    }

    private void RestoreRenderState(RenderState state)
    {
        Renderer.Settings = state.Settings;
        Renderer.Environment = state.Environment;
        Renderer.EnvironmentSource = state.EnvironmentSource;
        Renderer.DynamicDiffuse = state.DynamicDiffuse;
        Renderer.EnableVisibilityBuffer = state.VisibilityBuffer;
        Renderer.EnableRayShadows = state.RayShadows;
        Renderer.EnableBindlessMaterials = state.BindlessMaterials;
        Renderer.EnableHierarchicalZ = state.HierarchicalZ;
        Renderer.LodPixelError = state.LodPixelError;
        Renderer.VisibilityMode = state.VisibilityMode;
        Renderer.LocalLightSampling = state.LocalLightSampling;
        Renderer.RaySceneUpdateMode = state.RaySceneUpdateMode;
        Renderer.RasterAdmissionMode = state.RasterAdmissionMode;
        if (diffuseField is not null && !ReferenceEquals(ActiveWorld.CompiledProbes, diffuseField))
        {
            Renderer.SetCompiledDiffuseProbes(diffuseField);
            ActiveWorld.CompiledProbes = diffuseField;
        }
        Renderer.ReflectionProbe = state.ReflectionProbe;
        Renderer.LocalLights = state.LocalLights;
        Renderer.ParticleSystem = state.ParticleSystem;
        Renderer.ParticleEmitters = state.ParticleEmitters;
        Renderer.StaticEmissionIntensity = state.StaticEmissionIntensity;
        PrepareWorldModels();
        if (pendingLighting is not null && !ReferenceEquals(ActiveWorld.StaticLighting, pendingLighting))
        {
            Renderer.UploadStaticDiffuseLighting(pendingLighting);
            ActiveWorld.StaticLighting = pendingLighting;
        }
        Renderer.PublishStaticDiffuseLighting(lightingPublished);
    }

    private void PrepareWorldModels()
    {
        var world = ActiveWorld;
        while (world.PreparedModels < preparedModels.Count)
        {
            Renderer.Prepare(preparedModels[world.PreparedModels]);
            world.PreparedModels++;
        }
    }

    private void ApplyRenderScale(RenderScale3D scale)
    {
        if (scale == renderScale)
        {
            return;
        }
        var state = CaptureRenderState();
        if (state.ParticleSystem is not null)
        {
            // The shared particle pool's CPU-written slots are indexed by the active renderer's frame ring.
            Renderer.WaitForPendingFrames();
        }
        Renderer = worldRenderers[scale].Renderer;
        renderScale = scale;
        RestoreRenderState(state);
        // Correspondence from a dormant renderer predates the last presented frame.
        Renderer.ResetTemporalHistory();
        firstResolutionSubmission = null;
        ResolutionScaleChanges++;
    }

    private void ObserveResolutionTiming(Native3DFrameResult result)
    {
        if (ResolutionPolicy is null || result.CpuTimings is not { } submitted)
        {
            return;
        }
        if (firstResolutionSubmission is null)
        {
            firstResolutionSubmission = submitted.Sequence;
            lastResolutionSample = submitted.Sequence - 1;
        }
        // Old ring slots may complete after a cached renderer becomes active again.
        // Skip those observations and the first six frames while this scale settles.
        if (result.CompletedGpuFrame is { } completed
            && completed.Sequence >= firstResolutionSubmission.Value + 6 && completed.Sequence > lastResolutionSample)
        {
            lastResolutionSample = completed.Sequence;
            ResolutionPolicy.Observe(completed.ElapsedMilliseconds);
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
            Renderer = CreateWorldRenderer(renderScale).Renderer;
            if (ResolutionPolicy is not null)
            {
                foreach (RenderScale3D scale in Enum.GetValues<RenderScale3D>())
                {
                    if (scale != renderScale)
                    {
                        CreateWorldRenderer(scale);
                    }
                }
            }
            Presenter = OwnSurface(new VulkanNativeSwapchainPresenter(Plant, Target, Swapchain, enableFramesInFlight: enableFramesInFlight));
            Menus = OwnSurface(new GameMenuNativePresenter(Plant, Target,
                assets.Shader("AnalyticShape2D.v.ts"), assets.Shader("MsdfText.v.ts"), Font)
            {
                EnableFramesInFlight = enableFramesInFlight,
            });
            Layers.Retarget(Target);
        }
        catch
        {
            DisposeSurfaceResources();
            throw;
        }
    }

    private WorldRenderer CreateWorldRenderer(RenderScale3D scale)
    {
        var renderer = new VulkanSolid3DRenderer(Plant, assets.Shader("SurfaceSolid3D.v.ts"), Target,
            modelProgram: assets.Shader("SurfaceModel3D.v.ts"), shadowProgram: assets.Shader("Shadow3D.v.ts"),
            outputProgram: assets.Shader("ToneMap3D.v.ts"), temporalProgram: assets.Shader("TemporalResolve3D.v.ts"),
            bloomProgram: assets.Shader("Bloom3D.v.ts"),
            surfacePrograms: new(assets.Shader("SurfaceResolve3D.v.ts"), assets.Shader("AmbientOcclusion3D.v.ts"),
                assets.Shader("AmbientDenoise3D.v.ts"), assets.Shader("LightTiles3D.v.ts"))
            {
                LocalRayShadows = Plant.Facts.EnabledDeviceExtensions.Contains("VK_KHR_ray_query", StringComparer.Ordinal)
                    ? assets.ComputeShader("LocalShadows3D.v.ts") : null,
                DynamicDiffuse = Plant.Facts.EnabledDeviceExtensions.Contains("VK_KHR_ray_query", StringComparer.Ordinal)
                    ? assets.ComputeShader("DynamicDiffuse3D.v.ts") : null,
                SharpReflections = Plant.Facts.EnabledDeviceExtensions.Contains("VK_KHR_ray_query", StringComparer.Ordinal)
                    ? assets.ComputeShader("SharpReflection3D.v.ts") : null,
                GlossyReflectionResolve = Plant.Facts.EnabledDeviceExtensions.Contains("VK_KHR_ray_query", StringComparer.Ordinal)
                    ? assets.Shader("GlossyReflectionResolve3D.v.ts") : null,
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
            }, retainedPrograms: new(assets.Shader("InstancedModel3D.v.ts"), assets.Shader("InstancedShadow3D.v.ts"))
            {
                Visibility = new(assets.ComputeShader("VisibilityCull3D.v.ts"), assets.ComputeShader("VisibilityScan3D.v.ts"),
                    assets.ComputeShader("VisibilityScatter3D.v.ts")),
                Bindless = assets.Shader("BindlessModel3D.v.ts"),
                VisibilityBuffer = new(assets.Shader("VisibilityRaster3D.v.ts"), assets.Shader("VisibilityResolve3D.v.ts"))
                {
                    AnalyticRaster = assets.Shader("AnalyticRaster3D.v.ts"),
                    HybridResolve = assets.Shader("HybridResolve3D.v.ts"),
                },
                HierarchicalZ = new(assets.ComputeShader("HzbCull3D.v.ts"), assets.ComputeShader("HzbReduce3D.v.ts")),
            }, renderScale: scale)
        {
            FrameMode = enableFramesInFlight ? Native3DFrameMode.Interactive : Native3DFrameMode.Capture,
        };
        var world = new WorldRenderer(OwnSurface(renderer));
        worldRenderers.Add(scale, world);
        return world;
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
        worldRenderers.Clear();
        viewmodelRenderer = null;
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
