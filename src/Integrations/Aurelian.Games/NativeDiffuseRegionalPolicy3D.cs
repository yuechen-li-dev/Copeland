using System.Numerics;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Runtime.Rendering;

namespace Aurelian.Games;

/// <summary>Host-ticked policy bridge. Geometry/light changes conservatively dirty all regions
/// unless the caller supplies a conservative radiance-influence region set. No CPU rays or GPU wait.</summary>
public sealed class NativeDiffuseRegionalPolicy3D
{
    private readonly DiffuseProbeGrid grid;
    private VulkanSolid3DRenderer? owner;
    private NativeInstanceSet3D? previous;
    private LocalLight3D[] lights = [];
    private SkyIdentity? sky;
    private long compiledRevision;
    private long observed = -1;
    private long timed = -1;

    public NativeDiffuseRegionalPolicy3D(DiffuseProbeGrid grid, DiffuseRegionPartition3D? partition = null,
        DiffuseRegionalPolicySettings? settings = null, int traceCapacity = 2048)
    {
        this.grid = grid;
        Controller = new(grid, partition ?? new(), settings, traceCapacity);
    }

    public DiffuseRegionalPolicyController Controller { get; }
    public DiffuseRegionalPlan3D? LastPlan { get; private set; }

    public void BeforeFrame(VulkanSolid3DRenderer renderer, Native3DScene scene, TimeSpan elapsed,
        IReadOnlyList<int>? affectedRadianceRegions = null)
    {
        if (scene.Instances is not NativeInstanceSet3D current)
            throw new NotSupportedException("AUR-GI-POLICY-003: Regional policy requires the shared resident scene.");
        bool newOwner = !ReferenceEquals(owner, renderer);
        if (newOwner)
        {
            owner = renderer;
            previous = null;
            observed = -1;
            timed = -1;
            Controller.ResetObservationStream();
        }
        var settings = renderer.Settings;
        var nextSky = new SkyIdentity(settings.SunDirection, settings.SunColor, settings.SunIntensity,
            settings.Shadows, settings.EnvironmentIntensity, renderer.EnvironmentSource?.ContentKey,
            settings.DirectDiffuseModel, renderer.StaticEmissionIntensity,
            renderer.DynamicDiffuse.SecondarySkyLighting, renderer.DynamicDiffuse.SecondaryMaterials,
            renderer.DynamicDiffuse.SecondaryTextureLod, renderer.DynamicDiffuse.EnvironmentImportanceSampling);
        bool globalChange = sky != nextSky || compiledRevision != renderer.CompiledDiffuseRevision;
        bool sceneChange = !ReferenceEquals(previous, current)
            && (previous is null || !current.ContinuesFrom(previous) || current.HasRaySceneChanges);
        bool lightChange = !renderer.LocalLights.SequenceEqual(lights);
        if (globalChange) Controller.Invalidate();
        else if (sceneChange || lightChange || affectedRadianceRegions is not null) Controller.Invalidate(affectedRadianceRegions);
        sky = nextSky;
        compiledRevision = renderer.CompiledDiffuseRevision;
        previous = current;
        if (lightChange) lights = renderer.LocalLights.ToArray();
        if (renderer.CompletedDiffuseRegions is { } complete && complete.Sequence > observed)
        {
            Controller.Observe(complete);
            observed = complete.Sequence;
        }
        LastPlan = Controller.Tick(elapsed);
        renderer.DynamicDiffuse = renderer.DynamicDiffuse with
        {
            Mode = DynamicDiffuseMode3D.CompiledResidual, Grid = grid, RegionalPlan = LastPlan,
        };
    }

    public void AfterFrame(Native3DFrameResult result)
    {
        if (result.CompletedGpuFrame is not { } complete || complete.Sequence <= timed) return;
        timed = complete.Sequence;
        Controller.ObserveFrameCost((complete.EndTicks - complete.StartTicks) * complete.NanosecondsPerTick / 1_000_000);
    }

    private sealed record SkyIdentity(Vector3 Direction, Vector3 Color, float Intensity, bool Shadows,
        float EnvironmentIntensity, string? Source, DirectDiffuseModel3D Model, float Emission,
        bool SecondarySky, bool SecondaryMaterials, float TextureLod, bool Importance);
}
